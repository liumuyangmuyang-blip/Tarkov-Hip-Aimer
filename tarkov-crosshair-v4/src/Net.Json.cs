// v4.4 (W9) NET: a tiny JSON reader for the GitHub releases API and the usage worker's /stats reply.
// Pure (no WinForms, no network). Objects -> Dictionary<string, object>, arrays -> List<object>, numbers -> double,
// strings -> string, true/false -> bool, null -> null. Throws FormatException on bad input; callers catch.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TarkovCrosshair
{
    internal static class MiniJson
    {
        /// <summary>Hard cap on nesting so a hostile reply cannot blow the stack.</summary>
        public const int MaxDepth = 64;

        public static object Parse(string s)
        {
            if (s == null) throw new FormatException("null json");
            int i = 0;
            object v = Value(s, ref i, 0);
            Ws(s, ref i);
            if (i != s.Length) throw new FormatException("trailing data at " + i);
            return v;
        }

        /// <summary>Parse or null (never throws).</summary>
        public static object TryParse(string s)
        {
            try { return Parse(s); } catch (Exception) { return null; }
        }

        public static Dictionary<string, object> Obj(object o) { return o as Dictionary<string, object>; }
        public static List<object> Arr(object o) { return o as List<object>; }

        public static object Get(object o, string key)
        {
            Dictionary<string, object> d = o as Dictionary<string, object>;
            object v;
            return d != null && d.TryGetValue(key, out v) ? v : null;
        }

        public static string Str(object o, string key) { return Get(o, key) as string; }

        public static long Num(object o, string key, long fallback)
        {
            object v = Get(o, key);
            if (v is double) { double d = (double)v; if (d >= long.MinValue && d <= long.MaxValue && !double.IsNaN(d)) return (long)d; }
            return fallback;
        }

        public static bool Bool(object o, string key, bool fallback)
        {
            object v = Get(o, key);
            return v is bool ? (bool)v : fallback;
        }

        /// <summary>JSON string literal (with quotes) for writing small payloads.</summary>
        public static string Quote(string s)
        {
            StringBuilder b = new StringBuilder((s ?? "").Length + 2);
            b.Append('"');
            foreach (char c in s ?? "")
            {
                switch (c)
                {
                    case '"': b.Append("\\\""); break;
                    case '\\': b.Append("\\\\"); break;
                    case '\n': b.Append("\\n"); break;
                    case '\r': b.Append("\\r"); break;
                    case '\t': b.Append("\\t"); break;
                    default:
                        if (c < 0x20) b.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else b.Append(c);
                        break;
                }
            }
            b.Append('"');
            return b.ToString();
        }

        private static void Ws(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++;
        }

        private static object Value(string s, ref int i, int depth)
        {
            if (depth > MaxDepth) throw new FormatException("too deep");
            Ws(s, ref i);
            if (i >= s.Length) throw new FormatException("unexpected end");
            char c = s[i];
            if (c == '{')
            {
                Dictionary<string, object> d = new Dictionary<string, object>(StringComparer.Ordinal);
                i++; Ws(s, ref i);
                if (i < s.Length && s[i] == '}') { i++; return d; }
                while (true)
                {
                    Ws(s, ref i);
                    if (i >= s.Length || s[i] != '"') throw new FormatException("key expected at " + i);
                    string k = Str(s, ref i);
                    Ws(s, ref i);
                    if (i >= s.Length || s[i] != ':') throw new FormatException(": expected at " + i);
                    i++;
                    d[k] = Value(s, ref i, depth + 1);
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == '}') { i++; return d; }
                    throw new FormatException(", or } expected at " + i);
                }
            }
            if (c == '[')
            {
                List<object> a = new List<object>();
                i++; Ws(s, ref i);
                if (i < s.Length && s[i] == ']') { i++; return a; }
                while (true)
                {
                    a.Add(Value(s, ref i, depth + 1));
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == ']') { i++; return a; }
                    throw new FormatException(", or ] expected at " + i);
                }
            }
            if (c == '"') return Str(s, ref i);
            if (Lit(s, ref i, "true")) return true;
            if (Lit(s, ref i, "false")) return false;
            if (Lit(s, ref i, "null")) return null;
            int st = i;
            if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
            while (i < s.Length && ((s[i] >= '0' && s[i] <= '9') || s[i] == '.' || s[i] == 'e' || s[i] == 'E' || s[i] == '-' || s[i] == '+')) i++;
            double num;
            if (i == st || !double.TryParse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture, out num))
                throw new FormatException("bad value at " + st);
            return num;
        }

        private static bool Lit(string s, ref int i, string w)
        {
            if (string.CompareOrdinal(s, i, w, 0, w.Length) == 0) { i += w.Length; return true; }
            return false;
        }

        private static string Str(string s, ref int i)
        {
            i++; // opening quote
            StringBuilder b = new StringBuilder();
            while (true)
            {
                if (i >= s.Length) throw new FormatException("unterminated string");
                char c = s[i++];
                if (c == '"') return b.ToString();
                if (c != '\\') { b.Append(c); continue; }
                if (i >= s.Length) throw new FormatException("bad escape");
                char e = s[i++];
                switch (e)
                {
                    case '"': b.Append('"'); break;
                    case '\\': b.Append('\\'); break;
                    case '/': b.Append('/'); break;
                    case 'b': b.Append('\b'); break;
                    case 'f': b.Append('\f'); break;
                    case 'n': b.Append('\n'); break;
                    case 'r': b.Append('\r'); break;
                    case 't': b.Append('\t'); break;
                    case 'u':
                        int code;
                        if (i + 4 > s.Length || !int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                            throw new FormatException("bad \\u");
                        b.Append((char)code); i += 4; break;
                    default: throw new FormatException("bad escape \\" + e);
                }
            }
        }
    }
}
