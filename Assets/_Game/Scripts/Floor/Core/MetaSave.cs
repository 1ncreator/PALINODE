using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Palinode.Floor
{
    /// <summary>How the last run ended (drives the traces of past revisions).</summary>
    [Serializable]
    public sealed class DeathRecord
    {
        public int floor;              // 0 = none
        public string killer;          // enemy type id ("letters", "bug", …, "boss")
        public int page;               // room number by order of visit
        public List<string> items = new List<string>();
    }

    /// <summary>
    /// Persistent meta state (JSON in Application.persistentDataPath): the revision number (01 after the prologue,
    /// +1 on every death, forever), the hidden count of red-pencil edits, the last death, original pages found.
    /// </summary>
    [Serializable]
    public sealed class MetaSave
    {
        public const string FileName = "palinode_meta.json";

        /// <summary>Tests redirect the file here.</summary>
        public static string PathOverride;

        public int revision = 1;
        public int edits;              // red-pencil activations, all time (never shown)
        public int runs;
        public DeathRecord lastDeath = new DeathRecord();
        public List<int> pages = new List<int>();

        public static string FilePath => !string.IsNullOrEmpty(PathOverride)
            ? PathOverride
            : Path.Combine(Application.persistentDataPath, FileName);

        public static MetaSave Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var m = JsonUtility.FromJson<MetaSave>(File.ReadAllText(FilePath));
                    if (m != null)
                    {
                        if (m.revision < 1) m.revision = 1;
                        m.lastDeath ??= new DeathRecord();
                        m.pages ??= new List<int>();
                        return m;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[PALINODE] Meta save unreadable, starting over: " + e.Message);
            }
            return new MetaSave();
        }

        public void Save()
        {
            try
            {
                string dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(FilePath, JsonUtility.ToJson(this, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[PALINODE] Could not write the meta save: " + e.Message);
            }
        }

        /// <summary>Records a death and moves to the next revision (saved immediately).</summary>
        public void RegisterDeath(int floor, string killer, int page, IEnumerable<string> items)
        {
            lastDeath = new DeathRecord { floor = floor, killer = killer, page = page, items = new List<string>(items ?? new string[0]) };
            revision++;
            Save();
        }

        public void RegisterEdit()
        {
            edits++;
            Save();
        }

        public void AddPage(int index)
        {
            if (!pages.Contains(index)) pages.Add(index);
            Save();
        }

        public string RevisionLabel => revision.ToString("00");
    }
}
