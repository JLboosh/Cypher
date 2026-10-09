using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Cypher
{
    /// <summary>
    /// Shows a macOS notification through AppleScript's "display notification" (built into macOS;
    /// no paid developer account or extra tools). macOS lists them under "Script Editor" in
    /// System Settings → Notifications; allow notifications there if you don't see them.
    /// </summary>
    public static class MacNotifier
    {
        public static void Show(string title, string message, string soundName = "Glass")
        {
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            string script = $"display notification {AppleString(message)} with title {AppleString(title)}" +
                            (string.IsNullOrEmpty(soundName) ? "" : $" sound name {AppleString(soundName)}");
            string path = Path.Combine(Application.temporaryCachePath, $"cypher-notify-{Guid.NewGuid():N}.applescript");
            try
            {
                File.WriteAllText(path, script);
                Task.Run(() =>
                {
                    var psi = new ProcessStartInfo("/usr/bin/osascript", "\"" + path + "\"")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardError = true,
                    };
                    using (var p = Process.Start(psi))
                    {
                        string err = p?.StandardError.ReadToEnd();
                        p?.WaitForExit(5000);
                        if (!string.IsNullOrWhiteSpace(err)) Debug.LogWarning("[Cypher] Notification failed: " + err);
                    }
                    try { File.Delete(path); } catch { /* temp file */ }
                });
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Cypher] Notification failed: " + e.Message);
            }
#endif
        }

        /// <summary>Quotes text as an AppleScript string literal.</summary>
        static string AppleString(string s) =>
            "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ") + "\"";
    }
}
