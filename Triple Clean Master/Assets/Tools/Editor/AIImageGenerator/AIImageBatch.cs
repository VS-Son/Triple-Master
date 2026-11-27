using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Project.Tools.Editor.AIImageGenerator
{
    // Generates every image listed in a JSON spec and assigns each one to its target object.
    //
    // Spec format (JsonUtility):
    // {
    //   "folder": "Assets/Project/Art/Generated/MyScreen",
    //   "style": "",            // optional, falls back to the style saved in the window
    //   "model": "",            // optional, default gpt-image-2
    //   "quality": "medium",    // low | medium | high | auto
    //   "overwrite": true,      // replace files in place so existing references keep working
    //   "items": [
    //     {
    //       "name": "bt_play",                 // file name (no extension)
    //       "preset": "Button",                // Custom | Button | Icon | PopupPanel | Background | Logo | Banner
    //       "prompt": "green play button with a white triangle",
    //       "size": "1536x1024",               // 1024x1024 | 1536x1024 | 1024x1536 | auto
    //       "background": "transparent",       // transparent | opaque | auto
    //       "quality": "",                     // optional per-item override
    //       "prefab": "Assets/Resources/UI/Screen/Home Screen.prefab", // optional
    //       "target": "Root/Child/bt_play",    // path inside the prefab, or a hierarchy path in open scenes
    //       "setNativeSize": false,
    //       "slice": 0.3,                      // optional 9-slice border (fraction of shortest side), Image becomes Sliced
    //       "noTrim": false                    // transparent images are cropped to content unless true
    //     }
    //   ]
    // }
    [InitializeOnLoad]
    public static class AIImageBatch
    {
        // Codex results are picked up from a shared output folder, so run those one at a time
        private static int MaxParallel => OpenAIImageClient.Backend == ImageBackend.CodexCli ? 1 : 2;
        private const string LogPath = "Temp/AIImageBatch.log";
        private const string SessionLogKey = "AIImageBatch.Log";

        private static bool _reloadLocked;

        static AIImageBatch()
        {
            // Keep the last batch log readable after a domain reload
            Log.Append(SessionState.GetString(SessionLogKey, ""));
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        // Entering Play mode reloads scripts even when reloads are locked, which would drop the batch
        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingEditMode || !IsRunning) return;
            EditorApplication.isPlaying = false;
            string message = $"AI image batch is running ({Completed}/{Total}). Play mode is blocked until it finishes.";
            Debug.LogWarning("[AIImageBatch] " + message);
            foreach (SceneView view in SceneView.sceneViews) view.ShowNotification(new GUIContent(message), 4);
        }

        private static void OnBeforeAssemblyReload()
        {
            if (!IsRunning) return;
            Write($"INTERRUPTED by script reload: {Completed}/{Total} ok, {Pending.Count + Active.Count} not finished. Run the spec again.");
            UnlockReload();
        }

        private static void UnlockReload()
        {
            if (!_reloadLocked) return;
            _reloadLocked = false;
            EditorApplication.UnlockReloadAssemblies();
        }

        // Account-level errors: every remaining request would fail the same way, so stop the batch
        private static bool IsFatalError(string error)
        {
            if (string.IsNullOrEmpty(error)) return false;
            string e = error.ToLowerInvariant();
            return e.Contains("credits") || e.Contains("quota") || e.Contains("billing")
                   || e.Contains("api key") || e.Contains("unauthorized") || e.Contains("verify")
                   || e.Contains("not logged in") || e.Contains("codex login") || e.Contains("usage limit")
                   || e.Contains("could not start codex") || e.Contains("not exposed");
        }

        [Serializable]
        public class Item
        {
            public string name;
            public string preset = "Custom";
            public string prompt;
            public string size = "1024x1024";
            public string background = "transparent";
            public string quality;
            public string prefab;
            public string target;
            public bool setNativeSize;
            public float slice;          // 0 = simple sprite; e.g. 0.3 = 9-slice border of 30% of the shortest side
            public bool noTrim;          // keep the full canvas (transparent images are trimmed by default)
            public string[] references;  // per-item reference images (overrides the spec-level list)
        }

        [Serializable]
        public class Spec
        {
            public string folder = "Assets/Project/Art/Generated";
            public string style;
            public string model;
            public string quality = "medium";
            public bool overwrite = true;
            public string[] references;  // style reference images for every item (Codex backend)
            public Item[] items;
        }

        public static bool IsRunning { get; private set; }
        public static int Total { get; private set; }
        public static int Completed { get; private set; }
        public static int Failed { get; private set; }

        private static readonly StringBuilder Log = new StringBuilder();
        private static readonly Queue<Item> Pending = new Queue<Item>();
        private static readonly List<OpenAIImageClient.Handle> Active = new List<OpenAIImageClient.Handle>();
        private static Spec _spec;
        private static string _style;

        public static string Status =>
            $"{(IsRunning ? "RUNNING" : "IDLE")} {Completed}/{Total} done, {Failed} failed\n{Log}";

        [MenuItem("Tools/AI Image Generator/Run Batch Spec...")]
        private static void RunFromMenu()
        {
            string path = EditorUtility.OpenFilePanel("AI image batch spec", Application.dataPath, "json");
            if (!string.IsNullOrEmpty(path)) Run(path);
        }

        public static void Run(string specPath)
        {
            Run(specPath, false);
        }

        // skipExisting: only generate items whose PNG is not in the output folder yet (resume after an interruption)
        public static void Run(string specPath, bool skipExisting)
        {
            if (IsRunning)
            {
                Debug.LogWarning("[AIImageBatch] A batch is already running.");
                return;
            }

            Log.Clear();
            SessionState.SetString(SessionLogKey, "");
            Completed = 0;
            Failed = 0;
            Total = 0;

            if (!File.Exists(specPath))
            {
                Write($"ERROR spec not found: {specPath}");
                return;
            }
            string notReady = OpenAIImageClient.CheckReady();
            if (notReady != null)
            {
                Write("ERROR " + notReady);
                return;
            }

            _spec = JsonUtility.FromJson<Spec>(File.ReadAllText(specPath));
            if (_spec == null || _spec.items == null || _spec.items.Length == 0)
            {
                Write("ERROR spec has no items.");
                return;
            }

            _style = string.IsNullOrWhiteSpace(_spec.style)
                ? EditorPrefs.GetString(OpenAIImageClient.PrefStyle, OpenAIImageClient.DefaultStyle)
                : _spec.style;

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Write("ERROR stop Play mode first: entering or leaving Play mode reloads scripts and drops the batch.");
                return;
            }

            Pending.Clear();
            foreach (var item in _spec.items)
            {
                if (skipExisting && File.Exists($"{_spec.folder}/{item.name}.png"))
                {
                    Write($"SKIP  {item.name} (already generated)");
                    continue;
                }
                Pending.Enqueue(item);
            }
            Total = Pending.Count;
            if (Total == 0)
            {
                Write("DONE nothing to generate.");
                return;
            }
            IsRunning = true;
            // A script reload would drop in-flight requests (already paid for), so hold reloads until the batch ends
            EditorApplication.LockReloadAssemblies();
            _reloadLocked = true;
            Write($"START {Total} item(s) from {specPath}");
            Pump();
        }

        public static void Cancel()
        {
            if (!IsRunning) return;
            Pending.Clear();
            var active = Active.ToArray();
            Active.Clear();
            Finish("CANCELLED");
            foreach (var h in active) h.Cancel();
        }

        private static void Pump()
        {
            while (IsRunning && Active.Count < MaxParallel && Pending.Count > 0)
            {
                var item = Pending.Dequeue();
                Enum.TryParse(item.preset, true, out AssetPreset preset);
                var options = new ImageJobOptions
                {
                    model = string.IsNullOrEmpty(_spec.model)
                        ? EditorPrefs.GetString(OpenAIImageClient.PrefModel, "gpt-image-2")
                        : _spec.model,
                    prompt = OpenAIImageClient.BuildPrompt(item.prompt, preset, _style),
                    size = string.IsNullOrEmpty(item.size) ? "1024x1024" : item.size,
                    quality = !string.IsNullOrEmpty(item.quality) ? item.quality
                        : (string.IsNullOrEmpty(_spec.quality) ? "medium" : _spec.quality),
                    background = string.IsNullOrEmpty(item.background) ? "transparent" : item.background,
                    referenceImages = item.references != null && item.references.Length > 0 ? item.references : _spec.references,
                };

                Write($"GEN   {item.name} ({preset}, {options.size}, {options.quality})");
                OpenAIImageClient.Handle handle = null;
                handle = OpenAIImageClient.Generate(options, (png, error) =>
                {
                    Active.Remove(handle);
                    OnItemDone(item, png, error);
                    Pump();
                });
                if (!handle.IsDone) Active.Add(handle);
            }

            if (IsRunning && Active.Count == 0 && Pending.Count == 0) Finish("DONE");
        }

        private static void OnItemDone(Item item, byte[] png, string error)
        {
            if (png == null)
            {
                Failed++;
                Write($"FAIL  {item.name}: {error}");
                if (IsRunning && IsFatalError(error) && (Pending.Count > 0 || Active.Count > 0))
                {
                    Write($"STOP  account error, skipping {Pending.Count} queued item(s).");
                    Failed += Pending.Count;
                    Pending.Clear();
                    foreach (var h in Active.ToArray()) h.Cancel();
                }
                return;
            }

            try
            {
                if (!item.noTrim && item.background != "opaque") png = OpenAIImageClient.TrimTransparent(png);
                string assetPath = OpenAIImageClient.SavePngAsSprite(png, _spec.folder, item.name, _spec.overwrite);
                OpenAIImageClient.SetSliceBorder(assetPath, item.slice);
                string assigned = AssignToTarget(item, assetPath);
                Completed++;
                Write($"OK    {item.name} -> {assetPath}{assigned}");
            }
            catch (Exception e)
            {
                Failed++;
                Write($"FAIL  {item.name}: {e.Message}");
            }
        }

        private static string AssignToTarget(Item item, string assetPath)
        {
            if (string.IsNullOrEmpty(item.target) && string.IsNullOrEmpty(item.prefab)) return "";
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);

            if (!string.IsNullOrEmpty(item.prefab))
            {
                var root = PrefabUtility.LoadPrefabContents(item.prefab);
                try
                {
                    var go = string.IsNullOrEmpty(item.target) ? root : FindChild(root.transform, item.target);
                    if (go == null) return $" (target '{item.target}' not found in {item.prefab})";
                    string comp = OpenAIImageClient.AssignSprite(go, sprite, item.setNativeSize);
                    if (comp == null) return $" (no Image/SpriteRenderer on '{item.target}')";
                    PrefabUtility.SaveAsPrefabAsset(root, item.prefab);
                    return $" assigned to {comp} on {item.prefab}:{item.target}";
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            var sceneObj = FindInOpenScenes(item.target);
            if (sceneObj == null) return $" (target '{item.target}' not found in open scenes)";
            string c = OpenAIImageClient.AssignSprite(sceneObj, sprite, item.setNativeSize);
            if (c == null) return $" (no Image/SpriteRenderer on '{item.target}')";
            EditorSceneManager.MarkSceneDirty(sceneObj.scene);
            return $" assigned to {c} on scene object {item.target} (scene not saved)";
        }

        private static GameObject FindChild(Transform root, string path)
        {
            var t = root.Find(path);
            if (t == null && path.StartsWith(root.name + "/")) t = root.Find(path.Substring(root.name.Length + 1));
            return t != null ? t.gameObject : null;
        }

        private static GameObject FindInOpenScenes(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            string[] parts = path.Split(new[] { '/' }, 2);
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root.name != parts[0]) continue;
                    if (parts.Length == 1) return root;
                    var child = root.transform.Find(parts[1]);
                    if (child != null) return child.gameObject;
                }
            }
            return null;
        }

        private static void Finish(string state)
        {
            if (!IsRunning) return;
            IsRunning = false;
            UnlockReload();
            AssetDatabase.SaveAssets();
            Write($"{state} {Completed}/{Total} ok, {Failed} failed");
        }

        private static void Write(string line)
        {
            string stamped = $"[{DateTime.Now:HH:mm:ss}] {line}";
            Log.AppendLine(stamped);
            SessionState.SetString(SessionLogKey, Log.ToString());
            Debug.Log("[AIImageBatch] " + line);
            try { File.AppendAllText(LogPath, stamped + Environment.NewLine); } catch { }
        }
    }
}
