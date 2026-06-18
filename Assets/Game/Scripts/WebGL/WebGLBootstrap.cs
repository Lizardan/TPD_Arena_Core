using System;
using System.Collections;
using System.Text;
using UnityEngine;

namespace TPD.Arena
{
    /// <summary>
    /// Reads battle JSON from URL when running as Telegram Mini App WebGL build.
    /// </summary>
    public class WebGLBootstrap : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            var go = new GameObject(nameof(WebGLBootstrap));
            go.AddComponent<WebGLBootstrap>();
            DontDestroyOnLoad(go);
#endif
        }

        private IEnumerator Start()
        {
            yield return null;

            string url = Application.absoluteURL;
            if (string.IsNullOrEmpty(url))
                yield break;

            string battleParam = ReadQueryParam(url, "battle");
            if (string.IsNullOrEmpty(battleParam))
            {
                Debug.Log("[WebGL] No battle param in URL — manual mode.");
                yield break;
            }

            string json = DecodeBase64Url(battleParam);
            if (string.IsNullOrEmpty(json))
            {
                Debug.LogError("[WebGL] Failed to decode battle param.");
                yield break;
            }

            BattleRequestJson request = BattleRequestJson.Parse(json, out string error);
            if (request == null)
            {
                Debug.LogError($"[WebGL] {error}");
                yield break;
            }

            string arena = ReadQueryParam(url, "arena");
            string host = ReadQueryParam(url, "host");
            if (!string.IsNullOrEmpty(arena))
                Debug.Log($"[WebGL] arena={arena} host={host}");

            BattleController controller = FindFirstObjectByType<BattleController>();
            if (controller == null)
            {
                Debug.LogError("[WebGL] BattleController not found.");
                yield break;
            }

            controller.RunFromTelegramRequest(request);
        }

        private static string ReadQueryParam(string absoluteUrl, string key)
        {
            int queryIndex = absoluteUrl.IndexOf('?');
            if (queryIndex < 0)
                return null;

            string query = absoluteUrl.Substring(queryIndex + 1);
            string[] parts = query.Split('&');
            string prefix = key + "=";
            foreach (string part in parts)
            {
                if (part.StartsWith(prefix))
                    return Uri.UnescapeDataString(part.Substring(prefix.Length));
            }

            return null;
        }

        private static string DecodeBase64Url(string value)
        {
            if (string.IsNullOrEmpty(value))
                return null;

            try
            {
                string padded = value.Replace('-', '+').Replace('_', '/');
                switch (padded.Length % 4)
                {
                    case 2: padded += "=="; break;
                    case 3: padded += "="; break;
                }

                byte[] bytes = Convert.FromBase64String(padded);
                return Encoding.UTF8.GetString(bytes);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[WebGL] base64 decode failed: {ex.Message}");
                return null;
            }
        }
    }
}
