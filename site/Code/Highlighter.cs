using System.Collections.Generic;
using NetWasm.UI;
using static NetWasm.UI.Html;

namespace Site.Code;

/// <summary>
/// A tiny syntax highlighter for C#, <c>.nwui</c> and shell snippets. ASCII-only on purpose: the
/// Unicode-aware char APIs pull large tables into the Wasm (see CONTRIBUTING.md).
/// </summary>
internal static class Highlighter
{
    public static Content[] Highlight(string src, string lang)
    {
        var parts = new List<Content>();
        var markup = lang == "nwui";
        var shell = lang == "sh";
        var code = lang == "cs";
        var plainStart = 0;
        var i = 0;

        void Emit(int start, int end, string cls)
        {
            if (start > plainStart)
            {
                parts.Add(src.Substring(plainStart, start - plainStart));
            }

            parts.Add(Span(Class(cls), src.Substring(start, end - start)));
            plainStart = end;
        }

        while (i < src.Length)
        {
            var c = src[i];

            if (markup && StartsWith(src, i, "@*"))
            {
                var end = IndexOf(src, "*@", i + 2);
                end = end < 0 ? src.Length : end + 2;
                Emit(i, end, "tk-com");
                i = end;
            }
            else if (markup && StartsWith(src, i, "<!--"))
            {
                var end = IndexOf(src, "-->", i + 4);
                end = end < 0 ? src.Length : end + 3;
                Emit(i, end, "tk-com");
                i = end;
            }
            else if (code && StartsWith(src, i, "//"))
            {
                var end = LineEnd(src, i);
                Emit(i, end, "tk-com");
                i = end;
            }
            else if (shell && c == '#' && (i == 0 || src[i - 1] == '\n' || src[i - 1] == ' '))
            {
                var end = LineEnd(src, i);
                Emit(i, end, "tk-com");
                i = end;
            }
            else if (c == '"' && !shell)
            {
                var end = i + 1;
                while (end < src.Length && src[end] != '"' && src[end] != '\n')
                {
                    end += src[end] == '\\' ? 2 : 1;
                }

                end = end < src.Length ? end + 1 : src.Length;
                Emit(i, end, "tk-str");
                i = end;
            }
            else if (markup && c == '@' && i + 1 < src.Length && IsIdentStart(src[i + 1]))
            {
                var end = IdentEnd(src, i + 1);
                if (src.Substring(i + 1, end - i - 1) == "code")
                {
                    code = true;
                }

                Emit(i, end, "tk-dir");
                i = end;
            }
            else if (markup && c == '<' && i + 1 < src.Length && (IsIdentStart(src[i + 1]) || src[i + 1] == '/'))
            {
                var end = i + 1;
                if (src[end] == '/')
                {
                    end++;
                }

                while (end < src.Length && (IsIdentPart(src[end]) || src[end] == '-' || src[end] == '.'))
                {
                    end++;
                }

                var first = src[i + 1] == '/' ? i + 2 : i + 1;
                Emit(i, end, first < src.Length && src[first] >= 'A' && src[first] <= 'Z' ? "tk-cmp" : "tk-tag");
                i = end;
            }
            else if (IsIdentStart(c))
            {
                var end = IdentEnd(src, i);
                if (code)
                {
                    var word = src.Substring(i, end - i);
                    if (IsKeyword(word))
                    {
                        Emit(i, end, "tk-kw");
                    }
                    else if (c >= 'A' && c <= 'Z')
                    {
                        Emit(i, end, "tk-ty");
                    }
                }
                else if (shell && (i == 0 || src[i - 1] == '\n') && src.Substring(i, end - i) == "dotnet")
                {
                    Emit(i, end, "tk-kw");
                }

                i = end;
            }
            else if (code && c >= '0' && c <= '9')
            {
                var end = i + 1;
                while (end < src.Length && ((src[end] >= '0' && src[end] <= '9') || src[end] == '.' || src[end] == '_'))
                {
                    end++;
                }

                Emit(i, end, "tk-num");
                i = end;
            }
            else
            {
                i++;
            }
        }

        if (plainStart < src.Length)
        {
            parts.Add(src.Substring(plainStart));
        }

        return parts.ToArray();
    }

    private static bool IsIdentStart(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_';

    private static bool IsIdentPart(char c) => IsIdentStart(c) || (c >= '0' && c <= '9');

    private static int IdentEnd(string s, int i)
    {
        while (i < s.Length && IsIdentPart(s[i]))
        {
            i++;
        }

        return i;
    }

    private static int LineEnd(string s, int i)
    {
        while (i < s.Length && s[i] != '\n')
        {
            i++;
        }

        return i;
    }

    private static bool StartsWith(string s, int i, string value)
    {
        if (i + value.Length > s.Length)
        {
            return false;
        }

        for (var k = 0; k < value.Length; k++)
        {
            if (s[i + k] != value[k])
            {
                return false;
            }
        }

        return true;
    }

    private static int IndexOf(string s, string value, int from)
    {
        for (var i = from; i <= s.Length - value.Length; i++)
        {
            if (StartsWith(s, i, value))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsKeyword(string word)
    {
        switch (word)
        {
            case "using": case "namespace": case "public": case "private": case "protected": case "internal":
            case "static": case "readonly": case "sealed": case "partial": case "override": case "class":
            case "new": case "return": case "var": case "void": case "int": case "string": case "bool":
            case "if": case "else": case "foreach": case "for": case "in": case "while": case "true":
            case "false": case "null": case "this": case "async": case "await": case "get": case "set":
            case "out": case "is": case "not": case "const": case "extern": case "double": case "char":
            case "switch": case "case": case "break": case "default": case "throw": case "try": case "catch":
                return true;
            default:
                return false;
        }
    }
}
