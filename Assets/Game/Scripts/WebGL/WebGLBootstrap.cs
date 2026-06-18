using UnityEngine;

namespace TPD.Arena
{
    /// <summary>
    /// Reads arena/session query params when running as Telegram Mini App WebGL build.
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

        private void Start()
        {
            string url = Application.absoluteURL;
            if (string.IsNullOrEmpty(url))
                return;

            string arena = ReadQueryParam(url, "arena");
            string session = ReadQueryParam(url, "session");

            if (!string.IsNullOrEmpty(arena))
                Debug.Log($"[WebGL] arena={arena}");
            if (!string.IsNullOrEmpty(session))
                Debug.Log($"[WebGL] session={session}");
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
                    return System.Uri.UnescapeDataString(part.Substring(prefix.Length));
            }

            return null;
        }
    }
}
