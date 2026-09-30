using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Palinode.Core
{
    /// <summary>
    /// Minimal JSON reader producing Dictionary&lt;string, object&gt; / List&lt;object&gt; / string / double / bool / null.
    /// Used for the data-driven prologue description, where JsonUtility is too rigid (polymorphic steps).
    /// </summary>
    public static class Json
    {
        public static object Parse(string text)
        {
            var parser = new Parser(text);
            parser.SkipWhitespace();
            object value = parser.ParseValue();
            parser.SkipWhitespace();
            if (!parser.AtEnd)
                throw parser.Error("Unexpected trailing characters");
            return value;
        }

        private sealed class Parser
        {
            private readonly string _s;
            private int _i;

            public Parser(string s) { _s = s ?? string.Empty; }

            public bool AtEnd => _i >= _s.Length;

            public FormatException Error(string message)
            {
                int line = 1, col = 1;
                for (int k = 0; k < _i && k < _s.Length; k++)
                {
                    if (_s[k] == '\n') { line++; col = 1; } else col++;
                }
                return new FormatException($"JSON: {message} at line {line}, col {col}");
            }

            public void SkipWhitespace()
            {
                while (_i < _s.Length)
                {
                    char c = _s[_i];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '﻿') { _i++; continue; }
                    // Allow // line comments to keep data files human-friendly.
                    if (c == '/' && _i + 1 < _s.Length && _s[_i + 1] == '/')
                    {
                        while (_i < _s.Length && _s[_i] != '\n') _i++;
                        continue;
                    }
                    break;
                }
            }

            public object ParseValue()
            {
                if (AtEnd) throw Error("Unexpected end");
                char c = _s[_i];
                switch (c)
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '"': return ParseString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber();
                        throw Error($"Unexpected character '{c}'");
                }
            }

            private void Expect(string word)
            {
                if (string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0) throw Error($"Expected '{word}'");
                _i += word.Length;
            }

            private Dictionary<string, object> ParseObject()
            {
                var dict = new Dictionary<string, object>(StringComparer.Ordinal);
                _i++; // {
                SkipWhitespace();
                if (!AtEnd && _s[_i] == '}') { _i++; return dict; }
                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || _s[_i] != '"') throw Error("Expected property name");
                    string key = ParseString();
                    SkipWhitespace();
                    if (AtEnd || _s[_i] != ':') throw Error("Expected ':'");
                    _i++;
                    SkipWhitespace();
                    dict[key] = ParseValue();
                    SkipWhitespace();
                    if (AtEnd) throw Error("Unterminated object");
                    if (_s[_i] == ',')
                    {
                        _i++;
                        SkipWhitespace();
                        if (!AtEnd && _s[_i] == '}') { _i++; return dict; } // trailing comma tolerated
                        continue;
                    }
                    if (_s[_i] == '}') { _i++; return dict; }
                    throw Error("Expected ',' or '}'");
                }
            }

            private List<object> ParseArray()
            {
                var list = new List<object>();
                _i++; // [
                SkipWhitespace();
                if (!AtEnd && _s[_i] == ']') { _i++; return list; }
                while (true)
                {
                    SkipWhitespace();
                    list.Add(ParseValue());
                    SkipWhitespace();
                    if (AtEnd) throw Error("Unterminated array");
                    if (_s[_i] == ',')
                    {
                        _i++;
                        SkipWhitespace();
                        if (!AtEnd && _s[_i] == ']') { _i++; return list; }
                        continue;
                    }
                    if (_s[_i] == ']') { _i++; return list; }
                    throw Error("Expected ',' or ']'");
                }
            }

            private string ParseString()
            {
                var sb = new StringBuilder();
                _i++; // opening quote
                while (true)
                {
                    if (AtEnd) throw Error("Unterminated string");
                    char c = _s[_i++];
                    if (c == '"') break;
                    if (c != '\\') { sb.Append(c); continue; }
                    if (AtEnd) throw Error("Bad escape");
                    char e = _s[_i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_i + 4 > _s.Length) throw Error("Bad unicode escape");
                            sb.Append((char)Convert.ToInt32(_s.Substring(_i, 4), 16));
                            _i += 4;
                            break;
                        default: throw Error($"Bad escape '\\{e}'");
                    }
                }
                return sb.ToString();
            }

            private double ParseNumber()
            {
                int start = _i;
                while (_i < _s.Length && "+-0123456789.eE".IndexOf(_s[_i]) >= 0) _i++;
                string token = _s.Substring(start, _i - start);
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                    throw Error($"Bad number '{token}'");
                return d;
            }
        }
    }
}
