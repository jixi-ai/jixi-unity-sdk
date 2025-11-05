using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace JixiAI
{
    public class Jixi
    {
        private static readonly HttpClient httpClient = new HttpClient();
        private static readonly Jixi instance = new Jixi();

        private string apiKey;
        private List<JixiWorkflow> workflows = new();
        private Dictionary<string, string> workflowMap = new(StringComparer.Ordinal);

        private ConcurrentQueue<Action> mainThreadActions = new();

        private static JixiRunner runner;
        private static bool bootstrapped;

        private Jixi() { }

        public static Jixi Instance => instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Bootstrap()
        {
            if (bootstrapped) return;
            bootstrapped = true;

            var settings = Resources.Load<JixiSettings>(JixiSettings.DefaultResourcesPath);
            if (settings == null)
            {
                var all = Resources.LoadAll<JixiSettings>("");
                if (all != null && all.Length > 0) settings = all[0];
            }

            if (settings != null)
            {
                instance.apiKey = (settings.apiKey ?? "").Trim();
                instance.workflows = settings.workflows ?? new List<JixiWorkflow>();
                instance.workflowMap = settings.BuildMap();
                Debug.Log($"[Jixi] Loaded settings: apiKey? {(string.IsNullOrEmpty(instance.apiKey) ? "NO" : "YES")}, workflows={instance.workflows.Count}");
                if (instance.workflowMap != null)
                {
                    foreach (var kv in instance.workflowMap)
                        Debug.Log($"[Jixi] Workflow: '{kv.Key}' -> {kv.Value}");
                }
            }
            else
            {
                Debug.LogWarning("[Jixi] No JixiSettings found in Resources. Place one at Assets/Resources/JixiSettings.asset");
            }

            EnsureRunner();
        }

        private static void EnsureRunner()
        {
            if (runner != null) return;
            var go = new GameObject("JixiRunner");
            go.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(go);
            runner = go.AddComponent<JixiRunner>(); // JixiRunner should call Jixi.Instance.Tick() in Update
        }

        // =========================
        //  PUBLIC SDK: WORKFLOWS
        // =========================

        public void StartWorkflow<T>(string name, Action<T> onCompleted) where T : class
            => StartWorkflow<T>(name, "{}", null, onCompleted);

        public void StartWorkflow<T>(string name, object payload, Action<T> onCompleted) where T : class
        {
            Debug.Log($"payload = {payload}");
            StartWorkflow<T>(name, payload == null ? "{}" : JsonUtility.ToJson(payload), null, onCompleted);
        }

        public void StartWorkflow<T>(string name, string jsonData = "{}", string filePath = null, Action<T> onCompleted = null) where T : class
        {
            Debug.Log($"jsonData = {jsonData}");

            var url = GetWorkflowUrl(name);
            if (string.IsNullOrEmpty(url))
            {
                Debug.LogWarning($"No workflow {name} found in settings.");
                onCompleted?.Invoke(null);
                return;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            if (string.IsNullOrEmpty(filePath))
                runner.StartCoroutine(SendJsonWebGL(url, jsonData, onCompleted));
            else
                runner.StartCoroutine(SendMultipartWebGL(url, jsonData, filePath, onCompleted));
#else
            if (string.IsNullOrEmpty(filePath))
                SendJsonHttpClient(url, jsonData, onCompleted);
            else
                SendMultipartHttpClient(url, jsonData, filePath, onCompleted);
#endif
        }

        private string GetWorkflowUrl(string name)
            => workflowMap != null && workflowMap.TryGetValue(name, out var url) ? url : null;

        // ---------- Desktop/Mobile (HttpClient) ----------
        public void StartWorkflowBytes<T>(
            string name,
            string jsonData,
            byte[] fileBytes,
            string fileName,
            Action<T> onCompleted) where T : class
        {
            var url = GetWorkflowUrl(name);
            if (string.IsNullOrEmpty(url))
            {
                Debug.LogWarning($"No workflow {name} found in settings.");
                onCompleted?.Invoke(null);
                return;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
    // WebGL path — already uses UnityWebRequest + WWWForm → perfect for bytes
    runner.StartCoroutine(SendMultipartWebGLBytes(url, jsonData, fileBytes, fileName, onCompleted));
#else
            // Desktop/Mobile — use HttpClient multipart with bytes
            SendMultipartHttpClientBytes(url, jsonData, fileBytes, fileName, onCompleted);
#endif
        }
        
        private void SendMultipartHttpClientBytes<T>(
            string url,
            string jsonData,
            byte[] fileBytes,
            string fileName,
            Action<T> onCompleted) where T : class
        {
            Task.Run(async () =>
            {
                try
                {
                    if (!string.IsNullOrEmpty(apiKey))
                        httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

                    using var content = new MultipartFormDataContent();
                    content.Add(new StringContent(jsonData ?? "{}", Encoding.UTF8, "application/json"), "data");

                    if (fileBytes != null && fileBytes.Length > 0)
                    {
                        var mime = GuessMimeType(fileName);
                        var file = new ByteArrayContent(fileBytes);
                        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mime);
                        content.Add(file, "file", string.IsNullOrEmpty(fileName) ? "upload.bin" : fileName);
                    }

                    var response = await httpClient.PostAsync(url, content);
                    var body = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        if (typeof(T) == typeof(string))
                        {
                            string result = body?.Trim();
                            if (!string.IsNullOrEmpty(result))
                            {
                                if (result.StartsWith("{"))
                                {
                                    var maybe = TryExtractUrlFromJson(result);
                                    if (!string.IsNullOrEmpty(maybe)) result = maybe;
                                }
                                else if (result.StartsWith("\"") && result.EndsWith("\""))
                                {
                                    result = result.Trim('"');
                                }
                            }
                            mainThreadActions.Enqueue(() => onCompleted?.Invoke((T)(object)result));
                        }
                        else
                        {
                            var obj = JsonUtility.FromJson<T>(body);
                            mainThreadActions.Enqueue(() => onCompleted?.Invoke(obj));
                        }
                    }
                    else
                    {
                        Debug.LogError($"Jixi HTTP {response.StatusCode}\n{body}");
                        mainThreadActions.Enqueue(() => onCompleted?.Invoke(null));
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"Jixi exception: {e}");
                    mainThreadActions.Enqueue(() => onCompleted?.Invoke(null));
                }
            });
        }

        private void SendJsonHttpClient<T>(string url, string jsonData, Action<T> onCompleted) where T : class
        {
            Debug.Log(url);
            Debug.Log(jsonData);
            Task.Run(async () =>
            {
                try
                {
                    if (!string.IsNullOrEmpty(apiKey))
                        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                    var jsonContent = new StringContent(jsonData, Encoding.UTF8, "application/json");
                    var response = await httpClient.PostAsync(url, jsonContent);
                    var body = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        if (typeof(T) == typeof(string))
                        {
                            // Return raw body OR extract "url" if response is an object
                            string result = body?.Trim();
                            if (!string.IsNullOrEmpty(result))
                            {
                                if (result.StartsWith("{")) // try {"url":"..."} shape
                                {
                                    var maybe = TryExtractUrlFromJson(result);
                                    if (!string.IsNullOrEmpty(maybe)) result = maybe;
                                }
                                else if (result.StartsWith("\"") && result.EndsWith("\""))
                                {
                                    result = result.Trim('"');
                                }
                            }
                            var casted = (T)(object)result;
                            mainThreadActions.Enqueue(() => onCompleted?.Invoke(casted));
                        }
                        else
                        {
                            var obj = JsonUtility.FromJson<T>(body);
                            mainThreadActions.Enqueue(() => onCompleted?.Invoke(obj));
                        }
                    }
                    else
                    {
                        Debug.LogError($"Jixi HTTP {response.StatusCode}\n{body}");
                        mainThreadActions.Enqueue(() => onCompleted?.Invoke(null));
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"Jixi exception: {e}");
                    mainThreadActions.Enqueue(() => onCompleted?.Invoke(null));
                }
            });
        }

        private void SendMultipartHttpClient<T>(string url, string jsonData, string filePath, Action<T> onCompleted) where T : class
        {
            Task.Run(async () =>
            {
                try
                {
                    if (!string.IsNullOrEmpty(apiKey))
                        httpClient.DefaultRequestHeaders.Authorization =
                            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

                    using var content = new MultipartFormDataContent();
                    content.Add(new StringContent(jsonData ?? "{}", Encoding.UTF8, "application/json"), "data");

                    if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                    {
                        var fileName = Path.GetFileName(filePath);
                        var bytes = File.ReadAllBytes(filePath);
                        var file = new ByteArrayContent(bytes);
                        file.Headers.ContentType =
                            new System.Net.Http.Headers.MediaTypeHeaderValue(GuessMimeType(fileName));
                        content.Add(file, "file", fileName);
                    }
                    else
                    {
                        Debug.LogError($"File not found: {filePath}");
                        mainThreadActions.Enqueue(() => onCompleted?.Invoke(null));
                        return;
                    }

                    var response = await httpClient.PostAsync(url, content);
                    var body = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        if (typeof(T) == typeof(string))
                        {
                            string result = body?.Trim();
                            if (!string.IsNullOrEmpty(result))
                            {
                                if (result.StartsWith("{"))
                                {
                                    var maybe = TryExtractUrlFromJson(result); // same helper you already have
                                    if (!string.IsNullOrEmpty(maybe)) result = maybe;
                                }
                                else if (result.StartsWith("\"") && result.EndsWith("\""))
                                {
                                    result = result.Trim('"');
                                }
                            }
                            mainThreadActions.Enqueue(() => onCompleted?.Invoke((T)(object)result));
                        }
                        else
                        {
                            var obj = JsonUtility.FromJson<T>(body);
                            mainThreadActions.Enqueue(() => onCompleted?.Invoke(obj));
                        }
                    }
                    else
                    {
                        Debug.LogError($"Jixi HTTP {response.StatusCode}\n{body}");
                        mainThreadActions.Enqueue(() => onCompleted?.Invoke(null));
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"Jixi exception: {e}");
                    mainThreadActions.Enqueue(() => onCompleted?.Invoke(null));
                }
            });
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        // ---------- WebGL (UnityWebRequest) ----------
        private System.Collections.IEnumerator SendMultipartWebGLBytes<T>(
            string url,
            string jsonData,
            byte[] fileBytes,
            string fileName,
            Action<T> onCompleted) where T : class
        {
            var form = new WWWForm();
            form.AddField("data", string.IsNullOrEmpty(jsonData) ? "{}" : jsonData);

            if (fileBytes != null && fileBytes.Length > 0)
            {
                var mime = GuessMimeType(fileName);
                form.AddBinaryData("file", fileBytes, string.IsNullOrEmpty(fileName) ? "upload.bin" : fileName, mime);
            }

            var req = UnityWebRequest.Post(url, form);
            if (!string.IsNullOrEmpty(apiKey))
                req.SetRequestHeader("Authorization", $"Bearer {apiKey}");

            yield return req.SendWebRequest();

        #if UNITY_2020_2_OR_NEWER
            if (req.result != UnityWebRequest.Result.Success)
        #else
            if (req.isNetworkError || req.isHttpError)
        #endif
            {
                Debug.LogError($"Jixi WebGL error: {req.error}");
                onCompleted?.Invoke(null);
            }
            else
            {
                var text = req.downloadHandler.text;
                try
                {
                    if (typeof(T) == typeof(string))
                    {
                        string result = text?.Trim();
                        if (!string.IsNullOrEmpty(result))
                        {
                            if (result.StartsWith("{"))
                            {
                                var maybe = TryExtractUrlFromJson(result);
                                if (!string.IsNullOrEmpty(maybe)) result = maybe;
                            }
                            else if (result.StartsWith("\"") && result.EndsWith("\""))
                            {
                                result = result.Trim('"');
                            }
                        }
                        onCompleted?.Invoke((T)(object)result);
                    }
                    else
                    {
                        onCompleted?.Invoke(JsonUtility.FromJson<T>(text));
                    }
                }
                catch
                {
                    onCompleted?.Invoke(null);
                }
            }
        }

        private System.Collections.IEnumerator SendJsonWebGL<T>(string url, string jsonData, Action<T> onCompleted) where T : class
        {
            var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonData));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            if (!string.IsNullOrEmpty(apiKey))
                req.SetRequestHeader("Authorization", $"Bearer {apiKey}");

            yield return req.SendWebRequest();

        #if UNITY_2020_2_OR_NEWER
            if (req.result != UnityWebRequest.Result.Success)
        #else
            if (req.isNetworkError || req.isHttpError)
        #endif
            {
                Debug.LogError($"Jixi WebGL error: {req.error}");
                onCompleted?.Invoke(null);
            }
            else
            {
                var text = req.downloadHandler.text;
                try
                {
                    if (typeof(T) == typeof(string))
                    {
                        string result = text?.Trim();
                        if (!string.IsNullOrEmpty(result))
                        {
                            if (result.StartsWith("{"))
                            {
                                var maybe = TryExtractUrlFromJson(result);
                                if (!string.IsNullOrEmpty(maybe)) result = maybe;
                            }
                            else if (result.StartsWith("\"") && result.EndsWith("\""))
                            {
                                result = result.Trim('"');
                            }
                        }
                        onCompleted?.Invoke((T)(object)result);
                    }
                    else
                    {
                        onCompleted?.Invoke(JsonUtility.FromJson<T>(text));
                    }
                }
                catch
                {
                    onCompleted?.Invoke(null);
                }
            }
        }

        private System.Collections.IEnumerator SendMultipartWebGL<T>(string url, string jsonData, string filePath, Action<T> onCompleted) where T : class
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                Debug.LogError($"File not found: {filePath}");
                onCompleted?.Invoke(null);
                yield break;
            }

            var form = new WWWForm();
            form.AddField("data", jsonData);
            var fileName = Path.GetFileName(filePath);
            var bytes = File.ReadAllBytes(filePath);
            form.AddBinaryData("file", bytes, fileName, "application/octet-stream");

            var req = UnityWebRequest.Post(url, form);
            if (!string.IsNullOrEmpty(apiKey))
                req.SetRequestHeader("Authorization", $"Bearer {apiKey}");

            yield return req.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
            if (req.result != UnityWebRequest.Result.Success)
#else
            if (req.isNetworkError || req.isHttpError)
#endif
            {
                Debug.LogError($"Jixi WebGL error: {req.error}");
                onCompleted?.Invoke(null);
            }
            else
            {
                try { onCompleted?.Invoke(JsonUtility.FromJson<T>(req.downloadHandler.text)); }
                catch { onCompleted?.Invoke(null); }
            }
        }
#endif

        // =========================
        //  PUBLIC SDK: HELPERS
        // =========================

        /// <summary>
        /// Loads a Texture2D from URL, blits to a RenderTexture, returns on main thread.
        /// </summary>
        public void LoadRenderTextureFromUrl(string url, Action<RenderTexture> onDone)
        {
            if (string.IsNullOrWhiteSpace(url)) { onDone?.Invoke(null); return; }
            EnsureRunner();
            runner.StartCoroutine(LoadRT_Coroutine(url, onDone));
        }

        private System.Collections.IEnumerator LoadRT_Coroutine(string url, Action<RenderTexture> cb)
        {
            using (var req = UnityWebRequestTexture.GetTexture(url))
            {
                yield return req.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
                if (req.result != UnityWebRequest.Result.Success)
#else
                if (req.isNetworkError || req.isHttpError)
#endif
                { mainThreadActions.Enqueue(() => cb?.Invoke(null)); yield break; }

                var tex2D = DownloadHandlerTexture.GetContent(req);
                var rt = new RenderTexture(tex2D.width, tex2D.height, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(tex2D, rt);
                mainThreadActions.Enqueue(() => cb?.Invoke(rt));
            }
        }

        // -------------------- PUBLIC: multi --------------------
        public void SignJixiS3Urls(
            IEnumerable<string> originalUrls,
            Action<List<string>> onDone,
            string endpoint = "https://api.jixi.ai/sign-url",
            int timeoutSeconds = 15,
            int expiresInSeconds = 1800)
        {
            if (originalUrls == null) { onDone?.Invoke(null); return; }
            var list = new List<string>();
            foreach (var u in originalUrls) if (!string.IsNullOrWhiteSpace(u)) list.Add(u);
            if (list.Count == 0) { onDone?.Invoke(new List<string>()); return; }

            EnsureRunner();
            runner.StartCoroutine(SignUrls_UWR_Coroutine(apiKey, list, endpoint, timeoutSeconds, expiresInSeconds, onDone));
        }

        private System.Collections.IEnumerator SignUrls_UWR_Coroutine(
            string key,
            List<string> urls,
            string endpoint,
            int timeoutSeconds,
            int expiresInSeconds,
            Action<List<string>> cb)
        {
            key = (key ?? string.Empty).Trim(); // remove stray spaces/newlines
            if (string.IsNullOrEmpty(key))
            {
                Debug.LogWarning("Jixi.SignJixiS3Urls: API key is empty.");
                cb?.Invoke(null);
                yield break;
            }

            var json = BuildUrlsPayload(urls, expiresInSeconds); // {"urls":[...],"expiresIn":N}
            var body = Encoding.UTF8.GetBytes(json);

            using (var req = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST))
            {
                key = "Bearer " + key;
                req.uploadHandler   = new UploadHandlerRaw(body);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");

                // 3) header — send RAW key and use canonical casing
                // Do NOT prefix with "Bearer "
                req.SetRequestHeader("Authorization", key);

                // (optional: if your backend insisted on lowercase we could add both; usually not needed)
                req.SetRequestHeader("authorization", key);

                req.timeout = Mathf.Max(1, timeoutSeconds);

                yield return req.SendWebRequest();

        #if UNITY_2020_2_OR_NEWER
                if (req.result != UnityWebRequest.Result.Success)
        #else
                if (req.isNetworkError || req.isHttpError)
        #endif
                {
                    Debug.LogWarning($"Jixi.SignJixiS3Urls error: {req.responseCode} {req.error}\n{req.downloadHandler.text}");
                    cb?.Invoke(null);
                    yield break;
                }

                var resp = req.downloadHandler.text ?? string.Empty;
                // Debug.Log($"SignJixiS3Urls response: {resp}");
                var signed = TryExtractUrlsFromResponse(resp);
                cb?.Invoke(signed);
            }
        }

        // --- PUBLIC: single (wraps multi) ---
        public void SignJixiS3Url(
            string originalUrl,
            Action<string> onDone,
            string endpoint = "https://api.jixi.ai/sign-url",
            int timeoutSeconds = 15,
            int expiresInSeconds = 60)
        {
            if (string.IsNullOrWhiteSpace(originalUrl)) { onDone?.Invoke(null); return; }
            SignJixiS3Urls(new[] { originalUrl }, list =>
            {
                onDone?.Invoke((list != null && list.Count > 0) ? list[0] : null);
            }, endpoint, timeoutSeconds, expiresInSeconds);
        }

        #if UNITY_WEBGL && !UNITY_EDITOR
        private System.Collections.IEnumerator SignUrlsWebGL_Coroutine(
            string key,
            List<string> urls,
            string endpoint,
            int timeoutSeconds,
            int expiresInSeconds, // NEW
            Action<List<string>> cb)
        {
            var req = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST);
            var json = BuildUrlsPayload(urls, expiresInSeconds); // {"urls":[...], "expiresIn": N}
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            if (!string.IsNullOrEmpty(key)) req.SetRequestHeader("authorization", key);
            req.timeout = Mathf.Max(1, timeoutSeconds);

            yield return req.SendWebRequest();

        #if UNITY_2020_2_OR_NEWER
            if (req.result != UnityWebRequest.Result.Success)
        #else
            if (req.isNetworkError || req.isHttpError)
        #endif
            {
                Debug.LogWarning($"Jixi.SignJixiS3Urls WebGL error: {req.error}");
                cb?.Invoke(null);
            }
            else
            {
                var text = req.downloadHandler.text ?? string.Empty;
                var signed = TryExtractUrlsFromResponse(text);
                cb?.Invoke(signed);
            }
        }
        #endif

        // --- helpers ---
        private static string BuildUrlsPayload(List<string> urls, int expiresInSeconds)
        {
            var sb = new StringBuilder();
            sb.Append("{\"urls\":[");
            for (int i = 0; i < urls.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('\"').Append(EscapeJson(urls[i])).Append('\"');
            }
            sb.Append("],\"expiresIn\":").Append(expiresInSeconds).Append('}');
            return sb.ToString();
        }

        // -------------------- helpers --------------------
        private static string BuildUrlsPayload(List<string> urls)
        {
            // If 1 URL, the API also accepts {"url":"..."} — but we'll standardize on {"urls":[...]}
            var sb = new StringBuilder();
            sb.Append("{\"urls\":[");
            for (int i = 0; i < urls.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('\"').Append(EscapeJson(urls[i])).Append('\"');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        // Accepts any of:
        //   ["signed1","signed2",...]
        //   {"urls":["signed1","signed2",...]}
        //   {"url":"signed"} or "signed"
        private static List<string> TryExtractUrlsFromResponse(string body)
        {
            if (string.IsNullOrEmpty(body)) return null;

            // quick trim
            var t = body.Trim();

            // Raw array at root
            if (t.Length > 0 && t[0] == '[')
            {
                // Wrap to parse via JsonUtility
                var wrapped = "{\"urls\":" + t + "}";
                var arr = JsonUtility.FromJson<UrlListWrapper>(wrapped);
                return arr?.urls ?? new List<string>();
            }

            // Object with urls
            if (t.Contains("\"urls\""))
            {
                var arr = JsonUtility.FromJson<UrlListWrapper>(t);
                if (arr?.urls != null) return arr.urls;
            }

            // Object with single url
            var single = TryExtractUrlFromJson(t);
            if (!string.IsNullOrEmpty(single)) return new List<string> { single };

            // Fallback raw string
            if (t.Length > 0 && t[0] == '"' && t[t.Length - 1] == '"')
                return new List<string> { t.Trim('"') };

            return null;
        }

        [Serializable]
        private class UrlListWrapper { public List<string> urls; }

        // =========================
        //  INTERNAL UTILITIES
        // =========================

        private static string GuessMimeType(string fileName)
        {
            var ext = (System.IO.Path.GetExtension(fileName) ?? "").ToLowerInvariant();
            return ext switch
            {
                ".png" => "image/png",
                ".jpg" => "image/jpeg",
                ".jpeg" => "image/jpeg",
                ".webp" => "image/webp",
                _ => "application/octet-stream"
            };
        }

        private static string TryExtractUrlFromJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            const string key = "\"url\"";
            int i = json.IndexOf(key, StringComparison.Ordinal);
            if (i < 0) return null;
            int colon = json.IndexOf(':', i + key.Length);
            if (colon < 0) return null;
            int q1 = json.IndexOf('"', colon + 1);
            if (q1 < 0) return null;
            int q2 = q1 + 1;
            while (q2 < json.Length)
            {
                if (json[q2] == '"' && json[q2 - 1] != '\\') break;
                q2++;
            }
            if (q2 >= json.Length) return null;
            var raw = json.Substring(q1 + 1, q2 - (q1 + 1));
            return raw.Replace("\\\"", "\"");
        }

        internal void Tick()
        {
            while (mainThreadActions.TryDequeue(out var a))
                a?.Invoke();
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}