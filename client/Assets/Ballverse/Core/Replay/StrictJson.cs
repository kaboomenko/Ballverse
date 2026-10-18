namespace Ballverse.Core.Replay
{
    /// <summary>
    /// Strict RFC 8259 JSON syntax check for the JSON texts stored in a replay (header paramsJson, PARM values; spec §7.4).
    /// Unlike Newtonsoft's lenient reader it rejects comments, single quotes, unquoted property names, trailing commas,
    /// <c>NaN</c>/<c>Infinity</c>/<c>undefined</c>, leading zeros and control characters in strings. Nesting is limited to
    /// <see cref="MaxDepth"/> (Newtonsoft's default), so anything accepted here also loads through <see cref="CoreJson"/>.
    /// In compact mode whitespace outside strings is rejected too (PARM "value as compact JSON string").
    /// Recursion depth is bounded by <see cref="MaxDepth"/>, so hostile input cannot overflow the stack.
    /// </summary>
    internal static class StrictJson
    {
        /// <summary>Maximum nesting of objects/arrays.</summary>
        internal const int MaxDepth = 64;

        /// <summary>Returns null when <paramref name="json"/> is one valid JSON value (and compact if requested), else an error message.</summary>
        internal static string Validate(string json, bool compact)
        {
            if (json == null) return "JSON text is null";
            return new Parser(json, compact).Run();
        }

        sealed class Parser
        {
            readonly string _s;
            readonly bool _compact;
            int _pos;
            string _error;

            internal Parser(string s, bool compact)
            {
                _s = s;
                _compact = compact;
            }

            internal string Run()
            {
                if (SkipWhitespace() && Value(0) && SkipWhitespace() && _pos != _s.Length)
                    Fail("Unexpected content after the JSON value");
                return _error;
            }

            int Peek()
            {
                return _pos < _s.Length ? _s[_pos] : -1;
            }

            bool Fail(string message)
            {
                if (_error == null) _error = message + " at position " + _pos;
                return false;
            }

            bool SkipWhitespace()
            {
                while (_pos < _s.Length)
                {
                    char c = _s[_pos];
                    if (c != ' ' && c != '\t' && c != '\n' && c != '\r') return true;
                    if (_compact) return Fail("Whitespace is not allowed in compact JSON");
                    _pos++;
                }
                return true;
            }

            bool Value(int depth)
            {
                int c = Peek();
                switch (c)
                {
                    case -1:
                        return Fail("Unexpected end of JSON");
                    case '{':
                        return Container(depth, '}', true);
                    case '[':
                        return Container(depth, ']', false);
                    case '"':
                        return StringToken();
                    case 't':
                        return Literal("true");
                    case 'f':
                        return Literal("false");
                    case 'n':
                        return Literal("null");
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return Number();
                        return Fail("Unexpected character '" + (char)c + "'");
                }
            }

            bool Container(int depth, char close, bool isObject)
            {
                if (depth >= MaxDepth) return Fail("JSON nesting deeper than " + MaxDepth);
                _pos++;
                if (!SkipWhitespace()) return false;
                if (Peek() == close)
                {
                    _pos++;
                    return true;
                }
                while (true)
                {
                    if (isObject)
                    {
                        if (Peek() != '"') return Fail("Expected a double-quoted property name");
                        if (!StringToken() || !SkipWhitespace()) return false;
                        if (Peek() != ':') return Fail("Expected ':'");
                        _pos++;
                        if (!SkipWhitespace()) return false;
                    }
                    if (!Value(depth + 1) || !SkipWhitespace()) return false;
                    int c = Peek();
                    if (c == close)
                    {
                        _pos++;
                        return true;
                    }
                    if (c != ',') return Fail("Expected ',' or '" + close + "'");
                    _pos++;
                    if (!SkipWhitespace()) return false;
                }
            }

            bool StringToken()
            {
                _pos++;
                while (true)
                {
                    if (_pos >= _s.Length) return Fail("Unterminated string");
                    char c = _s[_pos++];
                    if (c == '"') return true;
                    if (c < 0x20) return Fail("Control character in string");
                    if (c != '\\') continue;
                    if (_pos >= _s.Length) return Fail("Unterminated escape");
                    char e = _s[_pos++];
                    if (e == '"' || e == '\\' || e == '/' || e == 'b' || e == 'f' || e == 'n' || e == 'r' || e == 't') continue;
                    if (e != 'u') return Fail("Invalid escape '\\" + e + "'");
                    for (int i = 0; i < 4; i++)
                    {
                        int h = Peek();
                        bool hex = (h >= '0' && h <= '9') || (h >= 'a' && h <= 'f') || (h >= 'A' && h <= 'F');
                        if (!hex) return Fail("Invalid \\u escape");
                        _pos++;
                    }
                }
            }

            bool Number()
            {
                if (Peek() == '-') _pos++;
                int c = Peek();
                if (c == '0')
                {
                    _pos++;
                }
                else if (c >= '1' && c <= '9')
                {
                    SkipDigits();
                }
                else
                {
                    return Fail("Invalid number");
                }
                if (Peek() == '.')
                {
                    _pos++;
                    if (SkipDigits() == 0) return Fail("Digits expected after '.'");
                }
                c = Peek();
                if (c == 'e' || c == 'E')
                {
                    _pos++;
                    c = Peek();
                    if (c == '+' || c == '-') _pos++;
                    if (SkipDigits() == 0) return Fail("Digits expected in exponent");
                }
                return true;
            }

            int SkipDigits()
            {
                int start = _pos;
                while (_pos < _s.Length && _s[_pos] >= '0' && _s[_pos] <= '9') _pos++;
                return _pos - start;
            }

            bool Literal(string literal)
            {
                if (_s.Length - _pos < literal.Length || string.CompareOrdinal(_s, _pos, literal, 0, literal.Length) != 0)
                    return Fail("Invalid literal");
                _pos += literal.Length;
                return true;
            }
        }
    }
}
