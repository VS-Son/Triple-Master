using UnityEditor;
using UnityEngine;

namespace Project.Tools.Editor.AIImageGenerator
{
    // Editor-only tool: generates UI art with the OpenAI Images API and imports it as a Sprite.
    public class OpenAIImageWindow : EditorWindow
    {
        private static readonly string[] Sizes = { "1024x1024", "1536x1024", "1024x1536", "auto" };
        private static readonly string[] Qualities = { "low", "medium", "high", "auto" };
        private static readonly string[] Backgrounds = { "transparent", "opaque", "auto" };

        private string _apiKey;
        private string _model;
        private string _style;
        private string _folder;
        private string _prompt = "";
        private string _fileName = "ui_asset";
        private AssetPreset _preset = AssetPreset.Button;
        private int _sizeIndex;
        private int _qualityIndex = 1;
        private int _backgroundIndex;
        private bool _assignToSelection = true;
        private bool _showKey;
        private string _codexStatus;

        private OpenAIImageClient.Handle _handle;
        private string _status = "";
        private Texture2D _preview;
        private string _lastAssetPath;
        private Vector2 _scroll;

        [MenuItem("Tools/AI Image Generator (ChatGPT)")]
        public static void Open()
        {
            var window = GetWindow<OpenAIImageWindow>("AI Image Generator");
            window.minSize = new Vector2(420, 560);
        }

        private void OnEnable()
        {
            _apiKey = OpenAIImageClient.ApiKey;
            _model = EditorPrefs.GetString(OpenAIImageClient.PrefModel, "gpt-image-2");
            _style = EditorPrefs.GetString(OpenAIImageClient.PrefStyle, OpenAIImageClient.DefaultStyle);
            _folder = EditorPrefs.GetString(OpenAIImageClient.PrefFolder, "Assets/Project/Art/Generated");
        }

        private void OnDisable()
        {
            _handle?.Cancel();
        }

        private void Update()
        {
            if (_handle != null && !_handle.IsDone)
            {
                _status = $"Generating... {EditorApplication.timeSinceStartup - _handle.StartTime:0}s";
                Repaint();
            }
            else if (AIImageBatch.IsRunning)
            {
                Repaint();
            }
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("Backend", EditorStyles.boldLabel);
            var backend = (ImageBackend)EditorGUILayout.EnumPopup("Generate with", OpenAIImageClient.Backend);
            if (backend != OpenAIImageClient.Backend) OpenAIImageClient.Backend = backend;

            if (backend == ImageBackend.CodexCli)
            {
                string found = CodexImageRunner.FindCodex();
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Codex CLI", found ?? "not found");
                    if (GUILayout.Button("Browse", GUILayout.Width(60)))
                    {
                        string exe = EditorUtility.OpenFilePanel("codex executable", "", "exe,cmd");
                        if (!string.IsNullOrEmpty(exe)) EditorPrefs.SetString(CodexImageRunner.PrefCodexPath, exe);
                    }
                }
                using (new EditorGUI.DisabledScope(found == null))
                {
                    if (GUILayout.Button("Check Codex login")) _codexStatus = CodexImageRunner.RunQuick("login status");
                }
                if (!string.IsNullOrEmpty(_codexStatus)) EditorGUILayout.HelpBox(_codexStatus, MessageType.None);
                EditorGUILayout.HelpBox(
                    "Uses your ChatGPT plan through Codex CLI ($imagegen), no API key or credits needed. " +
                    "Install Codex CLI, then run 'codex login' once and sign in with ChatGPT. " +
                    "Each image takes about 40-90 s; size and transparent background are requested in the prompt.",
                    MessageType.None);
            }
            else
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    string key = _showKey
                        ? EditorGUILayout.TextField("API Key", _apiKey)
                        : EditorGUILayout.PasswordField("API Key", _apiKey);
                    if (key != _apiKey) { _apiKey = key.Trim(); EditorPrefs.SetString(OpenAIImageClient.PrefKey, _apiKey); }
                    _showKey = GUILayout.Toggle(_showKey, "Show", "Button", GUILayout.Width(50));
                }
                EditorGUILayout.HelpBox("The key is saved in EditorPrefs on this computer only. It is not added to the project, git or the game build. Billed per image to API credits.", MessageType.None);

                string model = EditorGUILayout.TextField("Model", _model);
                if (model != _model) { _model = model.Trim(); EditorPrefs.SetString(OpenAIImageClient.PrefModel, _model); }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Style guide (applied to every image)", EditorStyles.boldLabel);
            string style = EditorGUILayout.TextArea(_style, GUILayout.MinHeight(54));
            if (style != _style) { _style = style; EditorPrefs.SetString(OpenAIImageClient.PrefStyle, _style); }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Single image", EditorStyles.boldLabel);
            _preset = (AssetPreset)EditorGUILayout.EnumPopup("Type", _preset);
            EditorGUILayout.LabelField("Description");
            _prompt = EditorGUILayout.TextArea(_prompt, GUILayout.MinHeight(70));
            _sizeIndex = EditorGUILayout.Popup("Size", _sizeIndex, Sizes);
            _qualityIndex = EditorGUILayout.Popup("Quality", _qualityIndex, Qualities);
            _backgroundIndex = EditorGUILayout.Popup("Background", _backgroundIndex, Backgrounds);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Folder", _folder);
                if (GUILayout.Button("...", GUILayout.Width(30))) PickFolder();
            }
            _fileName = EditorGUILayout.TextField("File name", _fileName);
            _assignToSelection = EditorGUILayout.Toggle("Assign to selected Image/SpriteRenderer", _assignToSelection);

            bool busy = _handle != null && !_handle.IsDone;
            using (new EditorGUI.DisabledScope(busy || OpenAIImageClient.CheckReady() != null || string.IsNullOrWhiteSpace(_prompt)))
            {
                if (GUILayout.Button("Generate", GUILayout.Height(32))) Generate();
            }
            if (busy && GUILayout.Button("Cancel")) _handle.Cancel();

            if (!string.IsNullOrEmpty(_status))
            {
                EditorGUILayout.HelpBox(_status, _status.StartsWith("Error") ? MessageType.Error : MessageType.Info);
            }

            if (_preview != null)
            {
                float w = position.width - 24;
                float h = w * _preview.height / _preview.width;
                Rect r = GUILayoutUtility.GetRect(w, h);
                EditorGUI.DrawTextureTransparent(r, _preview, ScaleMode.ScaleToFit);
                if (!string.IsNullOrEmpty(_lastAssetPath) && GUILayout.Button("Select asset"))
                {
                    var asset = AssetDatabase.LoadAssetAtPath<Texture2D>(_lastAssetPath);
                    Selection.activeObject = asset;
                    EditorGUIUtility.PingObject(asset);
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Batch (JSON spec)", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(AIImageBatch.IsRunning || OpenAIImageClient.CheckReady() != null))
                {
                    if (GUILayout.Button("Run spec..."))
                    {
                        string path = EditorUtility.OpenFilePanel("AI image batch spec", Application.dataPath, "json");
                        if (!string.IsNullOrEmpty(path)) AIImageBatch.Run(path);
                    }
                }
                using (new EditorGUI.DisabledScope(!AIImageBatch.IsRunning))
                {
                    if (GUILayout.Button("Cancel batch")) AIImageBatch.Cancel();
                }
            }
            if (AIImageBatch.Total > 0)
            {
                EditorGUILayout.HelpBox(AIImageBatch.Status, MessageType.None);
            }

            EditorGUILayout.EndScrollView();
        }

        private void PickFolder()
        {
            string abs = EditorUtility.OpenFolderPanel("Output folder", Application.dataPath, "");
            if (string.IsNullOrEmpty(abs)) return;
            abs = abs.Replace('\\', '/');
            string root = Application.dataPath.Replace('\\', '/');
            if (!abs.StartsWith(root))
            {
                _status = "Error: the folder must be inside Assets.";
                return;
            }
            _folder = "Assets" + abs.Substring(root.Length);
            EditorPrefs.SetString(OpenAIImageClient.PrefFolder, _folder);
        }

        private void Generate()
        {
            var options = new ImageJobOptions
            {
                model = _model,
                prompt = OpenAIImageClient.BuildPrompt(_prompt, _preset, _style),
                size = Sizes[_sizeIndex],
                quality = Qualities[_qualityIndex],
                background = Backgrounds[_backgroundIndex],
            };
            var selected = Selection.activeGameObject;
            _status = "Generating...";
            _handle = OpenAIImageClient.Generate(options, (png, error) =>
            {
                if (png == null)
                {
                    _status = "Error: " + error;
                    Repaint();
                    return;
                }
                string assetPath = OpenAIImageClient.SavePngAsSprite(png, _folder, _fileName, false);
                _preview = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                _lastAssetPath = assetPath;
                _status = "Saved " + assetPath;
                if (_assignToSelection)
                {
                    var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
                    string comp = OpenAIImageClient.AssignSprite(selected, sprite, false);
                    if (comp != null) _status += $"\nAssigned to {comp} on '{selected.name}'.";
                }
                Repaint();
            });
        }
    }
}
