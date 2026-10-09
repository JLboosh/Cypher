using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Cypher
{
    /// <summary>
    /// Loads and saves tasks as JSON in Application.persistentDataPath
    /// (on macOS: ~/Library/Application Support/&lt;Company&gt;/&lt;Product&gt;/tasks.json).
    /// Writes go to a temp file first and the previous save is kept as tasks.json.bak,
    /// so a crash mid-write can never wipe your list.
    /// </summary>
    public class TaskRepository
    {
        const string FileName = "tasks.json";

        public string FilePath => Path.Combine(Application.persistentDataPath, FileName);
        string BackupPath => FilePath + ".bak";
        string TempPath => FilePath + ".tmp";

        public TaskDatabase Data { get; private set; } = new TaskDatabase();
        public IReadOnlyList<TaskItem> Tasks => Data.tasks;

        public event Action Changed;

        public void Load()
        {
            Data = TryRead(FilePath) ?? TryRead(BackupPath) ?? new TaskDatabase();
            Data.tasks ??= new List<TaskItem>();
            Debug.Log($"[Cypher] Loaded {Data.tasks.Count} task(s) from {FilePath}");
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(TempPath, JsonUtility.ToJson(Data, true));
                if (File.Exists(FilePath))
                {
                    File.Copy(FilePath, BackupPath, true);
                    File.Delete(FilePath);
                }
                File.Move(TempPath, FilePath);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Cypher] Could not save tasks: {e.Message}");
            }
        }

        public TaskItem Get(string id) => Data.tasks.Find(t => t.id == id);

        public void Add(TaskItem task)
        {
            Data.tasks.Add(task);
            Changed?.Invoke();
        }

        public bool Remove(string id)
        {
            bool removed = Data.tasks.RemoveAll(t => t.id == id) > 0;
            if (removed) Changed?.Invoke();
            return removed;
        }

        public void NotifyChanged() => Changed?.Invoke();

        TaskDatabase TryRead(string path)
        {
            if (!File.Exists(path)) return null;
            try
            {
                return JsonUtility.FromJson<TaskDatabase>(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                // Keep the broken file for inspection instead of silently overwriting it.
                string corrupt = path + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                File.Copy(path, corrupt, true);
                Debug.LogError($"[Cypher] {path} is unreadable ({e.Message}). Copied to {corrupt}.");
                return null;
            }
        }
    }
}
