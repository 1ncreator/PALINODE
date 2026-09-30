using System;
using System.Collections.Generic;
using Palinode.Core;

namespace Palinode.Localization
{
    /// <summary>
    /// Key → (EN, RU) table loaded from Data/localization.csv (columns: key,en,ru[,note]).
    /// Lines starting with '#' are comments.
    /// </summary>
    public sealed class LocalizationTable
    {
        private readonly Dictionary<string, string[]> _entries = new Dictionary<string, string[]>(StringComparer.Ordinal);

        public IEnumerable<string> Keys => _entries.Keys;

        public static LocalizationTable FromCsv(string csv)
        {
            var table = new LocalizationTable();
            var rows = Csv.Parse(csv ?? string.Empty);
            for (int r = 0; r < rows.Count; r++)
            {
                var row = rows[r];
                if (row.Count == 0) continue;
                string key = row[0].Trim();
                if (key.Length == 0 || key.StartsWith("#") || (r == 0 && key == "key")) continue;
                string en = row.Count > 1 ? row[1] : string.Empty;
                string ru = row.Count > 2 ? row[2] : string.Empty;
                table._entries[key] = new[] { Unescape(en), Unescape(ru) };
            }
            return table;
        }

        private static string Unescape(string s) => s.Replace("\\n", "\n");

        public bool Contains(string key) => key != null && _entries.ContainsKey(key);

        public string Get(string key, Language lang)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            if (_entries.TryGetValue(key, out var v))
            {
                string s = v[(int)lang];
                if (string.IsNullOrEmpty(s)) s = v[0];
                return s;
            }
            return key; // show the key itself so missing strings are visible but never crash
        }

        public bool HasBoth(string key)
        {
            return _entries.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v[0]) && !string.IsNullOrEmpty(v[1]);
        }
    }
}
