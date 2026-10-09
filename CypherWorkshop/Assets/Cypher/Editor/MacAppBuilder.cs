using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace Cypher.EditorTools
{
    /// <summary>
    /// Menu: Cypher > Build macOS App. Produces ~/cypher/Builds/Cypher.app:
    ///   - generated app icon, windowed + resizable, runs in the background, no splash screen,
    ///   - every shader the app looks up at runtime is force-included,
    ///   - Info.plist: microphone permission text, App Nap disabled (reminders on time when
    ///     minimized), display name "Cypher",
    ///   - re-signed ad-hoc (free, no Apple Developer account) so macOS accepts the edited bundle.
    /// The app keeps the editor's company/product names, so it shares the same tasks.json,
    /// config and Google token as Play mode.
    /// </summary>
    public static class MacAppBuilder
    {
        const string ScenePath = "Assets/Cypher/Scenes/Workshop.unity";
        const string IconPath = "Assets/Cypher/Generated/AppIcon.png";
        const string AppName = "Cypher";

        static readonly string[] RuntimeShaders =
        {
            "Cypher/Hologram", "Cypher/GlowParticle", "Cypher/UIGlow", "Cypher/UIGlass", "Cypher/GlowLine",
            "Cypher/HologramCrumple", "Cypher/HoloHand", "Cypher/HoloBeam",
        };

        static string BuildDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Builds"));
        static string AppPath => Path.Combine(BuildDir, AppName + ".app");

        [MenuItem("Cypher/Build macOS App", priority = 40)]
        public static void Build()
        {
            if (!PlayModeGuard.EnsureEditMode("app", "Building the Mac app")) return;
            if (!File.Exists(ScenePath))
            {
                EditorUtility.DisplayDialog("Cypher", "Run Cypher > Build Workshop Scene first.", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            ConfigurePlayer();
            IncludeRuntimeShaders();

            Directory.CreateDirectory(BuildDir);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = AppPath,
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.None,
            });

            if (report.summary.result != BuildResult.Succeeded)
            {
                EditorUtility.DisplayDialog("Cypher build failed",
                    $"Result: {report.summary.result}. See the Console for the errors.", "OK");
                return;
            }

            bool plistOk = PatchInfoPlist(AppPath);
            bool signOk = Run("/usr/bin/codesign", $"--force --deep --sign - \"{AppPath}\"", out string signOutput);
            if (!signOk) Debug.LogWarning("[Cypher] codesign: " + signOutput);

            ulong mb = report.summary.totalSize / (1024 * 1024);
            Debug.Log($"[Cypher] Built {AppPath} ({mb} MB). Info.plist patched: {plistOk}. Signed: {signOk}.");
            if (EditorUtility.DisplayDialog("Cypher is built",
                    $"{AppPath}\n\nDouble-click it to launch. Drag it to Applications if you like.", "Show in Finder", "Close"))
                EditorUtility.RevealInFinder(AppPath);
        }

        [MenuItem("Cypher/Open Builds Folder", priority = 41)]
        static void OpenBuilds()
        {
            Directory.CreateDirectory(BuildDir);
            EditorUtility.RevealInFinder(BuildDir + "/");
        }

        // ------------------------------------------------------------------ settings

        static void ConfigurePlayer()
        {
            var standalone = NamedBuildTarget.Standalone;
            PlayerSettings.SetApplicationIdentifier(standalone, "com.personal.cypher");
            PlayerSettings.SetScriptingBackend(standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.runInBackground = true;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 1000;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.allowFullscreenSwitch = true;
            PlayerSettings.macRetinaSupport = true;
            try { PlayerSettings.SplashScreen.show = false; }
            catch (Exception e) { Debug.LogWarning("[Cypher] Couldn't turn off the splash screen: " + e.Message); }

            var icon = GenerateIcon();
            // The "Default Icon" slot: Unity scales it to every size macOS needs.
            if (icon != null) PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
        }

        /// <summary>Adds the shaders found with Shader.Find at runtime to "Always Included Shaders".</summary>
        static void IncludeRuntimeShaders()
        {
            var settings = new SerializedObject(GraphicsSettings.GetGraphicsSettings());
            var list = settings.FindProperty("m_AlwaysIncludedShaders");
            foreach (var name in RuntimeShaders)
            {
                var shader = Shader.Find(name);
                if (shader == null)
                {
                    Debug.LogWarning($"[Cypher] Shader {name} not found (compile error?).");
                    continue;
                }
                bool present = false;
                for (int i = 0; i < list.arraySize; i++)
                    if (list.GetArrayElementAtIndex(i).objectReferenceValue == shader) present = true;
                if (present) continue;
                list.InsertArrayElementAtIndex(list.arraySize);
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
            }
            settings.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
        }

        // ------------------------------------------------------------------ Info.plist + signing

        static bool PatchInfoPlist(string app)
        {
            string plist = Path.Combine(app, "Contents", "Info.plist");
            if (!File.Exists(plist)) return false;
            bool ok = true;
            ok &= SetPlist(plist, "NSMicrophoneUsageDescription", "string",
                "Cypher listens for Hey Cypher and your voice commands. Speech is processed on this Mac.");
            ok &= SetPlist(plist, "NSAppSleepDisabled", "bool", "true");          // App Nap off: reminders on time
            ok &= SetPlist(plist, "LSApplicationCategoryType", "string", "public.app-category.productivity");
            ok &= SetPlist(plist, "CFBundleDisplayName", "string", AppName);
            ok &= SetPlist(plist, "CFBundleName", "string", AppName);
            return ok;
        }

        static bool SetPlist(string plist, string key, string type, string value)
        {
            Run("/usr/libexec/PlistBuddy", $"-c \"Delete :{key}\" \"{plist}\"", out _); // fine if it didn't exist
            bool ok = Run("/usr/libexec/PlistBuddy", $"-c \"Add :{key} {type} {value}\" \"{plist}\"", out string output);
            if (!ok) Debug.LogWarning($"[Cypher] PlistBuddy {key}: {output}");
            return ok;
        }

        static bool Run(string exe, string args, out string output)
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using (var p = Process.Start(psi))
            {
                output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit();
                return p.ExitCode == 0;
            }
        }

        // ------------------------------------------------------------------ icon

        /// <summary>Draws a 1024 px icon: navy rounded square, glowing cyan "C" ring, bright core.</summary>
        static Texture2D GenerateIcon()
        {
            const int size = 1024;
            var px = new Color32[size * size];
            var center = new Vector2(size / 2f, size / 2f);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                // Rounded square mask (macOS-style margins).
                Vector2 q = new Vector2(Mathf.Abs(p.x - center.x), Mathf.Abs(p.y - center.y)) - new Vector2(400f, 400f) + Vector2.one * 180f;
                float box = Mathf.Min(Mathf.Max(q.x, q.y), 0f) + new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude - 180f;
                float mask = Mathf.Clamp01(0.5f - box);
                if (mask <= 0f) { px[y * size + x] = new Color32(0, 0, 0, 0); continue; }

                float t = y / (float)size;
                Color c = Color.Lerp(new Color(0.02f, 0.05f, 0.1f), new Color(0.06f, 0.15f, 0.24f), t);

                float d = Vector2.Distance(p, center);
                float angle = Mathf.Atan2(p.y - center.y, p.x - center.x) * Mathf.Rad2Deg;
                bool gap = Mathf.Abs(angle) < 38f; // the opening of the "C"
                float ring = gap ? 0f : Mathf.Exp(-Mathf.Pow((d - 300f) / 22f, 2f));
                float halo = gap ? 0f : Mathf.Exp(-Mathf.Pow((d - 300f) / 90f, 2f)) * 0.35f;
                float inner = Mathf.Exp(-Mathf.Pow((d - 205f) / 6f, 2f)) * 0.6f;
                float core = Mathf.Exp(-d * d / (2f * 55f * 55f));
                var cyan = new Color(0.35f, 0.9f, 1f);
                c += cyan * (ring + halo + inner) + new Color(0.75f, 0.97f, 1f) * core;

                c.a = mask;
                px[y * size + x] = c;
            }

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            Directory.CreateDirectory(Path.GetDirectoryName(IconPath));
            File.WriteAllBytes(IconPath, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(IconPath);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
        }
    }
}
