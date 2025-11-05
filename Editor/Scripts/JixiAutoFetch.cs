#if UNITY_EDITOR
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace JixiAI.Editor
{
    /// Auto-refreshes the Jixi workflows when the editor loads
    /// but only if an API key exists and the last refresh was > REFRESH_INTERVAL hours ago.
    [InitializeOnLoad]
    public static class JixiAutoFetch
    {
        // Throttle so we don't hammer the endpoint if scripts reload often
        private const string LAST_FETCH_KEY = "JixiAI.LastWorkflowFetchUtc";
        private const double REFRESH_INTERVAL_HOURS = 0.1667; // ~10 minutes
        private const string WORKFLOWS_URL = "https://api.jixi.ai/workflows";

        static JixiAutoFetch()
        {
            // Run once after domain reload
            EditorApplication.update += RunOnce;
        }

        private static void RunOnce()
        {
            EditorApplication.update -= RunOnce;

            if (Application.isBatchMode) return;
            if (EditorApplication.isUpdating) return;
            if (EditorApplication.isCompiling) return;

            var settings = JixiSettings.LoadOrCreateInResources();
            if (settings == null || string.IsNullOrWhiteSpace(settings.apiKey)) return;

            // Throttle
            var lastStr = EditorPrefs.GetString(LAST_FETCH_KEY, string.Empty);
            if (DateTime.TryParse(lastStr, out var lastUtc))
            {
                var elapsed = DateTime.UtcNow - lastUtc;
                if (elapsed.TotalHours < REFRESH_INTERVAL_HOURS) return;
            }

            // Fire-and-forget async (Editor context)
            _ = RefreshAsync(settings);
        }

        private static async Task RefreshAsync(JixiSettings settings)
        {
            try
            {
                var list = await GetWorkflows(settings.apiKey);
                if (list == null) return;

                settings.workflows = new System.Collections.Generic.List<JixiWorkflow>(list);
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();

                EditorPrefs.SetString(LAST_FETCH_KEY, DateTime.UtcNow.ToString("o"));
            }
            catch (Exception e) { }
        }

        private static async Task<JixiWorkflow[]> GetWorkflows(string apiKey)
        {
            using var client = new HttpClient();
            using var req = new HttpRequestMessage(HttpMethod.Get, WORKFLOWS_URL);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            req.Headers.Accept.ParseAdd("application/json");

            using var res = await client.SendAsync(req);
            var body = await res.Content.ReadAsStringAsync();

            if (!res.IsSuccessStatusCode)
                throw new Exception($"HTTP {(int)res.StatusCode} {res.ReasonPhrase}\n{body}");

            // Uses your existing JsonHelper that can parse top-level arrays
            return JsonHelper.FromJsonArray<JixiWorkflow>(body);
        }
    }
}
#endif