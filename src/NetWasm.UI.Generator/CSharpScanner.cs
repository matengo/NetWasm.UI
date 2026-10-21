namespace NetWasm.UI.Generator;

/// <summary>
/// Just enough C# lexing to find where an embedded expression ends: balanced brackets while
/// skipping string/char literals (regular, verbatim, interpolated, raw) and comments.
/// </summary>
internal static class CSharpScanner
{
    public static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_';

    public static bool IsIdentPart(char c) => char.IsLetterOrDigit(c) || c == '_';

    public static int ScanIdent(string s, int i)
    {
        while (i < s.Length && IsIdentPart(s[i]))
        {
            i++;
        }

        return i;
    }

    /// <summary><paramref name="pos"/> is at '(', '[' or '{'. Returns the index after the matching close, or -1.</summary>
    public static int SkipBalanced(string s, int pos)
    {
        var stack = new System.Collections.Generic.Stack<char>();
        var i = pos;
        while (i < s.Length)
        {
            var skipped = SkipTrivia(s, i);
            if (skipped != i)
            {
                if (skipped < 0)
                {
                    return -1;
                }

                i = skipped;
                continue;
            }

            var c = s[i];
            switch (c)
            {
                case '(':
                    stack.Push(')');
                    break;
                case '[':
                    stack.Push(']');
                    break;
                case '{':
                    stack.Push('}');
                    break;
                case ')':
                case ']':
                case '}':
                    if (stack.Count == 0 || stack.Pop() != c)
                    {
                        return -1;
                    }

                    if (stack.Count == 0)
                    {
                        return i + 1;
                    }

                    break;
            }

            i++;
        }

        return -1;
    }

    /// <summary>
    /// Scans C# until <paramref name="terminator"/> appears at bracket depth 0 (outside literals).
    /// Returns the terminator's index, or -1.
    /// </summary>
    public static int ScanUntil(string s, int pos, char terminator)
    {
        var i = pos;
        while (i < s.Length)
        {
            var c = s[i];
            if (c == terminator)
            {
                return i;
            }

            if (c is '(' or '[' or '{')
            {
                i = SkipBalanced(s, i);
                if (i < 0)
                {
                    return -1;
                }

                continue;
            }

            // At depth 0 the terminator (checked above) wins over a string start, so attribute quotes work.
            var skipped = SkipTrivia(s, i);
            if (skipped < 0)
            {
                return -1;
            }

            i = skipped == i ? i + 1 : skipped;
        }

        return -1;
    }

    /// <summary>
    /// Razor implicit expression: <c>ident(.ident | ?.ident | (...) | [...])*</c>.
    /// <paramref name="i"/> is at the identifier start. Returns the end index, or -1 on unbalanced brackets.
    /// </summary>
    public static int ScanImplicit(string s, int i)
    {
        i = ScanIdent(s, i);
        while (i < s.Length)
        {
            var c = s[i];
            if (c is '(' or '[')
            {
                i = SkipBalanced(s, i);
                if (i < 0)
                {
                    return -1;
                }
            }
            else if (c == '.' && i + 1 < s.Length && IsIdentStart(s[i + 1]))
            {
                i = ScanIdent(s, i + 1);
            }
            else if (c == '?' && i + 2 < s.Length && s[i + 1] == '.' && IsIdentStart(s[i + 2]))
            {
                i = ScanIdent(s, i + 2);
            }
            else if (c == '!' && i + 2 < s.Length && s[i + 1] == '.' && IsIdentStart(s[i + 2]))
            {
                i = ScanIdent(s, i + 2);
            }
            else
            {
                break;
            }
        }

        return i;
    }

    /// <summary>If a literal or comment starts at <paramref name="i"/>, returns the index after it (-1 if unterminated); otherwise <paramref name="i"/>.</summary>
    private static int SkipTrivia(string s, int i)
    {
        var c = s[i];
        if (c == '/' && i + 1 < s.Length)
        {
            if (s[i + 1] == '/')
            {
                var nl = s.IndexOf('\n', i);
                return nl < 0 ? s.Length : nl;
            }

            if (s[i + 1] == '*')
            {
                var end = s.IndexOf("*/", i + 2, System.StringComparison.Ordinal);
                return end < 0 ? -1 : end + 2;
            }
        }

        if (c == '\'')
        {
            var j = i + 1;
            while (j < s.Length && s[j] != '\'')
            {
                j += s[j] == '\\' ? 2 : 1;
            }

            return j < s.Length ? j + 1 : -1;
        }

        // String prefixes: " @" $" $@" @$" $$""" …
        var k = i;
        var dollars = 0;
        var verbatim = false;
        while (k < s.Length && (s[k] == '$' || s[k] == '@'))
        {
            if (s[k] == '$')
            {
                dollars++;
            }
            else
            {
                verbatim = true;
            }

            k++;
        }

        if (k >= s.Length || s[k] != '"' || (k > i && i > 0 && IsIdentPart(s[i - 1])))
        {
            return i;
        }

        var quotes = 0;
        while (k + quotes < s.Length && s[k + quotes] == '"')
        {
            quotes++;
        }

        if (quotes >= 3)
        {
            var close = new string('"', quotes);
            var end = s.IndexOf(close, k + quotes, System.StringComparison.Ordinal);
            return end < 0 ? -1 : end + quotes;
        }

        var p = k + 1;
        while (p < s.Length)
        {
            var ch = s[p];
            if (!verbatim && ch == '\\')
            {
                p += 2;
                continue;
            }

            if (ch == '"')
            {
                if (verbatim && p + 1 < s.Length && s[p + 1] == '"')
                {
                    p += 2;
                    continue;
                }

                return p + 1;
            }

            if (dollars > 0 && ch == '{')
            {
                if (p + 1 < s.Length && s[p + 1] == '{')
                {
                    p += 2;
                    continue;
                }

                p = SkipBalanced(s, p);
                if (p < 0)
                {
                    return -1;
                }

                continue;
            }

            if (!verbatim && ch == '\n')
            {
                return -1;
            }

            p++;
        }

        return -1;
    }
}
