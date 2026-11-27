using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace Project.Tools.Editor.AIImageGenerator
{
    public enum AssetPreset { Custom, Button, Icon, PopupPanel, Background, Logo, Banner }

    // CodexCli: uses the Codex CLI signed in with a ChatGPT account (billed to the ChatGPT plan).
    // OpenAIApi: uses the Images API with an API key (billed per image to API credits).
    public enum ImageBackend { CodexCli, OpenAIApi }

    public class ImageJobOptions
    {
        public string model = "gpt-image-2";
        public string prompt;
        public string size = "1024x1024";
        public string quality = "medium";
        public string background = "transparent";
        public string[] referenceImages;   // absolute paths; Codex backend only (style/layout references)
    }

    // Shared image generation client for the editor window and the batch runner.
    // The API key (API backend only) lives in EditorPrefs on this machine only.
    public static class OpenAIImageClient
    {
        public const string PrefKey = "OpenAIImage.ApiKey";
        public const string PrefModel = "OpenAIImage.Model";
        public const string PrefStyle = "OpenAIImage.Style";
        public const string PrefFolder = "OpenAIImage.Folder";
        private const string Endpoint = "https://api.openai.com/v1/images/generations";

        public const string DefaultStyle =
            "Casual mobile puzzle game UI art, bright cheerful colors, soft gradients, glossy highlights, " +
            "rounded shapes, clean outlines, subtle soft shadow, high quality, consistent game asset style.";

        public const string PrefBackend = "OpenAIImage.Backend";

        public static string ApiKey => EditorPrefs.GetString(PrefKey, "");
        public static bool HasApiKey => !string.IsNullOrEmpty(ApiKey);

        public static ImageBackend Backend
        {
            get => (ImageBackend)EditorPrefs.GetInt(PrefBackend, (int)ImageBackend.CodexCli);
            set => EditorPrefs.SetInt(PrefBackend, (int)value);
        }

        // Returns null when the current backend can run, otherwise the reason it cannot.
        public static string CheckReady()
        {
            if (Backend == ImageBackend.OpenAIApi)
            {
                return HasApiKey ? null : "No OpenAI API key. Set it in Tools > AI Image Generator (ChatGPT).";
            }
            return CodexImageRunner.FindCodex() != null
                ? null
                : "Codex CLI not found. Install it and run 'codex login' (see the AI Image Generator window).";
        }

        public class Handle
        {
            internal Action<Handle> Poller;
            internal Action Canceller;
            internal Action<byte[], string> OnDone;
            public double StartTime { get; internal set; }
            public bool IsDone { get; private set; }

            internal void Tick()
            {
                if (!IsDone) Poller?.Invoke(this);
            }

            internal void Complete(byte[] png, string error)
            {
                if (IsDone) return;
                IsDone = true;
                EditorApplication.update -= Tick;
                OnDone?.Invoke(png, error);
            }

            public void Cancel()
            {
                if (IsDone) return;
                try { Canceller?.Invoke(); } catch { }
                Complete(null, "Cancelled.");
            }
        }

        public static Handle Generate(ImageJobOptions options, Action<byte[], string> onDone)
        {
            var handle = new Handle { OnDone = onDone, StartTime = EditorApplication.timeSinceStartup };
            string notReady = CheckReady();
            if (notReady != null)
            {
                handle.Complete(null, notReady);
                return handle;
            }

            if (Backend == ImageBackend.CodexCli) CodexImageRunner.Start(handle, options);
            else StartApiRequest(handle, options);

            if (!handle.IsDone) EditorApplication.update += handle.Tick;
            return handle;
        }

        [Serializable]
        private class ImageRequest
        {
            public string model;
            public string prompt;
            public string size;
            public string quality;
            public string background;
            public string output_format = "png";
            public int n = 1;
        }

        [Serializable] private class ImageData { public string b64_json; }
        [Serializable] private class ImageResponse { public ImageData[] data; }
        [Serializable] private class ApiError { public string message; }
        [Serializable] private class ErrorResponse { public ApiError error; }

        private static void StartApiRequest(Handle handle, ImageJobOptions options)
        {
            var body = new ImageRequest
            {
                model = string.IsNullOrEmpty(options.model) ? "gpt-image-2" : options.model,
                prompt = options.prompt,
                size = options.size,
                quality = options.quality,
                background = options.background,
            };

            var request = new UnityWebRequest(Endpoint, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(body))),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 300
            };
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + ApiKey);
            request.SendWebRequest();

            handle.Canceller = () =>
            {
                request.Abort();
                request.Dispose();
            };
            handle.Poller = h =>
            {
                if (!request.isDone) return;
                byte[] png = null;
                string error;
                try
                {
                    png = ParseResponse(request, out error);
                }
                catch (Exception e)
                {
                    error = e.Message;
                }
                finally
                {
                    request.Dispose();
                }
                h.Complete(png, error);
            };
        }

        private static byte[] ParseResponse(UnityWebRequest req, out string error)
        {
            error = null;
            string text = req.downloadHandler != null ? req.downloadHandler.text : "";
            if (req.result != UnityWebRequest.Result.Success)
            {
                error = req.error;
                if (!string.IsNullOrEmpty(text))
                {
                    var err = JsonUtility.FromJson<ErrorResponse>(text);
                    if (err != null && err.error != null && !string.IsNullOrEmpty(err.error.message)) error = err.error.message;
                }
                return null;
            }

            var response = JsonUtility.FromJson<ImageResponse>(text);
            if (response == null || response.data == null || response.data.Length == 0 || string.IsNullOrEmpty(response.data[0].b64_json))
            {
                error = "The response contained no image.";
                return null;
            }
            return Convert.FromBase64String(response.data[0].b64_json);
        }

        public static string BuildPrompt(string description, AssetPreset preset, string style)
        {
            string presetText;
            switch (preset)
            {
                case AssetPreset.Button:
                    presetText = "A single game UI button, centered, front view, no text unless requested, isolated on an empty background.";
                    break;
                case AssetPreset.Icon:
                    presetText = "A single game icon, centered, simple readable silhouette at small sizes, isolated on an empty background.";
                    break;
                case AssetPreset.PopupPanel:
                    presetText = "A game popup panel / dialog frame, centered, empty interior for content, suitable for 9-slice scaling, no text.";
                    break;
                case AssetPreset.Background:
                    presetText = "A full-screen game background, no UI elements, no text, leave calm space in the center for gameplay.";
                    break;
                case AssetPreset.Logo:
                    presetText = "A game logo, centered, bold and playful lettering, isolated on an empty background.";
                    break;
                case AssetPreset.Banner:
                    presetText = "A horizontal game ribbon / banner for a title, centered, empty space for text, isolated on an empty background.";
                    break;
                default:
                    presetText = "";
                    break;
            }

            var sb = new StringBuilder();
            sb.Append((description ?? "").Trim());
            if (presetText.Length > 0) sb.Append("\n").Append(presetText);
            if (!string.IsNullOrWhiteSpace(style)) sb.Append("\nStyle: ").Append(style.Trim());
            return sb.ToString();
        }

        // Saves the PNG under folder/fileName.png. When overwrite is true an existing file is replaced
        // in place, so every reference to that sprite (GUID) keeps working.
        // Crops fully transparent margins (generated UI pieces usually float in a large empty canvas).
        public static byte[] TrimTransparent(byte[] png, int padding = 4, float alphaThreshold = 0.03f)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!tex.LoadImage(png)) return png;
                int w = tex.width, h = tex.height;
                Color32[] px = tex.GetPixels32();
                byte limit = (byte)(alphaThreshold * 255f);
                int minX = w, minY = h, maxX = -1, maxY = -1;
                for (int y = 0; y < h; y++)
                {
                    int row = y * w;
                    for (int x = 0; x < w; x++)
                    {
                        if (px[row + x].a <= limit) continue;
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
                if (maxX < 0) return png;

                minX = Mathf.Max(0, minX - padding);
                minY = Mathf.Max(0, minY - padding);
                maxX = Mathf.Min(w - 1, maxX + padding);
                maxY = Mathf.Min(h - 1, maxY + padding);
                int cw = maxX - minX + 1, ch = maxY - minY + 1;
                if (cw == w && ch == h) return png;

                var cropped = new Texture2D(cw, ch, TextureFormat.RGBA32, false);
                cropped.SetPixels(tex.GetPixels(minX, minY, cw, ch));
                cropped.Apply();
                byte[] result = cropped.EncodeToPNG();
                UnityEngine.Object.DestroyImmediate(cropped);
                return result;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        // slice > 0: sets an even 9-slice border of (slice * shortest side) so frames/buttons stretch without warping
        public static void SetSliceBorder(string assetPath, float slice)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null) return;
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (tex == null) return;
            float b = Mathf.Round(Mathf.Min(tex.width, tex.height) * Mathf.Clamp(slice, 0f, 0.49f));
            importer.spriteBorder = new Vector4(b, b, b, b);
            importer.SaveAndReimport();
        }

        public static string SavePngAsSprite(byte[] png, string folder, string fileName, bool overwrite)
        {
            if (!AssetDatabase.IsValidFolder(folder))
            {
                Directory.CreateDirectory(folder);
                AssetDatabase.Refresh();
            }

            string safeName = string.IsNullOrWhiteSpace(fileName) ? "ui_asset" : fileName.Trim();
            foreach (char c in Path.GetInvalidFileNameChars()) safeName = safeName.Replace(c, '_');
            string assetPath = $"{folder}/{safeName}.png";
            if (!overwrite) assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);

            File.WriteAllBytes(assetPath, png);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }
            return assetPath;
        }

        // Assigns the sprite to an Image, SpriteRenderer or RawImage on the object. Returns the component name or null.
        public static string AssignSprite(GameObject go, Sprite sprite, bool setNativeSize)
        {
            if (go == null || sprite == null) return null;

            var image = go.GetComponent<Image>();
            if (image != null)
            {
                Undo.RecordObject(image, "Assign AI Sprite");
                image.sprite = sprite;
                if (sprite.border != Vector4.zero)
                {
                    image.type = Image.Type.Sliced;
                    image.fillCenter = true;
                    // Scale the borders so the sprite's height maps 1:1 onto the target height (corners keep their shape)
                    float targetHeight = ((RectTransform)image.transform).rect.height;
                    if (targetHeight > 1f) image.pixelsPerUnitMultiplier = Mathf.Max(0.01f, sprite.rect.height / targetHeight);
                }
                else if (image.type == Image.Type.Sliced)
                {
                    image.type = Image.Type.Simple;
                    image.preserveAspect = true;
                }
                if (setNativeSize) image.SetNativeSize();
                EditorUtility.SetDirty(image);
                return "Image";
            }

            var spriteRenderer = go.GetComponent<SpriteRenderer>();
            if (spriteRenderer != null)
            {
                Undo.RecordObject(spriteRenderer, "Assign AI Sprite");
                spriteRenderer.sprite = sprite;
                EditorUtility.SetDirty(spriteRenderer);
                return "SpriteRenderer";
            }

            var rawImage = go.GetComponent<RawImage>();
            if (rawImage != null)
            {
                Undo.RecordObject(rawImage, "Assign AI Sprite");
                rawImage.texture = sprite.texture;
                if (setNativeSize) rawImage.SetNativeSize();
                EditorUtility.SetDirty(rawImage);
                return "RawImage";
            }
            return null;
        }
    }
}
