using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Barcade.Core.Content
{
    /// <summary>
    /// Minimal hand-rolled JSON reader/writer, scoped to exactly the value shapes
    /// GDD §11.1 needs: object, array, string, number (double), bool, null.
    ///
    /// Why hand-rolled instead of System.Text.Json / Newtonsoft.Json: this file
    /// lives in Barcade.Core and must compile and run identically inside the Unity
    /// player (arcade cabinet, GDD §12 remote-content loader) and the dotnet
    /// fast-test runner. System.Text.Json is not guaranteed present in Unity's
    /// IL2CPP/Mono reference-assembly subset, and Newtonsoft.Json is not an
    /// installed package (see Barcade/Packages/manifest.json). Per the project's
    /// minimalism ladder ("no dependency that isn't sanctioned"), a small custom
    /// codec scoped to this one schema is the defensible choice over adding a
    /// package the whole project would carry just for this.
    ///
    /// Not a general-purpose library -- do not extend beyond this schema's needs.
    /// </summary>
    internal static class MiniJson
    {
        // ── Public API ────────────────────────────────────────────────────────────

        public static string Write(object value)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value);
            return sb.ToString();
        }

        public static object Parse(string json)
        {
            int i = 0;
            SkipWhitespace(json, ref i);
            object value = ParseValue(json, ref i);
            return value;
        }

        // ── Writer ────────────────────────────────────────────────────────────────

        private static void WriteValue(StringBuilder sb, object value)
        {
            switch (value)
            {
                case null:
                    sb.Append("null");
                    break;
                case string s:
                    WriteString(sb, s);
                    break;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    break;
                case double d:
                    sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case float f:
                    sb.Append(((double)f).ToString("R", CultureInfo.InvariantCulture));
                    break;
                case int n:
                    sb.Append(n.ToString(CultureInfo.InvariantCulture));
                    break;
                case IDictionary<string, object> obj:
                    WriteObject(sb, obj);
                    break;
                case IEnumerable<object> arr:
                    WriteArray(sb, arr);
                    break;
                default:
                    throw new ArgumentException($"MiniJson cannot write value of type {value.GetType()}");
            }
        }

        private static void WriteObject(StringBuilder sb, IDictionary<string, object> obj)
        {
            sb.Append('{');
            bool first = true;
            foreach (KeyValuePair<string, object> kv in obj)
            {
                if (!first) sb.Append(',');
                first = false;
                WriteString(sb, kv.Key);
                sb.Append(':');
                WriteValue(sb, kv.Value);
            }
            sb.Append('}');
        }

        private static void WriteArray(StringBuilder sb, IEnumerable<object> arr)
        {
            sb.Append('[');
            bool first = true;
            foreach (object item in arr)
            {
                if (!first) sb.Append(',');
                first = false;
                WriteValue(sb, item);
            }
            sb.Append(']');
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ')
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ── Parser ────────────────────────────────────────────────────────────────

        private static object ParseValue(string s, ref int i)
        {
            SkipWhitespace(s, ref i);
            if (i >= s.Length) throw new FormatException("Unexpected end of JSON input.");

            char c = s[i];
            switch (c)
            {
                case '{': return ParseObject(s, ref i);
                case '[': return ParseArray(s, ref i);
                case '"': return ParseString(s, ref i);
                case 't':
                    Expect(s, ref i, "true");
                    return true;
                case 'f':
                    Expect(s, ref i, "false");
                    return false;
                case 'n':
                    Expect(s, ref i, "null");
                    return null;
                default:
                    return ParseNumber(s, ref i);
            }
        }

        private static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            i++; // consume '{'
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return result; }

            while (true)
            {
                SkipWhitespace(s, ref i);
                string key = ParseString(s, ref i);
                SkipWhitespace(s, ref i);
                if (s[i] != ':') throw new FormatException($"Expected ':' at position {i}.");
                i++;
                object value = ParseValue(s, ref i);
                result[key] = value;

                SkipWhitespace(s, ref i);
                if (i >= s.Length) throw new FormatException("Unterminated JSON object.");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; break; }
                throw new FormatException($"Expected ',' or '}}' at position {i}.");
            }

            return result;
        }

        private static List<object> ParseArray(string s, ref int i)
        {
            var result = new List<object>();
            i++; // consume '['
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return result; }

            while (true)
            {
                object value = ParseValue(s, ref i);
                result.Add(value);

                SkipWhitespace(s, ref i);
                if (i >= s.Length) throw new FormatException("Unterminated JSON array.");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; break; }
                throw new FormatException($"Expected ',' or ']' at position {i}.");
            }

            return result;
        }

        private static string ParseString(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException($"Expected '\"' at position {i}.");
            i++;
            var sb = new StringBuilder();
            while (s[i] != '"')
            {
                char c = s[i];
                if (c == '\\')
                {
                    i++;
                    char esc = s[i];
                    switch (esc)
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
                            string hex = s.Substring(i + 1, 4);
                            sb.Append((char)Convert.ToInt32(hex, 16));
                            i += 4;
                            break;
                        default:
                            throw new FormatException($"Unknown escape sequence '\\{esc}' at position {i}.");
                    }
                    i++;
                }
                else
                {
                    sb.Append(c);
                    i++;
                }
            }
            i++; // consume closing quote
            return sb.ToString();
        }

        private static object ParseNumber(string s, ref int i)
        {
            int start = i;
            if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == 'e' || s[i] == 'E' || s[i] == '+' || s[i] == '-'))
                i++;

            string token = s.Substring(start, i - start);
            if (token.Length == 0) throw new FormatException($"Expected a value at position {start}.");
            return double.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private static void Expect(string s, ref int i, string literal)
        {
            if (i + literal.Length > s.Length || s.Substring(i, literal.Length) != literal)
                throw new FormatException($"Expected '{literal}' at position {i}.");
            i += literal.Length;
        }

        private static void SkipWhitespace(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }
    }
}
