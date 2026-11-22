using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace RioCharts.Docs.Shared;

/// <summary>
/// A small, forgiving highlighter for the Razor, C#, JSON and shell snippets on this site. It produces
/// HTML spans with <c>tok-*</c> classes; anything it does not recognize stays plain text.
/// </summary>
public static partial class CodeHighlighter
{
    private static readonly HashSet<string> Keywords =
    [
        "new", "var", "private", "public", "static", "readonly", "const", "async", "await", "return", "true", "false",
        "null", "using", "class", "record", "sealed", "void", "string", "double", "int", "bool", "if", "else", "foreach",
        "in", "override", "protected", "get", "set", "init", "this", "typeof", "is", "and", "or", "not", "for", "while",
        "namespace", "object", "long", "float", "decimal", "dotnet", "add", "package",
    ];

    // Order matters: the first alternative that matches at a position wins.
    [GeneratedRegex("""
        (?<comment>@\*[\s\S]*?\*@|//[^\n]*|<!--[\s\S]*?-->)
        |(?<tag></?)(?<tagname>[A-Za-z][\w.]*)
        |(?<tagend>/?>)
        |(?<attr>@?[A-Za-z_][\w:-]*)(?==")
        |(?<str>"(?:[^"\\\n]|\\.)*")
        |(?<directive>@@?[A-Za-z]+)
        |(?<num>\b\d+(?:\.\d+)?\b)
        |(?<arrow>=>)
        |(?<ident>[A-Za-z_]\w*)
        """, RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex Token();

    public static string ToHtml(string code, string language = "razor")
    {
        var sb = new StringBuilder(code.Length * 2);
        if (language is "markdown")
        {
            return System.Net.WebUtility.HtmlEncode(code); // text to paste: no highlighting
        }

        if (language is "json")
        {
            HighlightJson(code, sb);
            return sb.ToString();
        }

        Highlight(code, sb, markup: language is "razor" or "html");
        return sb.ToString();
    }

    private static void Highlight(string code, StringBuilder sb, bool markup)
    {
        var pos = 0;
        while (pos < code.Length)
        {
            var m = Token().Match(code, pos);
            if (!m.Success)
            {
                Plain(sb, code[pos..]);
                return;
            }

            Plain(sb, code[pos..m.Index]);
            pos = m.Index + m.Length;

            if (m.Groups["comment"].Success) Span(sb, "comment", m.Value);
            else if (m.Groups["tag"].Success)
            {
                // A '<' only opens a tag in markup; in C# it is a generic or a comparison.
                if (markup)
                {
                    Span(sb, "punct", m.Groups["tag"].Value);
                    Span(sb, "tag", m.Groups["tagname"].Value);
                }
                else
                {
                    Plain(sb, m.Groups["tag"].Value);
                    Ident(sb, code, m.Groups["tagname"].Index, m.Groups["tagname"].Value);
                }
            }
            else if (m.Groups["tagend"].Success)
            {
                if (markup) Span(sb, "punct", m.Value);
                else Plain(sb, m.Value);
            }
            else if (m.Groups["attr"].Success && markup)
            {
                Span(sb, "attr", m.Value);
                pos = AttributeValue(code, pos, m.Value, sb);
            }
            else if (m.Groups["attr"].Success) Ident(sb, code, m.Index, m.Value);
            else if (m.Groups["str"].Success) Span(sb, "str", m.Value);
            else if (m.Groups["directive"].Success) Span(sb, "kw", m.Value);
            else if (m.Groups["num"].Success) Span(sb, "num", m.Value);
            else if (m.Groups["arrow"].Success) Span(sb, "kw", m.Value);
            else Ident(sb, code, m.Index, m.Value);
        }
    }

    /// <summary>Writes <c>="value"</c> after an attribute name; the value may hold C# with nested quotes.</summary>
    private static int AttributeValue(string code, int pos, string name, StringBuilder sb)
    {
        // code[pos] is '=' and code[pos + 1] is '"'.
        var start = pos + 2;
        var depth = 0;
        var inString = false;
        var end = start;
        for (; end < code.Length; end++)
        {
            var c = code[end];
            if (c == '\n') break;
            if (inString)
            {
                if (c == '\\') end++;
                else if (c == '"') inString = false;
            }
            else if (c is '{' or '(' or '[') depth++;
            else if (c is '}' or ')' or ']') depth = Math.Max(0, depth - 1);
            else if (c == '"')
            {
                if (depth == 0) break;
                inString = true;
            }
        }

        Span(sb, "punct", "=");
        var value = code[start..Math.Min(end, code.Length)];
        // Component parameters hold C# unless they are text by nature; HTML attributes are text.
        var isCode = !TextAttributes.Contains(name) && !char.IsLower(name[0]) || name.StartsWith('@');
        sb.Append("<span class=\"tok-str\">\"</span>");
        if (isCode)
        {
            Highlight(value, sb, markup: false);
        }
        else
        {
            Span(sb, "str", value);
        }

        if (end < code.Length && code[end] == '"')
        {
            sb.Append("<span class=\"tok-str\">\"</span>");
            return end + 1;
        }

        return end;
    }

    private static readonly HashSet<string> TextAttributes =
    [
        "Name", "Title", "Label", "Format", "Stack", "Group", "TimeZone", "Class", "Text", "Unit", "Id", "Color", "Description",
    ];

    private static void Ident(StringBuilder sb, string code, int index, string ident)
    {
        if (Keywords.Contains(ident))
        {
            Span(sb, "kw", ident);
            return;
        }

        var next = index + ident.Length < code.Length ? code[index + ident.Length] : '\0';
        if (next == '(') Span(sb, "fn", ident);
        else if (char.IsUpper(ident[0])) Span(sb, "type", ident);
        else Plain(sb, ident);
    }

    [GeneratedRegex("""
        (?<key>"(?:[^"\\\n]|\\.)*")(?=\s*:)
        |(?<str>"(?:[^"\\\n]|\\.)*")
        |(?<num>-?\b\d+(?:\.\d+)?(?:[eE][+-]?\d+)?\b)
        |(?<kw>\b(?:true|false|null)\b)
        """, RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex JsonToken();

    private static void HighlightJson(string code, StringBuilder sb)
    {
        var pos = 0;
        foreach (Match m in JsonToken().Matches(code))
        {
            Plain(sb, code[pos..m.Index]);
            var kind = m.Groups["key"].Success ? "attr" : m.Groups["str"].Success ? "str" : m.Groups["num"].Success ? "num" : "kw";
            Span(sb, kind, m.Value);
            pos = m.Index + m.Length;
        }

        Plain(sb, code[pos..]);
    }

    private static void Span(StringBuilder sb, string kind, string text) =>
        sb.Append("<span class=\"tok-").Append(kind).Append("\">").Append(WebUtility.HtmlEncode(text)).Append("</span>");

    private static void Plain(StringBuilder sb, string text) => sb.Append(WebUtility.HtmlEncode(text));
}
