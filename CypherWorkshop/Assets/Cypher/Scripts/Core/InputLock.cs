using System.Collections.Generic;
using UnityEngine;

namespace Cypher
{
    /// <summary>
    /// Global "someone else owns the mouse/keyboard right now" flag.
    /// The edit panel (Phase 3) and voice dialogs (Phase 4) acquire it so that
    /// dragging, camera rotation and debug hotkeys pause while you type.
    /// </summary>
    public static class InputLock
    {
        static readonly HashSet<object> owners = new HashSet<object>();

        public static bool IsLocked => owners.Count > 0;

        public static void Acquire(object owner) => owners.Add(owner);

        public static void Release(object owner) => owners.Remove(owner);

        // Static state survives between Play sessions when domain reload is off; clear it.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => owners.Clear();
    }
}
