#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using JixiAI;

namespace JixiAI.Editor
{
    public class JixiSetupWindow : EditorWindow
    {
        private JixiSettings _settings;
        private Texture2D _logoCached;
        private Texture2D _copyIcon;
        private Vector2 _scroll;

        // Logo banner
        private const float BannerTopPadding = 14f;
        private const float BannerBottomPadding = 14f;
        private static readonly Color BannerDark = new Color(0.078f, 0.078f, 0.078f, 1f);
        private static readonly Color BannerLight = new Color(0.90f, 0.90f, 0.90f, 1f);

        // Toast (tiny)
        private string _toastText;
        private double _toastUntil;
        private GUIStyle _toastBg;
        private GUIStyle _toastTextStyle;

        private const string WorkflowsUrl = "https://api.jixi.ai/workflows";

        // Layout constants
        private const float RowSidePadding = 8f;
        private const float RowKeyWidth = 52f;
        private const float RowButtonSize = 22f;
        private const float RowInnerSpacing = 4f;

        // === NEW: dynamic editor/package paths ===
        private string _thisEditorDir;
        private string _spritesDir;

        [MenuItem("Jixi AI/Setup")]
        public static void Open() => GetWindow<JixiSetupWindow>("Jixi Setup");

        private void OnEnable()
        {
            // Settings
            _settings = JixiSettings.LoadOrCreateInResources();

            // Tiny toast styles
            _toastBg = new GUIStyle(GUI.skin.box) { padding = new RectOffset(8, 8, 4, 4) };
            _toastTextStyle = new GUIStyle(EditorStyles.miniLabel);

            // Resolve dynamic locations for sprites (works for Assets/ and Packages/)
            ResolveEditorDirectories();

            // Load icons via resilient helper
            _copyIcon = LoadSprite("copy_icon.png");

            // Re-evaluate logo each draw (skin-aware)
            _logoCached = null;
        }

        private void OnGUI()
        {
            DrawCenteredLogo();

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("API Key", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            _settings.apiKey = EditorGUILayout.PasswordField(_settings.apiKey);
            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(_settings);
                AssetDatabase.SaveAssets(); // auto-save
            }

            EditorGUILayout.Space(6);

            if (GUILayout.Button("Fetch Workflows"))
            {
                _ = FetchWorkflowsAsync();
            }

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Workflows (read-only)", EditorStyles.boldLabel);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            if (_settings.workflows == null || _settings.workflows.Count == 0)
            {
                EditorGUILayout.HelpBox("No workflows loaded. Click 'Fetch Workflows'.", MessageType.Info);
            }
            else
            {
                foreach (var wf in _settings.workflows)
                {
                    DrawWorkflowRow(wf);
                }
            }
            EditorGUILayout.EndScrollView();

            GUILayout.FlexibleSpace();
            EditorGUILayout.HelpBox(
                "The API key and workflows are stored in Assets/Resources/JixiSettings.asset.\n" +
                "At runtime, the SDK loads that asset and uses a name→URL map.", MessageType.None);

            DrawToast();
        }

        // ---------- Rows ----------
        private void DrawWorkflowRow(JixiWorkflow wf)
        {
            using (new EditorGUILayout.HorizontalScope("box"))
            {
                GUILayout.Space(RowSidePadding);

                var nameStyle = new GUIStyle(EditorStyles.label) { wordWrap = false, clipping = TextClipping.Clip };
                var urlStyle  = new GUIStyle(EditorStyles.miniLabel) { wordWrap = false, clipping = TextClipping.Clip };

                float lineHeight = EditorGUIUtility.singleLineHeight;
                float totalTextHeight = lineHeight * 2f + RowInnerSpacing;
                float buttonHeight = RowButtonSize;
                float maxHeight = Mathf.Max(totalTextHeight, buttonHeight);

                Rect rowRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
                    GUILayout.Height(maxHeight), GUILayout.ExpandWidth(true));

                float leftWidth = rowRect.width - RowButtonSize - RowSidePadding * 2f - 6f;
                Rect leftRect   = new Rect(rowRect.x + RowSidePadding, rowRect.y, leftWidth, rowRect.height);
                Rect buttonRect = new Rect(leftRect.xMax + 6f, rowRect.y + (maxHeight - RowButtonSize) * 0.5f, RowButtonSize, RowButtonSize);

                float textStartY = rowRect.y + (maxHeight - totalTextHeight) * 0.5f;
                Rect nameKeyRect = new Rect(leftRect.x, textStartY, RowKeyWidth, lineHeight);
                Rect nameValRect = new Rect(leftRect.x + RowKeyWidth, textStartY, leftRect.width - RowKeyWidth, lineHeight);
                Rect urlKeyRect  = new Rect(leftRect.x, textStartY + lineHeight + RowInnerSpacing, RowKeyWidth, lineHeight);
                Rect urlValRect  = new Rect(leftRect.x + RowKeyWidth, textStartY + lineHeight + RowInnerSpacing, leftRect.width - RowKeyWidth, lineHeight);

                EditorGUI.LabelField(nameKeyRect, "Name:", EditorStyles.miniBoldLabel);
                EditorGUI.LabelField(nameValRect, new GUIContent(wf.name, wf.name), nameStyle);
                EditorGUI.LabelField(urlKeyRect, "URL:", EditorStyles.miniBoldLabel);
                EditorGUI.LabelField(urlValRect, new GUIContent(wf.url, wf.url), urlStyle);

                var btnContent = _copyIcon
                    ? new GUIContent(_copyIcon, "Copy workflow name")
                    : new GUIContent("Copy", "Copy workflow name");

                if (GUI.Button(buttonRect, btnContent))
                {
                    EditorGUIUtility.systemCopyBuffer = wf.name;
                    ShowToast($"Copied “{wf.name}”", 1.25f);
                }

                GUILayout.Space(RowSidePadding);
            }
        }

        // ---------- Logo ----------
        private void DrawCenteredLogo()
        {
            // Choose sprite name by editor skin
            string logoFile = EditorGUIUtility.isProSkin ? "jixi_logo_light.png" : "jixi_logo_dark.png";

            // Load via resilient helper
            var tex = _logoCached;
            if (tex == null)
            {
                tex = LoadSprite(logoFile);
                _logoCached = tex; // cache per skin
            }

            float maxWidth = Mathf.Min(position.width * 0.6f, 260f);
            float width    = Mathf.Clamp(maxWidth, 80f, 128f);
            float height   = 60f;

            if (tex != null)
            {
                float aspect = (float)tex.width / Mathf.Max(1f, tex.height);
                height = width / Mathf.Max(0.01f, aspect);
            }

            float bannerHeight = height + BannerTopPadding + BannerBottomPadding;
            Rect bannerRect = GUILayoutUtility.GetRect(position.width, bannerHeight,
                GUILayout.ExpandWidth(true), GUILayout.Height(bannerHeight));

            var bannerColor = EditorGUIUtility.isProSkin ? BannerDark : BannerLight;
            EditorGUI.DrawRect(new Rect(0, bannerRect.y, position.width, bannerRect.height), bannerColor);

            if (tex != null)
            {
                float x = (position.width - width) * 0.5f;
                float y = bannerRect.y + BannerTopPadding;
                var logoRect = new Rect(x, y, width, height);
                GUI.DrawTexture(logoRect, tex, ScaleMode.ScaleToFit);
            }
        }

        // ---------- Networking ----------
        private async Task FetchWorkflowsAsync()
        {
            if (string.IsNullOrWhiteSpace(_settings.apiKey))
            {
                EditorUtility.DisplayDialog("Jixi", "Please enter your API key first.", "OK");
                return;
            }

            try
            {
                var list = await GetWorkflowsOverHttpClient(_settings.apiKey);
                _settings.workflows = new List<JixiWorkflow>(list ?? Array.Empty<JixiWorkflow>());
                EditorUtility.SetDirty(_settings);
                AssetDatabase.SaveAssets();
                Repaint();
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Jixi", "Failed to fetch workflows:\n\n" + ex.Message, "OK");
            }
        }

        private static async Task<JixiWorkflow[]> GetWorkflowsOverHttpClient(string apiKey)
        {
            using var client = new HttpClient();
            using var req = new HttpRequestMessage(HttpMethod.Get, WorkflowsUrl);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            req.Headers.Accept.ParseAdd("application/json");

            using var res = await client.SendAsync(req);
            var body = await res.Content.ReadAsStringAsync();

            if (!res.IsSuccessStatusCode)
                throw new Exception($"HTTP {(int)res.StatusCode} {res.ReasonPhrase}\n{body}");

            try
            {
                return JsonHelper.FromJsonArray<JixiWorkflow>(body);
            }
            catch (Exception parseEx)
            {
                throw new Exception("JSON parse error: " + parseEx.Message + "\n\nBody:\n" + body);
            }
        }

        // ---------- Tiny toast ----------
        private void ShowToast(string text, float seconds = 1.25f)
        {
            _toastText = text;
            _toastUntil = EditorApplication.timeSinceStartup + seconds;
        }

        private void DrawToast()
        {
            if (string.IsNullOrEmpty(_toastText)) return;
            if (EditorApplication.timeSinceStartup > _toastUntil) return;

            var winRect = new Rect(0, 0, position.width, position.height);
            GUI.BeginGroup(winRect);

            var content = new GUIContent(_toastText);
            var textStyle = _toastTextStyle ?? EditorStyles.miniLabel;
            var size = textStyle.CalcSize(content);
            float padX = 8f, padY = 4f;
            float w = Mathf.Clamp(size.x + padX * 2f, 80f, 320f);
            float h = Mathf.Clamp(size.y + padY * 2f, 18f, 28f);

            var rect = new Rect(winRect.width - w - 12f, winRect.height - h - 12f, w, h);
            var bg = _toastBg ?? new GUIStyle(GUI.skin.box);
            GUI.Box(rect, GUIContent.none, bg);

            var inner = new Rect(rect.x + padX, rect.y + padY, rect.width - padX * 2f, rect.height - padY * 2f);
            GUI.Label(inner, content, textStyle);

            GUI.EndGroup();
            Repaint();
        }
        
        private void ResolveEditorDirectories()
        {
            // Path to this editor script file
            var mono = MonoScript.FromScriptableObject(this);
            var scriptPath = AssetDatabase.GetAssetPath(mono); // e.g., Assets/.../JixiSetupWindow.cs or Packages/com.jixi.ai/Editor/JixiSetupWindow.cs
            var editorDir = Path.GetDirectoryName(scriptPath)?.Replace("\\", "/") ?? string.Empty;

            _thisEditorDir = editorDir;
            _spritesDir = string.IsNullOrEmpty(editorDir) ? string.Empty : (editorDir + "/Sprites");
        }

        private Texture2D LoadSprite(string fileName)
        {
            Texture2D tex = null;

            if (!string.IsNullOrEmpty(_spritesDir))
            {
                var p = _spritesDir + "/" + fileName;
                tex = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                if (tex) return tex;
            }

            var legacy = "Assets/Jixi AI/Editor/Sprites/" + fileName;
            tex = AssetDatabase.LoadAssetAtPath<Texture2D>(legacy);
            if (tex) return tex;

            if (!string.IsNullOrEmpty(_thisEditorDir))
            {
                var maybe = Path.GetDirectoryName(_thisEditorDir)?.Replace("\\", "/");
                if (!string.IsNullOrEmpty(maybe))
                {
                    var p2 = maybe + "/Sprites/" + fileName;
                    tex = AssetDatabase.LoadAssetAtPath<Texture2D>(p2);
                    if (tex) return tex;
                }
            }

            var nameNoExt = Path.GetFileNameWithoutExtension(fileName);
            var searchIn = new List<string>();
            if (!string.IsNullOrEmpty(_thisEditorDir)) searchIn.Add(_thisEditorDir);
            searchIn.Add("Assets");

            var guids = AssetDatabase.FindAssets($"{nameNoExt} t:Texture2D", searchIn.ToArray());
            foreach (var g in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex) return tex;
            }

            return null;
        }
    }
}
#endif