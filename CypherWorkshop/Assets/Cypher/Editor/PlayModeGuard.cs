using System;
using UnityEditor;

namespace Cypher.EditorTools
{
    /// <summary>
    /// Scenes and apps can't be built while Play mode is running. Menu commands call
    /// PlayModeGuard.EnsureEditMode(...): if Play is on, it offers to stop it and then runs the
    /// command automatically once the editor is back in Edit mode.
    /// </summary>
    [InitializeOnLoad]
    public static class PlayModeGuard
    {
        const string PendingKey = "Cypher.PendingBuildCommand";

        static PlayModeGuard()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredEditMode) return;
                string pending = SessionState.GetString(PendingKey, "");
                SessionState.EraseString(PendingKey);
                if (pending == "scene") EditorApplication.delayCall += () => WorkshopSceneBuilder.Build(askFirst: false);
                if (pending == "app") EditorApplication.delayCall += MacAppBuilder.Build;
            };
        }

        /// <returns>true if it's safe to continue right now.</returns>
        public static bool EnsureEditMode(string command, string what)
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode) return true;
            if (EditorUtility.DisplayDialog("Stop Play mode first?",
                    $"{what} can't run while the game is playing.\n\nStop Play mode and continue automatically?",
                    "Stop Play and continue", "Cancel"))
            {
                SessionState.SetString(PendingKey, command);
                EditorApplication.ExitPlaymode();
            }
            return false;
        }
    }
}
