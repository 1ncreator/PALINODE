using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Palinode.Core
{
    /// <summary>Convenience wrapper over parsed JSON values with typed, defaulted accessors.</summary>
    public readonly struct JNode
    {
        public readonly object Raw;

        public JNode(object raw) { Raw = raw; }

        public static JNode Parse(string text) => new JNode(Json.Parse(text));

        public bool IsNull => Raw == null;
        public bool IsObject => Raw is Dictionary<string, object>;
        public bool IsArray => Raw is List<object>;
        public bool IsString => Raw is string;
        public bool IsNumber => Raw is double;

        public Dictionary<string, object> Object => Raw as Dictionary<string, object>;
        public List<object> Array => Raw as List<object>;

        public bool Has(string key) => Object != null && Object.ContainsKey(key) && Object[key] != null;

        public JNode this[string key]
        {
            get
            {
                var o = Object;
                if (o != null && o.TryGetValue(key, out var v)) return new JNode(v);
                return default;
            }
        }

        public JNode this[int index]
        {
            get
            {
                var a = Array;
                if (a != null && index >= 0 && index < a.Count) return new JNode(a[index]);
                return default;
            }
        }

        public int Count => Array?.Count ?? Object?.Count ?? 0;

        public IEnumerable<JNode> Items()
        {
            var a = Array;
            if (a == null) yield break;
            foreach (var v in a) yield return new JNode(v);
        }

        public IEnumerable<KeyValuePair<string, JNode>> Pairs()
        {
            var o = Object;
            if (o == null) yield break;
            foreach (var kv in o) yield return new KeyValuePair<string, JNode>(kv.Key, new JNode(kv.Value));
        }

        public string AsString(string fallback = null)
        {
            if (Raw is string s) return s;
            if (Raw is double d) return d.ToString(CultureInfo.InvariantCulture);
            if (Raw is bool b) return b ? "true" : "false";
            return fallback;
        }

        public float AsFloat(float fallback = 0f)
        {
            if (Raw is double d) return (float)d;
            if (Raw is string s && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) return f;
            if (Raw is bool b) return b ? 1f : 0f;
            return fallback;
        }

        public int AsInt(int fallback = 0) => Raw is double d ? (int)Math.Round(d) : fallback;

        public bool AsBool(bool fallback = false)
        {
            if (Raw is bool b) return b;
            if (Raw is double d) return Math.Abs(d) > 1e-6;
            if (Raw is string s) return s == "true" || s == "1";
            return fallback;
        }

        public string Str(string key, string fallback = null) => this[key].AsString(fallback);
        public float Num(string key, float fallback = 0f) => this[key].AsFloat(fallback);
        public int Int(string key, int fallback = 0) => this[key].AsInt(fallback);
        public bool Bool(string key, bool fallback = false) => this[key].AsBool(fallback);

        public Vector2 Vec2(string key, Vector2 fallback)
        {
            var n = this[key];
            if (n.IsArray && n.Count >= 2) return new Vector2(n[0].AsFloat(), n[1].AsFloat());
            if (n.IsNumber) return new Vector2(n.AsFloat(), n.AsFloat());
            return fallback;
        }

        public Vector4 Vec4(string key, Vector4 fallback)
        {
            var n = this[key];
            if (n.IsArray && n.Count >= 4) return new Vector4(n[0].AsFloat(), n[1].AsFloat(), n[2].AsFloat(), n[3].AsFloat());
            return fallback;
        }

        public Color Color(string key, Color fallback)
        {
            string s = Str(key);
            if (string.IsNullOrEmpty(s)) return fallback;
            if (s == "black") return UnityEngine.Color.black;
            if (s == "white") return UnityEngine.Color.white;
            if (s == "clear") return UnityEngine.Color.clear;
            return ColorUtility.TryParseHtmlString(s, out var c) ? c : fallback;
        }

        public override string ToString() => Raw?.ToString() ?? "null";
    }
}
