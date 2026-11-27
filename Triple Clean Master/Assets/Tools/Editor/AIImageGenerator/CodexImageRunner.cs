using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace Project.Tools.Editor.AIImageGenerator
{
    // Generates an image through the Codex CLI ($imagegen) signed in with a ChatGPT account.
    // Codex saves results to <CODEX_HOME>/generated_images/<session-id>/*.png; we pick the file up from there.
    public static class CodexImageRunner
    {
        public const string PrefCodexPath = "OpenAIImage.CodexPath";
        private const double TimeoutSeconds = 420;
        private static double _lastAppLookup;

        public static string FindCodex()
        {
            string custom = EditorPrefs.GetString(PrefCodexPath, "");
            if (!string.IsNullOrEmpty(custom) && File.Exists(custom)) return custom;

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var candidates = (Environment.GetEnvironmentVariable("PATH") ?? "")
                .Split(Path.PathSeparator)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .SelectMany(p => new[] { Path.Combine(p.Trim(), "codex.exe"), Path.Combine(p.Trim(), "codex.cmd") })
                .Concat(new[]
                {
                    Path.Combine(home, ".local", "bin", "codex.exe"),
                    Path.Combine(appData, "npm", "codex.cmd"),
                });

            foreach (var c in candidates)
            {
                try { if (File.Exists(c)) return c; } catch { }
            }
            return FindCodexInDesktopApp();
        }

        // The Codex desktop app (Microsoft Store) bundles the CLI at <InstallLocation>\app\resources\codex.exe.
        // WindowsApps cannot be listed, so ask PowerShell for the install location; the path changes on every app update.
        private static string FindCodexInDesktopApp()
        {
            const string cacheKey = "CodexImage.AppCodexPath";
            string cached = SessionState.GetString(cacheKey, "");
            if (!string.IsNullOrEmpty(cached) && File.Exists(cached)) return cached;

            // Called from OnGUI: don't spawn PowerShell on every repaint when the app is not installed
            double now = EditorApplication.timeSinceStartup;
            if (now - _lastAppLookup < 30 && _lastAppLookup > 0) return null;
            _lastAppLookup = now;

            try
            {
                var psi = new ProcessStartInfo("powershell",
                    "-NoProfile -Command \"(Get-AppxPackage -Name OpenAI.Codex | Sort-Object Version -Descending | Select-Object -First 1).InstallLocation\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                };
                using (var p = Process.Start(psi))
                {
                    string location = p.StandardOutput.ReadToEnd().Trim();
                    p.WaitForExit(10000);
                    if (string.IsNullOrEmpty(location)) return null;
                    string exe = Path.Combine(location, "app", "resources", "codex.exe");
                    if (!File.Exists(exe)) return null;
                    SessionState.SetString(cacheKey, exe);
                    return exe;
                }
            }
            catch
            {
                return null;
            }
        }

        private static string CodexHome()
        {
            string env = Environment.GetEnvironmentVariable("CODEX_HOME");
            if (!string.IsNullOrEmpty(env)) return env;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        }

        private static ProcessStartInfo CreateStartInfo(string codex, string args, string workDir)
        {
            bool isCmd = codex.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase);
            return new ProcessStartInfo
            {
                FileName = isCmd ? "cmd.exe" : codex,
                Arguments = isCmd ? $"/c \"\"{codex}\" {args}\"" : args,
                WorkingDirectory = workDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
        }

        // Runs a short Codex command synchronously (e.g. "login status") and returns its output.
        public static string RunQuick(string args, int timeoutMs = 20000)
        {
            string codex = FindCodex();
            if (codex == null) return "Codex CLI not found.";
            try
            {
                using (var p = Process.Start(CreateStartInfo(codex, args, Environment.CurrentDirectory)))
                {
                    p.StandardInput.Close();
                    var stdout = p.StandardOutput.ReadToEndAsync();
                    var stderr = p.StandardError.ReadToEndAsync();
                    if (!p.WaitForExit(timeoutMs))
                    {
                        try { p.Kill(); } catch { }
                        return "Timed out.";
                    }
                    return (stdout.Result + "\n" + stderr.Result).Trim();
                }
            }
            catch (Exception e)
            {
                return "Error: " + e.Message;
            }
        }

        private static string BuildCodexPrompt(ImageJobOptions options)
        {
            string aspect;
            switch (options.size)
            {
                case "1536x1024": aspect = "landscape 3:2 (1536x1024)"; break;
                case "1024x1536": aspect = "portrait 2:3 (1024x1536)"; break;
                case "1024x1024": aspect = "square 1:1 (1024x1024)"; break;
                default: aspect = "whatever fits the subject best"; break;
            }

            string background = options.background == "transparent"
                ? "transparent background (PNG with alpha channel), no backdrop, no floor shadow"
                : options.background == "opaque" ? "fully opaque, the image fills the whole canvas" : "any";

            var sb = new StringBuilder();
            sb.AppendLine("$imagegen");
            var refs = ValidReferences(options);
            if (refs.Length > 0)
            {
                sb.AppendLine("Reference images (match their art style, colors, materials, outlines and lighting; do not copy their layout unless asked):");
                for (int i = 0; i < refs.Length; i++) sb.AppendLine($"Image {i + 1}: {refs[i]}");
                sb.AppendLine();
            }
            sb.AppendLine(options.prompt);
            sb.AppendLine();
            sb.AppendLine("Output requirements:");
            sb.AppendLine("- Generate exactly one image with the built-in image generation tool.");
            sb.AppendLine("- Aspect ratio / size: " + aspect + ".");
            sb.AppendLine("- Background: " + background + ".");
            sb.AppendLine("- Do not write code, do not call any API, do not edit files in the workspace.");
            sb.AppendLine("- When finished, reply only with the absolute path of the generated PNG.");
            return sb.ToString();
        }

        public static void Start(OpenAIImageClient.Handle handle, ImageJobOptions options)
        {
            string codex = FindCodex();
            string workDir = Path.GetFullPath(Path.Combine("Temp", "CodexImageGen", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(workDir);

            DateTime startUtc = DateTime.UtcNow.AddSeconds(-2);
            var output = new StringBuilder();
            Process process;
            try
            {
                var argsBuilder = new StringBuilder("exec -C . -s workspace-write --skip-git-repo-check --color never");
                foreach (var r in ValidReferences(options)) argsBuilder.Append(" --image \"").Append(r).Append('"');
                argsBuilder.Append(" -- -");
                string args = argsBuilder.ToString();
                process = Process.Start(CreateStartInfo(codex, args, workDir));
                process.OutputDataReceived += (s, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                byte[] prompt = Encoding.UTF8.GetBytes(BuildCodexPrompt(options));
                process.StandardInput.BaseStream.Write(prompt, 0, prompt.Length);
                process.StandardInput.Close();
            }
            catch (Exception e)
            {
                handle.Complete(null, "Could not start Codex CLI: " + e.Message);
                return;
            }

            handle.Canceller = () => KillTree(process);
            handle.Poller = h =>
            {
                bool exited;
                try { exited = process.HasExited; } catch { exited = true; }

                if (!exited)
                {
                    if (EditorApplication.timeSinceStartup - h.StartTime > TimeoutSeconds)
                    {
                        KillTree(process);
                        h.Complete(null, "Codex timed out.");
                    }
                    return;
                }

                process.WaitForExit();
                string log;
                lock (output) log = output.ToString();
                process.Dispose();

                string png = FindGeneratedPng(log, workDir, startUtc);
                if (png == null)
                {
                    string tail = log.Length > 800 ? log.Substring(log.Length - 800) : log;
                    Debug.LogWarning("[CodexImage] No image produced. Codex output:\n" + log);
                    h.Complete(null, "Codex did not produce an image. " + tail.Trim());
                    return;
                }

                byte[] bytes = File.ReadAllBytes(png);
                try { Directory.Delete(workDir, true); } catch { }
                h.Complete(bytes, null);
            };
        }

        private static string[] ValidReferences(ImageJobOptions options)
        {
            if (options.referenceImages == null) return new string[0];
            return options.referenceImages
                .Where(p => !string.IsNullOrEmpty(p))
                .Select(p => Path.GetFullPath(p))
                .Where(File.Exists)
                .Take(16)
                .ToArray();
        }

        private static string FindGeneratedPng(string log, string workDir, DateTime startUtc)
        {
            string imagesRoot = Path.Combine(CodexHome(), "generated_images");

            // Prefer the folder of this exact session when Codex printed its id
            var match = Regex.Match(log, @"session id:\s*([0-9a-fA-F-]{8,})");
            if (match.Success)
            {
                string sessionDir = Path.Combine(imagesRoot, match.Groups[1].Value);
                string fromSession = Newest(sessionDir, DateTime.MinValue);
                if (fromSession != null) return fromSession;
            }

            // A path the model replied with
            foreach (Match m in Regex.Matches(log, @"[A-Za-z]:\\[^\s""'<>|]+?\.png", RegexOptions.IgnoreCase))
            {
                if (File.Exists(m.Value) && File.GetLastWriteTimeUtc(m.Value) >= startUtc) return m.Value;
            }

            return Newest(workDir, startUtc) ?? Newest(imagesRoot, startUtc);
        }

        private static string Newest(string dir, DateTime notBeforeUtc)
        {
            if (!Directory.Exists(dir)) return null;
            return Directory.GetFiles(dir, "*.png", SearchOption.AllDirectories)
                .Where(f => File.GetLastWriteTimeUtc(f) >= notBeforeUtc)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }

        private static void KillTree(Process process)
        {
            try
            {
                if (process == null || process.HasExited) return;
                using (var killer = Process.Start(new ProcessStartInfo("taskkill", $"/PID {process.Id} /T /F")
                       { CreateNoWindow = true, UseShellExecute = false }))
                {
                    killer?.WaitForExit(5000);
                }
            }
            catch { }
        }
    }
}
