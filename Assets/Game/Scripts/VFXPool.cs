using System.Collections.Generic;
using UnityEngine;

namespace TPD.Arena
{
    public class VFXPool
    {
        private readonly Dictionary<GameObject, Queue<GameObject>> pools = new Dictionary<GameObject, Queue<GameObject>>();

        public static GameObject InstantiatePrefab(Object prefab, Transform parent, Vector3 localPosition, Vector3 localScale)
        {
            if (prefab == null)
                return null;

            Object clone;
            try
            {
                clone = Object.Instantiate(prefab, parent, false);
            }
            catch (System.InvalidCastException)
            {
                return null;
            }

            GameObject obj = ResolveGameObject(clone);
            if (obj == null)
                return null;

            obj.transform.localPosition = localPosition;
            obj.transform.localRotation = Quaternion.identity;
            obj.transform.localScale = localScale;
            return obj;
        }

        public static GameObject InstantiatePrefab(Object prefab, Vector3 worldPosition, Quaternion rotation)
        {
            if (prefab == null)
                return null;

            Object clone;
            try
            {
                clone = Object.Instantiate(prefab, worldPosition, rotation);
            }
            catch (System.InvalidCastException)
            {
                return null;
            }

            return ResolveGameObject(clone);
        }

        private static GameObject ResolveGameObject(Object clone)
        {
            if (clone is GameObject go)
                return go;
            if (clone is Component component)
                return component.gameObject;
            if (clone != null)
                Object.Destroy(clone);
            return null;
        }

        public GameObject Get(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            if (prefab == null) return null;

            if (!pools.TryGetValue(prefab, out Queue<GameObject> queue))
            {
                queue = new Queue<GameObject>();
                pools[prefab] = queue;
            }

            GameObject obj;
            if (queue.Count > 0)
            {
                obj = queue.Dequeue();
                obj.transform.SetPositionAndRotation(position, rotation);
                obj.SetActive(true);
            }
            else
            {
                obj = InstantiatePrefab(prefab, position, rotation);
                if (obj == null)
                    return null;
            }

            RestartParticleSystems(obj);
            return obj;
        }

        public void Return(GameObject prefab, GameObject obj)
        {
            if (obj == null || prefab == null) return;
            obj.SetActive(false);
            if (!pools.TryGetValue(prefab, out Queue<GameObject> queue))
            {
                queue = new Queue<GameObject>();
                pools[prefab] = queue;
            }
            queue.Enqueue(obj);
        }

        private static void RestartParticleSystems(GameObject obj)
        {
            foreach (var ps in obj.GetComponentsInChildren<ParticleSystem>(true))
            {
                ps.Clear(true);
                ps.Play(true);
            }
        }

        public static float GetParticleLifetime(GameObject prefab, float maxLifetime = -1f)
        {
            if (prefab == null) return 2f;

            float max = 0f;
            foreach (ParticleSystem ps in prefab.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = ps.main;
                float startLife = GetMaxStartLifetime(main);
                float life = main.duration + startLife;
                if (life > max)
                    max = life;
            }

            if (max <= 0f)
                max = 2f;

            if (maxLifetime > 0f)
                max = Mathf.Min(max, maxLifetime);

            return Mathf.Clamp(max, 0.5f, 8f);
        }

        private static float GetMaxStartLifetime(ParticleSystem.MainModule main)
        {
            ParticleSystem.MinMaxCurve curve = main.startLifetime;
            switch (curve.mode)
            {
                case ParticleSystemCurveMode.Constant:
                    return curve.constant;
                case ParticleSystemCurveMode.TwoConstants:
                    return curve.constantMax;
                default:
                    return Mathf.Max(curve.constant, curve.constantMax);
            }
        }
    }
}
