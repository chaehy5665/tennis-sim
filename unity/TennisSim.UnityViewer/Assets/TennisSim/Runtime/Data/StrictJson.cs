using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TennisSim.Viewer
{
    // Small strict JSON reader shared verbatim by Unity and the .NET checks.
    // No reflection, engine DTOs, packages, or permissive missing-number defaults.
    internal sealed class StrictJson
    {
        readonly string text;
        int cursor;
        StrictJson(string text) { this.text = text; }
        public static object Parse(string text)
        {
            if (text == null) throw new FormatException("Empty JSON");
            var reader = new StrictJson(text);
            var value = reader.Value(0); reader.White();
            if (reader.cursor != text.Length) throw reader.Error("Trailing input");
            return value;
        }
        FormatException Error(string reason) => new FormatException(reason + " at JSON offset " + cursor);
        void White() { while (cursor < text.Length && " \r\n\t".IndexOf(text[cursor]) >= 0) cursor++; }
        bool Take(char c) { White(); if (cursor < text.Length && text[cursor] == c) { cursor++; return true; } return false; }
        void Expect(char c) { if (!Take(c)) throw Error("Expected " + c); }
        object Value(int depth)
        {
            if (depth > 64) throw Error("JSON nesting exceeds 64");
            White(); if (cursor == text.Length) throw Error("Unexpected end");
            char c = text[cursor];
            if (c == '{')
            {
                cursor++; var map = new Dictionary<string, object>(StringComparer.Ordinal);
                if (Take('}')) return map;
                do { White(); var key = String(); Expect(':'); if (map.ContainsKey(key)) throw Error("Duplicate key " + key); map.Add(key, Value(depth + 1)); } while (Take(','));
                Expect('}'); return map;
            }
            if (c == '[')
            {
                cursor++; var list = new List<object>();
                if (Take(']')) return list;
                do { list.Add(Value(depth + 1)); } while (Take(','));
                Expect(']'); return list;
            }
            if (c == '"') return String();
            if (c == 't') { Literal("true"); return true; }
            if (c == 'f') { Literal("false"); return false; }
            if (c == 'n') { Literal("null"); return null; }
            int start = cursor;
            if (c == '-') cursor++;
            if (cursor < text.Length && text[cursor] == '0') cursor++; else Digits();
            if (cursor < text.Length && text[cursor] == '.') { cursor++; Digits(); }
            if (cursor < text.Length && (text[cursor] == 'e' || text[cursor] == 'E'))
            { cursor++; if (cursor < text.Length && (text[cursor] == '+' || text[cursor] == '-')) cursor++; Digits(); }
            if (!double.TryParse(text.Substring(start, cursor - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || double.IsInfinity(number) || double.IsNaN(number)) throw Error("Invalid finite number");
            return number;
        }
        void Digits() { int start = cursor; while (cursor < text.Length && text[cursor] >= '0' && text[cursor] <= '9') cursor++; if (cursor == start) throw Error("Expected digit"); }
        void Literal(string s) { if (cursor + s.Length > text.Length || text.Substring(cursor, s.Length) != s) throw Error("Invalid literal"); cursor += s.Length; }
        string String()
        {
            if (cursor >= text.Length || text[cursor++] != '"') throw Error("Expected string");
            var result = new StringBuilder();
            while (cursor < text.Length)
            {
                char c = text[cursor++]; if (c == '"') return result.ToString();
                if (c < 32) throw Error("Unescaped control character");
                if (c != '\\') { result.Append(c); continue; }
                if (cursor == text.Length) throw Error("Incomplete escape");
                c = text[cursor++];
                switch (c)
                {
                    case '"': case '\\': case '/': result.Append(c); break;
                    case 'b': result.Append('\b'); break; case 'f': result.Append('\f'); break;
                    case 'n': result.Append('\n'); break; case 'r': result.Append('\r'); break; case 't': result.Append('\t'); break;
                    case 'u':
                        if (cursor + 4 > text.Length || !ushort.TryParse(text.Substring(cursor, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ushort code)) throw Error("Invalid Unicode escape");
                        result.Append((char)code); cursor += 4; break;
                    default: throw Error("Invalid escape");
                }
            }
            throw Error("Unterminated string");
        }
    }
}
