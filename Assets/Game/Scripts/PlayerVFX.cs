using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

namespace TPD.Arena
{
    public class PlayerVFX : MonoBehaviour
    {
        private const float FloatingTextDuration = 1.2f;

        [SerializeField] GameObject damageTextPrefab;
        [SerializeField] GameObject stunVFXPrefab;
        [SerializeField] Vector3 stunLocalPos = new Vector3(0, 1.25f, -1);
        [SerializeField] Vector3 stunLocalScale = new Vector3(0.5f, 0.5f, 0.5f);

        private GameObject activeStunVFX;
        private GameObject activeCastVfx;
        private Coroutine activeCastVfxCoroutine;
        private VFXPool vfxPool;
        private readonly List<Coroutine> activeCoroutines = new List<Coroutine>();
        private readonly List<ExportFloater> exportFloaters = new List<ExportFloater>();

        private struct ExportAbilityVfxEntry
        {
            public long eventKey;
            public GameObject obj;
            public float eventTimestamp;
            public float maxLifetime;
            public bool renderOnTop;
        }

        private readonly List<ExportAbilityVfxEntry> exportAbilityVfx = new List<ExportAbilityVfxEntry>();
        private readonly List<GameObject> activeAbilityVfx = new List<GameObject>();
        private float lastExportScrubTime = -1f;

        private struct ExportFloater
        {
            public GameObject obj;
            public GameObject prefab;
        }

        private void Awake()
        {
            vfxPool = new VFXPool();
        }

        public void SpawnDamageText(int damage) => SpawnFloatingText(damage.ToString(), Color.red);
        public void SpawnHealText(int heal) => SpawnFloatingText($"+{heal} hp", Color.green);
        public void SpawnShieldText(int shield) => SpawnFloatingText($"+{shield} es", Color.cyan);

        public void ShowStun()
        {
            if (activeStunVFX == null && stunVFXPrefab != null)
            {
                activeStunVFX = VFXPool.InstantiatePrefab(stunVFXPrefab, transform, stunLocalPos, stunLocalScale);
                ApplyRenderOnTop(activeStunVFX);
            }
        }

        public void HideStun()
        {
            if (activeStunVFX == null)
                return;

            ClearParticleSystems(activeStunVFX);
            Destroy(activeStunVFX);
            activeStunVFX = null;
        }

        public void SyncStun(bool isStunned, float scrubParticleTime = -1f, float exportSimStep = 0f)
        {
            if (!isStunned)
            {
                HideStun();
                return;
            }

            ShowStun();
            if (scrubParticleTime < 0f || activeStunVFX == null)
                return;

            float step = exportSimStep > 0f ? exportSimStep : 1f / 60f;
            ScrubParticleSystemsToAge(activeStunVFX, scrubParticleTime, step);
            PauseParticleSystems(activeStunVFX);
        }

        public void SyncExportTransientVfx(float time, List<TimelineEvent> events, int playerIdx)
        {
            ReturnExportFloaters();
            if (damageTextPrefab == null || events == null)
                return;

            foreach (TimelineEvent ev in events)
            {
                if (ev.timestamp > time)
                    break;

                if (ev.targetPlayer != playerIdx)
                    continue;

                float age = time - ev.timestamp;
                if (age < 0f || age > FloatingTextDuration)
                    continue;

                string text = null;
                Color color = Color.white;
                switch (ev.eventType)
                {
                    case TimelineEventType.Hit when ev.damage > 0:
                        text = ev.damage.ToString();
                        color = Color.red;
                        break;
                    case TimelineEventType.HealApplied:
                        text = $"+{ev.damage} hp";
                        color = Color.green;
                        break;
                    case TimelineEventType.ShieldApplied:
                        text = $"+{ev.damage} es";
                        color = Color.cyan;
                        break;
                }

                if (text == null)
                    continue;

                ShowExportFloater(text, color, age, ev.timestamp);
            }
        }

        public void SyncExportAbilityVfx(
            float time,
            List<TimelineEvent> events,
            int playerIdx,
            Func<int, string, AbilityDataSO> resolveAbility,
            float exportSimStep)
        {
            if (events == null || resolveAbility == null)
            {
                ReturnExportAbilityVfx();
                return;
            }

            const float scrubEpsilon = 0.0001f;
            if (lastExportScrubTime >= 0f && time < lastExportScrubTime - scrubEpsilon)
                ReturnExportAbilityVfx();

            lastExportScrubTime = time;
            float simStep = exportSimStep > 0f ? exportSimStep : 1f / 30f;
            PruneExpiredExportAbilityVfx(time);

            for (int i = 0; i < events.Count; i++)
            {
                TimelineEvent ev = events[i];
                if (ev.timestamp > time)
                    break;

                float age = time - ev.timestamp;
                if (age < 0f)
                    continue;

                if (ev.actorPlayer == playerIdx)
                {
                    if (ev.eventType == TimelineEventType.StartCasting
                        || ev.eventType == TimelineEventType.ResumeCasting)
                    {
                        AbilityDataSO ability = resolveAbility(ev.actorPlayer, ev.abilityName);
                        if (ability != null && (ability.type == AbilityType.Heal || ability.type == AbilityType.Shield))
                            continue;
                        Vector3 offset = ability != null ? ability.hitVfxOffset : Vector3.up;
                        Vector3 scale = ResolveVfxScale(ability != null ? ability.vfxLocalScale : Vector3.zero);
                        GameObject prefab = ability != null ? ability.castVfxPrefab : null;
                        float castEnd = FindCastEndTime(events, ev, i, ability);
                        float maxAge = Mathf.Min(castEnd - ev.timestamp,
                            VFXPool.GetParticleLifetime(prefab, ability != null ? ability.castTime : -1f));
                        SyncExportAbilityVfxInstance(
                                MakeExportVfxEventKey(ev),
                                prefab,
                                offset,
                                scale,
                                ev.timestamp,
                                age,
                                maxAge,
                                simStep,
                                null,
                                true);
                    }
                }

                if (ev.targetPlayer != playerIdx)
                    continue;

                switch (ev.eventType)
                {
                    case TimelineEventType.Hit when ev.damage > 0:
                    case TimelineEventType.HealApplied:
                    case TimelineEventType.ShieldApplied:
                    {
                        AbilityDataSO ability = resolveAbility(ev.actorPlayer, ev.abilityName);
                        Vector3 offset = ability != null ? ability.hitVfxOffset : Vector3.up;
                        if (ev.eventType == TimelineEventType.HealApplied || ev.eventType == TimelineEventType.ShieldApplied)
                            offset = GetSupportEffectOffset(offset);
                        Vector3 scale = ResolveVfxScale(ability != null ? ability.vfxLocalScale : Vector3.zero);
                        SyncExportAbilityVfxInstance(
                                MakeExportVfxEventKey(ev),
                                ability != null ? ability.hitVfxPrefab : null,
                                offset,
                                scale,
                                ev.timestamp,
                                age,
                                VFXPool.GetParticleLifetime(ability != null ? ability.hitVfxPrefab : null),
                                simStep,
                                ability != null ? ability.displayColor : (Color?)null,
                                true);
                        break;
                    }
                }
            }
        }

        private static long MakeExportVfxEventKey(TimelineEvent ev) =>
            HashCode.Combine(
                (int)ev.eventType,
                BitConverter.SingleToInt32Bits(ev.timestamp),
                ev.actorPlayer,
                ev.targetPlayer,
                ev.castSessionId);

        private void PruneExpiredExportAbilityVfx(float time)
        {
            for (int i = exportAbilityVfx.Count - 1; i >= 0; i--)
            {
                ExportAbilityVfxEntry entry = exportAbilityVfx[i];
                if (time - entry.eventTimestamp > entry.maxLifetime)
                {
                    if (entry.obj != null)
                        DestroyImmediate(entry.obj);
                    exportAbilityVfx.RemoveAt(i);
                }
            }
        }

        private void SyncExportAbilityVfxInstance(
            long eventKey,
            GameObject prefab,
            Vector3 localOffset,
            Vector3 localScale,
            float eventTimestamp,
            float age,
            float maxLifetime,
            float simStep,
            Color? tint,
            bool renderOnTop)
        {
            if (prefab == null || age > maxLifetime)
                return;

            int entryIndex = FindExportAbilityVfxIndex(eventKey);
            GameObject activeObj;
            if (entryIndex < 0)
            {
                activeObj = SpawnAbilityVfxObject(prefab, localOffset, localScale, tint, renderOnTop);
                if (activeObj == null)
                    return;

                exportAbilityVfx.Add(new ExportAbilityVfxEntry
                {
                    eventKey = eventKey,
                    obj = activeObj,
                    eventTimestamp = eventTimestamp,
                    maxLifetime = maxLifetime,
                    renderOnTop = renderOnTop
                });
            }
            else
            {
                activeObj = exportAbilityVfx[entryIndex].obj;
                if (activeObj == null)
                    return;
            }

            ScrubParticleSystemsToAge(activeObj, age, simStep);
            PauseParticleSystems(activeObj);
        }

        private int FindExportAbilityVfxIndex(long eventKey)
        {
            for (int i = 0; i < exportAbilityVfx.Count; i++)
            {
                if (exportAbilityVfx[i].eventKey == eventKey)
                    return i;
            }

            return -1;
        }

        private static float FindCastEndTime(
            List<TimelineEvent> events,
            TimelineEvent castStart,
            int startIndex,
            AbilityDataSO ability)
        {
            for (int j = startIndex + 1; j < events.Count; j++)
            {
                TimelineEvent ev = events[j];
                if (ev.castSessionId != castStart.castSessionId)
                    continue;

                if (ev.eventType == TimelineEventType.CastFinished
                    || ev.eventType == TimelineEventType.CastInterrupted)
                    return ev.timestamp;
            }

            float castTime = ability != null ? ability.castTime : 2f;
            if (castStart.eventType == TimelineEventType.ResumeCasting && castStart.remainingCastTime > 0f)
                castTime = castStart.remainingCastTime;

            return castStart.timestamp + castTime;
        }

        private static Vector3 ResolveVfxScale(Vector3 scale) =>
            scale.sqrMagnitude < 0.0001f ? new Vector3(0.525f, 0.525f, 0.525f) : scale;

        private void ReturnExportAbilityVfx()
        {
            foreach (ExportAbilityVfxEntry entry in exportAbilityVfx)
            {
                if (entry.obj != null)
                    DestroyImmediate(entry.obj);
            }

            exportAbilityVfx.Clear();
            lastExportScrubTime = -1f;
        }

        private void ShowExportFloater(string text, Color color, float age, float seed)
        {
            Vector3 startPos = transform.position + Vector3.up * 2f;
            float offsetX = Mathf.Sin(seed * 17.31f) * 0.5f;
            Vector3 targetPos = startPos + new Vector3(offsetX, 1.5f, 0f);
            float t = Mathf.Clamp01(age / FloatingTextDuration);

            GameObject obj = vfxPool.Get(damageTextPrefab, startPos, Quaternion.identity);
            if (obj == null)
                return;

            TextMeshProUGUI tmp = obj.GetComponent<TextMeshProUGUI>();
            if (tmp != null)
            {
                tmp.text = text;
                Color c = color;
                c.a = Mathf.Lerp(1f, 0f, t);
                tmp.color = c;
            }

            obj.transform.position = Vector3.Lerp(startPos, targetPos, t);
            exportFloaters.Add(new ExportFloater { obj = obj, prefab = damageTextPrefab });
        }

        private void ReturnExportFloaters()
        {
            foreach (ExportFloater floater in exportFloaters)
            {
                if (floater.obj != null)
                    vfxPool.Return(floater.prefab, floater.obj);
            }

            exportFloaters.Clear();
        }

        private static void ScrubParticleSystemsToAge(GameObject root, float age, float stepSeconds)
        {
            stepSeconds = Mathf.Max(stepSeconds, 1f / 240f);
            foreach (ParticleSystem ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                if (age <= 0f)
                    continue;

                ps.Play(true);
                float simulated = 0f;
                while (simulated + stepSeconds <= age)
                {
                    ps.Simulate(stepSeconds, true, false, false);
                    simulated += stepSeconds;
                }

                float remainder = age - simulated;
                if (remainder > 0f)
                    ps.Simulate(remainder, true, false, false);
            }
        }

        private static void ClearParticleSystems(GameObject root)
        {
            foreach (ParticleSystem ps in root.GetComponentsInChildren<ParticleSystem>(true))
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private static void PauseParticleSystems(GameObject root)
        {
            foreach (ParticleSystem ps in root.GetComponentsInChildren<ParticleSystem>(true))
                ps.Pause(true);
        }

        private static void RestartParticleSystems(GameObject root)
        {
            foreach (ParticleSystem ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                ps.Clear(true);
                ps.Play(true);
            }
        }

        private static void ApplyParticleTint(GameObject root, Color tint)
        {
            foreach (ParticleSystem ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = ps.main;
                main.startColor = new ParticleSystem.MinMaxGradient(tint);
            }

            foreach (ParticleSystemRenderer renderer in root.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                Material[] materials = renderer.materials;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material mat = materials[i];
                    if (mat == null)
                        continue;

                    if (mat.HasProperty("_TintColor"))
                    {
                        mat.SetColor("_TintColor", new Color(
                            tint.r,
                            Mathf.Max(tint.g, tint.r * 0.25f),
                            Mathf.Max(tint.b, tint.r * 0.1f),
                            0.85f));
                    }

                    if (mat.HasProperty("_Color"))
                        mat.SetColor("_Color", tint);
                }
            }
        }

        private GameObject SpawnAbilityVfxObject(
            GameObject prefab,
            Vector3 localOffset,
            Vector3 localScale,
            Color? tint = null,
            bool renderOnTop = true)
        {
            if (prefab == null)
                return null;

            GameObject obj = VFXPool.InstantiatePrefab(prefab, transform, localOffset, localScale);
            if (obj != null && tint.HasValue)
                ApplyParticleTint(obj, tint.Value);
            if (obj != null && renderOnTop)
            {
                ApplyRenderOnTop(obj);
                PushTowardCamera(obj.transform, 0.45f);
            }

            return obj;
        }

        public void PlayCastVFX(GameObject prefab, Vector3 localOffset, Vector3 localScale, float maxLifetime)
        {
            StopCastVFX();
            GameObject obj = SpawnAbilityVfxObject(prefab, localOffset, ResolveVfxScale(localScale));
            if (obj == null)
                return;

            RestartParticleSystems(obj);
            activeCastVfx = obj;
            activeAbilityVfx.Add(obj);

            float lifetime = VFXPool.GetParticleLifetime(prefab, maxLifetime);
            activeCastVfxCoroutine = StartCoroutine(DestroyAbilityVfxAfter(obj, lifetime, true));
        }

        public void StopCastVFX()
        {
            if (activeCastVfxCoroutine != null)
            {
                StopCoroutine(activeCastVfxCoroutine);
                activeCastVfxCoroutine = null;
            }

            if (activeCastVfx == null)
                return;

            DestroyAbilityVfxObject(activeCastVfx);
            activeCastVfx = null;
        }

        public void PlayAbilityVFX(
            GameObject prefab,
            Vector3 localOffset,
            Vector3 localScale,
            float maxLifetime = -1f,
            Color? tint = null,
            bool renderOnTop = true)
        {
            GameObject obj = SpawnAbilityVfxObject(
                prefab,
                localOffset,
                ResolveVfxScale(localScale),
                tint,
                renderOnTop);
            if (obj == null)
                return;

            RestartParticleSystems(obj);
            activeAbilityVfx.Add(obj);

            float lifetime = VFXPool.GetParticleLifetime(prefab, maxLifetime);
            activeCoroutines.Add(StartCoroutine(DestroyAbilityVfxAfter(obj, lifetime, false)));
        }

        private IEnumerator DestroyAbilityVfxAfter(GameObject obj, float delay, bool isCastVfx)
        {
            yield return new WaitForSeconds(delay);
            if (obj == null)
                yield break;

            DestroyAbilityVfxObject(obj);
            if (isCastVfx && activeCastVfx == obj)
            {
                activeCastVfx = null;
                activeCastVfxCoroutine = null;
            }
        }

        private void DestroyAbilityVfxObject(GameObject obj)
        {
            if (obj == null)
                return;

            ClearParticleSystems(obj);
            activeAbilityVfx.Remove(obj);
            Destroy(obj);
        }

        private void SpawnFloatingText(string text, Color color)
        {
            if (damageTextPrefab == null) return;
            Vector3 pos = transform.position + Vector3.up * 2f;
            GameObject obj = vfxPool.Get(damageTextPrefab, pos, Quaternion.identity);
            if (obj == null) return;

            TextMeshProUGUI tmp = obj.GetComponent<TextMeshProUGUI>();
            if (tmp)
            {
                tmp.text = text;
                tmp.color = color;
            }
            activeCoroutines.Add(StartCoroutine(FloatingTextRoutine(damageTextPrefab, obj, tmp)));
        }

        private IEnumerator FloatingTextRoutine(GameObject prefab, GameObject obj, TextMeshProUGUI tmp)
        {
            float elapsed = 0;
            Vector3 startPos = obj.transform.position;
            Vector3 targetPos = startPos + new Vector3(UnityEngine.Random.Range(-0.5f, 0.5f), 1.5f, 0);
            Color orig = tmp ? tmp.color : Color.white;
            while (elapsed < FloatingTextDuration && obj != null)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / FloatingTextDuration;
                obj.transform.position = Vector3.Lerp(startPos, targetPos, t);
                if (tmp) { Color c = orig; c.a = Mathf.Lerp(1, 0, t); tmp.color = c; }
                yield return null;
            }
            if (obj != null) vfxPool.Return(prefab, obj);
        }

        public Vector3 GetSupportEffectOffset(Vector3 originalOffset)
        {
            return new Vector3(originalOffset.x, 0f, originalOffset.z);
        }

        private static void ApplyRenderOnTop(GameObject root)
        {
            if (root == null)
                return;

            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                renderer.sortingOrder = 5000;
                if (renderer is ParticleSystemRenderer particleRenderer)
                    particleRenderer.sortingFudge = 8f;

                Material[] materials = renderer.materials;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material mat = materials[i];
                    if (mat == null)
                        continue;

                    if (mat.renderQueue < 5000)
                        mat.renderQueue = 5000;
                    if (mat.HasProperty("_ZTest"))
                        mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                    if (mat.HasProperty("_ZWrite"))
                        mat.SetInt("_ZWrite", 0);
                }
            }
        }

        private static void PushTowardCamera(Transform effectRoot, float distance)
        {
            if (effectRoot == null || distance <= 0f)
                return;

            Camera cam = Camera.main;
            if (cam == null)
                return;

            Vector3 toCamera = cam.transform.position - effectRoot.position;
            if (toCamera.sqrMagnitude < 0.0001f)
                return;

            effectRoot.position += toCamera.normalized * distance;
        }

        public void ClearVFX()
        {
            HideStun();
            StopCastVFX();
            ReturnExportFloaters();
            ReturnExportAbilityVfx();
            foreach (GameObject obj in activeAbilityVfx)
            {
                if (obj != null)
                    Destroy(obj);
            }
            activeAbilityVfx.Clear();
            foreach (Coroutine coroutine in activeCoroutines)
            {
                if (coroutine != null) StopCoroutine(coroutine);
            }
            activeCoroutines.Clear();
        }
    }
}
