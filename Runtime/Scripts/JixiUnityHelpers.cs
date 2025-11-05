using UnityEngine;
using UnityEngine.Networking;

namespace JixiAI
{
    public static class JixiUnityHelpers
    {
        public static void LoadRenderTextureFromUrl(string url, System.Action<RenderTexture> onDone)
        {
            EnsureRunner();
            JixiCoroutineHost.Start(LoadRT(url, onDone));
        }

        private static System.Collections.IEnumerator LoadRT(string url, System.Action<RenderTexture> cb)
        {
            using (var req = UnityWebRequestTexture.GetTexture(url))
            {
                yield return req.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
                if (req.result != UnityWebRequest.Result.Success)
#else
                if (req.isNetworkError || req.isHttpError)
#endif
                { cb?.Invoke(null); yield break; }

                var tex2D = DownloadHandlerTexture.GetContent(req);
                var rt = new RenderTexture(tex2D.width, tex2D.height, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(tex2D, rt);
                cb?.Invoke(rt);
            }
        }

        private static void EnsureRunner() => JixiCoroutineHost.Ensure();

        private class JixiCoroutineHost : MonoBehaviour
        {
            private static JixiCoroutineHost _inst;
            public static void Ensure()
            {
                if (_inst) return;
                var go = GameObject.Find("JixiRunner") ?? new GameObject("JixiRunner");
                Object.DontDestroyOnLoad(go);
                _inst = go.GetComponent<JixiCoroutineHost>() ?? go.AddComponent<JixiCoroutineHost>();
            }
            public static void Start(System.Collections.IEnumerator r)
            {
                Ensure(); _inst.StartCoroutine(r);
            }
        }
    }
}