using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace NetWasm.UI.Generator;

/// <summary>Parses a <c>.nwui</c> template (HTML-like markup with Razor-style <c>@</c> transitions).</summary>
internal sealed class TemplateParser
{
    private static readonly HashSet<string> VoidElements = new(System.StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "source", "track", "wbr",
    };

    private static readonly Regex ForeachHeader = new(
        @"^\s*(?<type>.+?)\s+(?<name>@?[A-Za-z_][A-Za-z0-9_]*)\s+in\s+(?<expr>[\s\S]+?)\s*$",
        RegexOptions.CultureInvariant);

    private readonly string s;
    private int pos;

    private TemplateParser(string text) => s = text;

    public static TemplateFile Parse(string text)
    {
        var parser = new TemplateParser(text);
        var file = new TemplateFile();
        parser.ParseNodes(file.Nodes, file, inBlock: false, closingTag: null);
        return file;
    }

    /// <summary>Parses markup until EOF, a '}' (when <paramref name="inBlock"/>) or a closing tag.</summary>
    private void ParseNodes(List<TNode> nodes, TemplateFile? file, bool inBlock, TElement? closingTag)
    {
        var text = new StringBuilder();
        var textStart = pos;

        void FlushText()
        {
            if (text.Length > 0)
            {
                nodes.Add(new TText(text.ToString()) { Start = textStart });
                text.Clear();
            }
        }

        while (pos < s.Length)
        {
            var c = s[pos];
            if (text.Length == 0)
            {
                textStart = pos;
            }

            if (c == '<')
            {
                if (StartsWith("<!--"))
                {
                    var end = s.IndexOf("-->", pos + 4, System.StringComparison.Ordinal);
                    if (end < 0)
                    {
                        throw Error("Unterminated HTML comment.", pos, 4);
                    }

                    pos = end + 3;
                    continue;
                }

                if (StartsWith("</"))
                {
                    FlushText();
                    if (closingTag is null)
                    {
                        var nameEnd = ScanTagName(pos + 2);
                        throw Error($"Unexpected closing tag '</{s.Substring(pos + 2, nameEnd - pos - 2)}>'.", pos, nameEnd - pos + 1);
                    }

                    return;
                }

                if (pos + 1 < s.Length && char.IsLetter(s[pos + 1]))
                {
                    FlushText();
                    nodes.Add(ParseElement(file));
                    continue;
                }

                text.Append(c);
                pos++;
                continue;
            }

            if (c == '}' && inBlock)
            {
                FlushText();
                return;
            }

            if (c == '@')
            {
                if (StartsWith("@@"))
                {
                    text.Append('@');
                    pos += 2;
                    continue;
                }

                if (StartsWith("@*"))
                {
                    var end = s.IndexOf("*@", pos + 2, System.StringComparison.Ordinal);
                    if (end < 0)
                    {
                        throw Error("Unterminated Razor comment '@* … *@'.", pos, 2);
                    }

                    pos = end + 2;
                    continue;
                }

                // e-mail addresses and the like: "a@b" stays text.
                if (pos > 0 && char.IsLetterOrDigit(s[pos - 1]))
                {
                    text.Append(c);
                    pos++;
                    continue;
                }

                FlushText();
                ParseTransition(nodes, file);
                continue;
            }

            text.Append(c);
            pos++;
        }

        FlushText();
        if (inBlock)
        {
            throw Error("Missing '}' to close the block.", textStart, 1);
        }

        if (closingTag is not null)
        {
            throw Error($"Element <{closingTag.Tag}> is never closed.", closingTag.Start, closingTag.Tag.Length + 1);
        }
    }

    private void ParseTransition(List<TNode> nodes, TemplateFile? file)
    {
        var at = pos;
        pos++; // '@'
        if (pos >= s.Length)
        {
            throw Error("Expected an expression after '@'.", at);
        }

        if (s[pos] == '(')
        {
            var end = CSharpScanner.SkipBalanced(s, pos);
            if (end < 0)
            {
                throw Error("Unbalanced parentheses in '@( … )'.", at, 2);
            }

            nodes.Add(new TExpr(new Directive(s.Substring(pos + 1, end - pos - 2), pos + 1)) { Start = at });
            pos = end;
            return;
        }

        if (!CSharpScanner.IsIdentStart(s[pos]))
        {
            throw Error("Unexpected '@'. Use '@@' for a literal '@'.", at);
        }

        var identEnd = CSharpScanner.ScanIdent(s, pos);
        var keyword = s.Substring(pos, identEnd - pos);
        switch (keyword)
        {
            case "if":
                pos = identEnd;
                nodes.Add(ParseIf(at, file));
                return;
            case "foreach":
                pos = identEnd;
                nodes.Add(ParseForeach(at, file));
                return;
            case "render":
                {
                    pos = identEnd;
                    SkipSpaces();
                    if (pos >= s.Length || s[pos] != '(')
                    {
                        throw Error("Expected '(' after '@render'.", at, 7);
                    }

                    var end = CSharpScanner.SkipBalanced(s, pos);
                    if (end < 0)
                    {
                        throw Error("Unbalanced parentheses in '@render( … )'.", at, 7);
                    }

                    nodes.Add(new TRender(new Directive(s.Substring(pos + 1, end - pos - 2), pos + 1)) { Start = at });
                    pos = end;
                    return;
                }

            case "code":
            case "using":
            case "namespace":
            case "prop":
            case "inject":
                if (file is null)
                {
                    throw Error($"'@{keyword}' is only allowed at the top level of a template.", at, keyword.Length + 1);
                }

                pos = identEnd;
                ParseTopLevelDirective(keyword, at, file);
                return;
            case "else":
                throw Error("'@else' without a matching '@if'.", at, 5);
            case "for":
            case "while":
            case "switch":
            case "do":
            case "functions":
            case "page":
                throw new TemplateException("NWUI003", $"'@{keyword}' is not supported in .nwui templates (supported: @if, @foreach, @render, @code, @prop, @inject, @using, @namespace).", at, keyword.Length + 1);
        }

        var exprEnd = CSharpScanner.ScanImplicit(s, pos);
        if (exprEnd < 0)
        {
            throw Error("Unbalanced brackets in expression.", at);
        }

        nodes.Add(new TExpr(new Directive(s.Substring(pos, exprEnd - pos), pos)) { Start = at });
        pos = exprEnd;
    }

    private void ParseTopLevelDirective(string keyword, int at, TemplateFile file)
    {
        if (keyword == "code")
        {
            SkipWhitespace();
            if (pos >= s.Length || s[pos] != '{')
            {
                throw Error("Expected '{' after '@code'.", at, 5);
            }

            var end = CSharpScanner.SkipBalanced(s, pos);
            if (end < 0)
            {
                throw Error("Unbalanced braces in '@code { … }'.", at, 5);
            }

            file.CodeBlocks.Add(new Directive(s.Substring(pos + 1, end - pos - 2), pos + 1));
            pos = end;
            return;
        }

        SkipSpaces();
        var lineEnd = s.IndexOf('\n', pos);
        if (lineEnd < 0)
        {
            lineEnd = s.Length;
        }

        var value = s.Substring(pos, lineEnd - pos).Trim().TrimEnd(';').Trim();
        if (value.Length == 0)
        {
            throw Error($"Expected a name after '@{keyword}'.", at, keyword.Length + 1);
        }

        if (keyword == "prop")
        {
            file.Props.Add(ParseProp(value, pos, at, keyword));
            pos = lineEnd;
            return;
        }

        if (keyword == "inject")
        {
            var decl = ParseProp(value, pos, at, keyword);
            if (decl.Default is not null)
            {
                throw Error("'@inject' cannot have a default value; register the service with Services.Add/AddLazy instead.", at, 7);
            }

            file.Injects.Add(new InjectDecl(decl.Type, decl.Name));
            pos = lineEnd;
            return;
        }

        var d = new Directive(value, pos);
        if (keyword == "using")
        {
            file.Usings.Add(d);
        }
        else
        {
            file.Namespace = d;
        }

        pos = lineEnd;
    }

    /// <summary><c>Type Name</c> or <c>Type Name = default</c>; <paramref name="start"/> is the offset of <paramref name="decl"/>.</summary>
    private PropDecl ParseProp(string decl, int start, int at, string keyword)
    {
        var depth = 0;
        var eq = -1;
        for (var i = 0; i < decl.Length && eq < 0; i++)
        {
            switch (decl[i])
            {
                case '<' or '(' or '[' or '{':
                    depth++;
                    break;
                case '>' or ')' or ']' or '}':
                    depth--;
                    break;
                case '=' when depth == 0:
                    eq = i;
                    break;
            }
        }

        var left = (eq < 0 ? decl : decl.Substring(0, eq)).TrimEnd();
        var nameStart = left.Length;
        while (nameStart > 0 && CSharpScanner.IsIdentPart(left[nameStart - 1]))
        {
            nameStart--;
        }

        var name = left.Substring(nameStart);
        var type = left.Substring(0, nameStart).Trim();
        if (name.Length == 0 || !CSharpScanner.IsIdentStart(name[0]) || type.Length == 0)
        {
            throw Error(keyword == "prop"
                ? "Expected '@prop Type Name' or '@prop Type Name = default'."
                : $"Expected '@{keyword} Type Name'.", at, keyword.Length + 1);
        }

        Directive? defaultValue = null;
        if (eq >= 0)
        {
            var raw = decl.Substring(eq + 1);
            var code = raw.Trim();
            if (code.Length == 0)
            {
                throw Error($"Expected a default value after '=' in '@{keyword} {left}'.", start + eq, 1);
            }

            defaultValue = new Directive(code, start + eq + 1 + (raw.Length - raw.TrimStart().Length));
        }

        return new PropDecl(new Directive(type, start), new Directive(name, start + nameStart), defaultValue);
    }

    private TIf ParseIf(int at, TemplateFile? file)
    {
        var node = new TIf { Start = at };
        var branch = new Branch { Start = at, Condition = ParseParenthesized("@if") };
        ParseBlock(branch.Body, "@if");
        node.Branches.Add(branch);

        while (true)
        {
            var save = pos;
            SkipWhitespace();
            if (StartsWith("@else"))
            {
                pos++;
            }

            if (!StartsWithKeyword("else"))
            {
                pos = save;
                return node;
            }

            var elseAt = pos;
            pos += 4;
            SkipWhitespace();
            if (StartsWithKeyword("if"))
            {
                pos += 2;
                var b = new Branch { Start = elseAt, Condition = ParseParenthesized("else if") };
                ParseBlock(b.Body, "else if");
                node.Branches.Add(b);
                continue;
            }

            var last = new Branch { Start = elseAt };
            ParseBlock(last.Body, "else");
            node.Branches.Add(last);
            return node;
        }
    }

    private TForeach ParseForeach(int at, TemplateFile? file)
    {
        var header = ParseParenthesized("@foreach");
        var m = ForeachHeader.Match(header.Code);
        if (!m.Success)
        {
            throw Error("Expected '@foreach (var item in items) { … }'.", header.Start, header.Code.Length);
        }

        var type = m.Groups["type"].Value.Trim();
        var expr = m.Groups["expr"];
        var node = new TForeach
        {
            Start = at,
            Type = type == "var" ? null : type,
            Variable = m.Groups["name"].Value,
            Collection = new Directive(expr.Value, header.Start + expr.Index),
        };
        ParseBlock(node.Body, "@foreach");
        return node;
    }

    private Directive ParseParenthesized(string what)
    {
        SkipWhitespace();
        if (pos >= s.Length || s[pos] != '(')
        {
            throw Error($"Expected '(' after '{what}'.", System.Math.Min(pos, s.Length - 1));
        }

        var end = CSharpScanner.SkipBalanced(s, pos);
        if (end < 0)
        {
            throw Error($"Unbalanced parentheses after '{what}'.", pos);
        }

        var d = new Directive(s.Substring(pos + 1, end - pos - 2), pos + 1);
        pos = end;
        return d;
    }

    private void ParseBlock(List<TNode> body, string what)
    {
        SkipWhitespace();
        if (pos >= s.Length || s[pos] != '{')
        {
            throw Error($"Expected '{{' after '{what}'.", System.Math.Min(pos, s.Length - 1));
        }

        pos++;
        ParseNodes(body, null, inBlock: true, closingTag: null);
        pos++; // '}'
    }

    private TElement ParseElement(TemplateFile? file)
    {
        var start = pos;
        pos++; // '<'
        var nameEnd = ScanTagName(pos);
        var element = new TElement(s.Substring(pos, nameEnd - pos)) { Start = start, TagStart = pos };
        pos = nameEnd;

        while (true)
        {
            SkipWhitespace();
            if (pos >= s.Length)
            {
                throw Error($"Unterminated start tag <{element.Tag}>.", start, element.Tag.Length + 1);
            }

            if (StartsWith("/>"))
            {
                pos += 2;
                return element;
            }

            if (s[pos] == '>')
            {
                pos++;
                break;
            }

            element.Attributes.Add(ParseAttribute(element));
        }

        if (!element.IsComponent && VoidElements.Contains(element.Tag))
        {
            return element;
        }

        ParseNodes(element.Children, null, inBlock: false, closingTag: element);

        // At "</"
        var closeStart = pos;
        pos += 2;
        var closeEnd = ScanTagName(pos);
        var closeName = s.Substring(pos, closeEnd - pos);
        if (closeName != element.Tag)
        {
            throw Error($"Expected '</{element.Tag}>' but found '</{closeName}>'.", closeStart, closeEnd - closeStart + 1);
        }

        pos = closeEnd;
        SkipWhitespace();
        if (pos >= s.Length || s[pos] != '>')
        {
            throw Error($"Expected '>' to end '</{element.Tag}'.", closeStart, closeEnd - closeStart);
        }

        pos++;
        return element;
    }

    private TAttribute ParseAttribute(TElement element)
    {
        var start = pos;
        while (pos < s.Length && !char.IsWhiteSpace(s[pos]) && s[pos] != '=' && s[pos] != '>' && !StartsWith("/>"))
        {
            pos++;
        }

        if (pos == start)
        {
            throw Error($"Expected an attribute name in <{element.Tag}>.", start);
        }

        var attr = new TAttribute(s.Substring(start, pos - start), start);
        var save = pos;
        SkipWhitespace();
        if (pos >= s.Length || s[pos] != '=')
        {
            pos = save;
            return attr;
        }

        pos++;
        SkipWhitespace();
        attr.HasValue = true;
        if (pos >= s.Length)
        {
            throw Error($"Expected a value for attribute '{attr.Name}'.", start, attr.Name.Length);
        }

        var quote = s[pos];
        if (attr.IsDirective)
        {
            attr.Code = quote is '"' or '\'' ? ParseQuotedCode(attr, quote) : ParseUnquotedCode(attr);
            return attr;
        }

        if (quote is '"' or '\'')
        {
            pos++;
            ParseSegments(attr, quote);
        }
        else if (quote == '@')
        {
            attr.Segments.Add(ParseCodeSegment(attr));
        }
        else
        {
            var vStart = pos;
            while (pos < s.Length && !char.IsWhiteSpace(s[pos]) && s[pos] != '>' && !StartsWith("/>"))
            {
                pos++;
            }

            attr.Segments.Add(new Segment(false, s.Substring(vStart, pos - vStart), vStart));
        }

        return attr;
    }

    /// <summary>Directive values are C#: <c>@onclick="() => Say(&quot;hi&quot;)"</c>. A leading '@' is optional.</summary>
    private Directive ParseQuotedCode(TAttribute attr, char quote)
    {
        pos++;
        if (pos < s.Length && s[pos] == '@')
        {
            pos++;
        }

        var end = CSharpScanner.ScanUntil(s, pos, quote);
        if (end < 0)
        {
            throw Error($"Unterminated value for '{attr.Name}'.", attr.Start, attr.Name.Length);
        }

        var d = new Directive(s.Substring(pos, end - pos), pos);
        pos = end + 1;
        if (d.Code.Trim().Length == 0)
        {
            throw Error($"'{attr.Name}' needs a C# value.", attr.Start, attr.Name.Length);
        }

        return d;
    }

    private Directive ParseUnquotedCode(TAttribute attr)
    {
        var seg = ParseCodeSegment(attr);
        return new Directive(seg.Text, seg.Start);
    }

    private Segment ParseCodeSegment(TAttribute attr)
    {
        var at = pos;
        if (s[pos] == '@')
        {
            pos++;
        }

        if (pos < s.Length && s[pos] == '(')
        {
            var end = CSharpScanner.SkipBalanced(s, pos);
            if (end < 0)
            {
                throw Error($"Unbalanced parentheses in '{attr.Name}'.", at);
            }

            var seg = new Segment(true, s.Substring(pos + 1, end - pos - 2), pos + 1);
            pos = end;
            return seg;
        }

        if (pos < s.Length && CSharpScanner.IsIdentStart(s[pos]))
        {
            var end = CSharpScanner.ScanImplicit(s, pos);
            if (end < 0)
            {
                throw Error($"Unbalanced brackets in '{attr.Name}'.", at);
            }

            var seg = new Segment(true, s.Substring(pos, end - pos), pos);
            pos = end;
            return seg;
        }

        throw Error($"Expected a C# expression for '{attr.Name}'.", at);
    }

    private void ParseSegments(TAttribute attr, char quote)
    {
        var literal = new StringBuilder();
        var literalStart = pos;
        while (true)
        {
            if (pos >= s.Length)
            {
                throw Error($"Unterminated value for attribute '{attr.Name}'.", attr.Start, attr.Name.Length);
            }

            var c = s[pos];
            if (c == quote)
            {
                pos++;
                break;
            }

            if (c == '@' && StartsWith("@@"))
            {
                literal.Append('@');
                pos += 2;
                continue;
            }

            if (c == '@' && pos + 1 < s.Length && (s[pos + 1] == '(' || CSharpScanner.IsIdentStart(s[pos + 1]))
                && (pos == 0 || !char.IsLetterOrDigit(s[pos - 1])))
            {
                if (literal.Length > 0)
                {
                    attr.Segments.Add(new Segment(false, literal.ToString(), literalStart));
                    literal.Clear();
                }

                attr.Segments.Add(ParseCodeSegment(attr));
                literalStart = pos;
                continue;
            }

            literal.Append(c);
            pos++;
        }

        if (literal.Length > 0 || attr.Segments.Count == 0)
        {
            attr.Segments.Add(new Segment(false, literal.ToString(), literalStart));
        }
    }

    private int ScanTagName(int i)
    {
        while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] is '-' or ':' or '.' or '_'))
        {
            i++;
        }

        return i;
    }

    private bool StartsWith(string value) => string.CompareOrdinal(s, pos, value, 0, value.Length) == 0;

    private bool StartsWithKeyword(string keyword) =>
        StartsWith(keyword) && (pos + keyword.Length >= s.Length || !CSharpScanner.IsIdentPart(s[pos + keyword.Length]));

    private void SkipWhitespace()
    {
        while (pos < s.Length && char.IsWhiteSpace(s[pos]))
        {
            pos++;
        }
    }

    private void SkipSpaces()
    {
        while (pos < s.Length && (s[pos] == ' ' || s[pos] == '\t'))
        {
            pos++;
        }
    }

    private static TemplateException Error(string message, int start, int length = 1) =>
        new("NWUI001", message, start, length);
}
