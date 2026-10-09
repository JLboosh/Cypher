using UnityEngine;
using UnityEngine.InputSystem;

namespace Cypher
{
    /// <summary>
    /// Phase 1 test shortcuts (voice and the edit panel replace these later; remove the
    /// component from "Systems" whenever you like):
    ///   N               add a test task
    ///   Shift+Backspace delete the hovered task (plays the crumple-and-throw animation)
    ///   Shift+R         forget manual drags and let the layout place everything again
    ///   Shift+I         pretend the hovered task was imported (cycles Local / Google Calendar / Notion)
    ///                   so you can test the "update the original?" question in the edit panel
    ///   Shift+M         set the hovered task's reminder to 10 seconds from now (adds a due date if needed)
    /// </summary>
    public class DebugTaskHotkeys : MonoBehaviour
    {
        [SerializeField] InteractionController interaction;

        int counter = 1;

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || InputLock.IsLocked || TaskManager.Instance == null) return;

            bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;

            if (kb.nKey.wasPressedThisFrame && !shift)
                TaskManager.Instance.AddTask($"Test task {counter++}");

            if (shift && kb.backspaceKey.wasPressedThisFrame && interaction != null && interaction.Hovered != null)
                TaskManager.Instance.RemoveTask(interaction.Hovered.Task.id);

            if (shift && kb.rKey.wasPressedThisFrame)
                TaskManager.Instance.ResetManualPlacement();

            if (shift && kb.mKey.wasPressedThisFrame && interaction != null && interaction.Hovered != null)
            {
                var task = interaction.Hovered.Task;
                if (!task.DueDate.HasValue) task.DueDate = System.DateTime.Today.AddDays(1).AddHours(23).AddMinutes(59);
                task.Reminder = System.DateTime.Now.AddSeconds(10);
                task.reminderFired = false;
                TaskManager.Instance.NotifyTaskChanged(task);
                interaction.Hovered.Flash();
                Debug.Log($"[Cypher] Test reminder for \"{task.title}\" in 10 seconds.");
            }

            if (shift && kb.iKey.wasPressedThisFrame && interaction != null && interaction.Hovered != null)
            {
                var task = interaction.Hovered.Task;
                task.source = (TaskSource)(((int)task.source + 1) % 3);
                TaskManager.Instance.NotifyTaskChanged(task, relayout: false);
            }
        }
    }
}
