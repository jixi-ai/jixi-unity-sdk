#if UNITY_EDITOR
using System;
using System.Collections.Generic;
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

        // Your paths
        private const string LogoLightPath = "Assets/Jixi AI/Editor/Sprites/jixi_logo_light.png";
        private const string LogoDarkPath  = "Assets/Jixi AI/Editor/Sprites/jixi_logo_dark.png";
        private const string CopyIconPath  = "Assets/Jixi AI/Editor/Sprites/copy_icon.png";

        // Layout constants
        private const float RowSidePadding = 8f;
        private const float RowKeyWidth = 52f;
        private const float RowButtonSize = 22f;
        private const float RowInnerSpacing = 4f;

        [MenuItem("Jixi AI/Setup")]
        public static void Open() => GetWindow<JixiSetupWindow>("Jixi Setup");

        private void OnEnable()
        {
            _settings = JixiSettings.LoadOrCreateInResources();
            _copyIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(CopyIconPath);
            _logoCached = null; // re-evaluate per skin on first draw

            // tiny toast styles
            _toastBg = new GUIStyle(GUI.skin.box) { padding = new RectOffset(8, 8, 4, 4) };
            _toastTextStyle = new GUIStyle(EditorStyles.miniLabel);
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

                // Create a vertical layout for name + URL (compact, no wrapping)
                var nameStyle = new GUIStyle(EditorStyles.label)
                {
                    wordWrap = false,
                    clipping = TextClipping.Clip,
                    richText = false
                };
                var urlStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    wordWrap = false,
                    clipping = TextClipping.Clip,
                    richText = false
                };

                // Compute height manually to vertically center with button
                float lineHeight = EditorGUIUtility.singleLineHeight;
                float totalTextHeight = lineHeight * 2f + RowInnerSpacing;
                float buttonHeight = RowButtonSize;
                float maxHeight = Mathf.Max(totalTextHeight, buttonHeight);

                Rect rowRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
                    GUILayout.Height(maxHeight), GUILayout.ExpandWidth(true));

                // Layout rects for left text and right button
                float leftWidth = rowRect.width - RowButtonSize - RowSidePadding * 2f - 6f;
                Rect leftRect = new Rect(rowRect.x + RowSidePadding, rowRect.y, leftWidth, rowRect.height);
                Rect buttonRect = new Rect(leftRect.xMax + 6f, rowRect.y + (maxHeight - RowButtonSize) * 0.5f, RowButtonSize, RowButtonSize);

                // Draw text block manually (two lines centered vertically)
                float textStartY = rowRect.y + (maxHeight - totalTextHeight) * 0.5f;
                Rect nameKeyRect = new Rect(leftRect.x, textStartY, RowKeyWidth, lineHeight);
                Rect nameValRect = new Rect(leftRect.x + RowKeyWidth, textStartY, leftRect.width - RowKeyWidth, lineHeight);
                Rect urlKeyRect = new Rect(leftRect.x, textStartY + lineHeight + RowInnerSpacing, RowKeyWidth, lineHeight);
                Rect urlValRect = new Rect(leftRect.x + RowKeyWidth, textStartY + lineHeight + RowInnerSpacing, leftRect.width - RowKeyWidth, lineHeight);

                EditorGUI.LabelField(nameKeyRect, "Name:", EditorStyles.miniBoldLabel);
                EditorGUI.LabelField(nameValRect, new GUIContent(wf.name, wf.name), nameStyle);
                EditorGUI.LabelField(urlKeyRect, "URL:", EditorStyles.miniBoldLabel);
                EditorGUI.LabelField(urlValRect, new GUIContent(wf.url, wf.url), urlStyle);

                // Copy button (vertically centered)
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
            // Swap logo asset based on skin (dark editor -> light logo; light editor -> dark logo)
            var desiredPath = EditorGUIUtility.isProSkin ? LogoLightPath : LogoDarkPath;
            if (_logoCached == null || AssetDatabase.GetAssetPath(_logoCached) != desiredPath)
                _logoCached = AssetDatabase.LoadAssetAtPath<Texture2D>(desiredPath);

            // Compute logo size first so we know the banner height
            float maxWidth = Mathf.Min(position.width * 0.6f, 260f);
            float width = Mathf.Clamp(maxWidth, 80f, 128f);
            float height = 60f; // fallback if no texture

            if (_logoCached != null)
            {
                float aspect = (float)_logoCached.width / Mathf.Max(1f, _logoCached.height);
                height = width / Mathf.Max(0.01f, aspect);
            }

            // Banner rect (full width)
            float bannerHeight = height + BannerTopPadding + BannerBottomPadding;
            Rect bannerRect = GUILayoutUtility.GetRect(position.width, bannerHeight,
                GUILayout.ExpandWidth(true), GUILayout.Height(bannerHeight));

            // Draw banner
            var bannerColor = EditorGUIUtility.isProSkin ? BannerDark : BannerLight;
            EditorGUI.DrawRect(new Rect(0, bannerRect.y, position.width, bannerRect.height), bannerColor);

            // Draw logo centered inside banner
            if (_logoCached != null)
            {
                float x = (position.width - width) * 0.5f;
                float y = bannerRect.y + BannerTopPadding;
                var logoRect = new Rect(x, y, width, height);
                GUI.DrawTexture(logoRect, _logoCached, ScaleMode.ScaleToFit);
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

            // Draw on top of all layout (no clipping from scroll views)
            var winRect = new Rect(0, 0, position.width, position.height);
            GUI.BeginGroup(winRect); // local (0,0) space

            var content = new GUIContent(_toastText);
            // compact font and tighter box
            var textStyle = _toastTextStyle ?? EditorStyles.miniLabel;
            var size = textStyle.CalcSize(content);
            float padX = 8f, padY = 4f;         // smaller padding
            float w = Mathf.Clamp(size.x + padX * 2f, 80f, 320f);
            float h = Mathf.Clamp(size.y + padY * 2f, 18f, 28f);

            // bottom-right overlay
            var rect = new Rect(winRect.width - w - 12f, winRect.height - h - 12f, w, h);

            // subtle background that works in light/dark
            var bg = _toastBg ?? new GUIStyle(GUI.skin.box);
            GUI.Box(rect, GUIContent.none, bg);

            var inner = new Rect(rect.x + padX, rect.y + padY, rect.width - padX * 2f, rect.height - padY * 2f);
            GUI.Label(inner, content, textStyle);

            GUI.EndGroup();

            Repaint();
        }
    }
}
#endif