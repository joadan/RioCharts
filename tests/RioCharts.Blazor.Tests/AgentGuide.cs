using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.AspNetCore.Components;
using RioCharts.Core;

namespace RioCharts.Blazor.Tests;

/// <summary>
/// Builds llms-full.txt, the guide for coding agents: docs/llms/guide.md with the example
/// components from Guide/ (compiled and rendered by <see cref="AgentGuideTests"/>) and a reference
/// of every component parameter and the types they use, from reflection and the XML docs. So the
/// guide cannot drift from the code: a changed parameter fails the test until the file is accepted.
/// </summary>
internal static partial class AgentGuide
{
    /// <summary>The examples in the order of the guide; every .razor file in Guide/ must be listed.</summary>
    public static readonly string[] Examples =
        ["LineChart", "BarsFromItems", "StackedBars", "Donut", "TwoAxes", "Interactive", "LiveData", "FromCode"];

    public static string Root { get; } = FindRoot();

    public static string OutputPath => Path.Combine(Root, "samples", "RioCharts.Docs", "wwwroot", "llms-full.txt");

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "RioCharts.sln")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException($"RioCharts.sln not found above {AppContext.BaseDirectory}.");
    }

    public static string Build()
    {
        var template = File.ReadAllText(Path.Combine(Root, "docs", "llms", "guide.md")).ReplaceLineEndings("\n");
        var text = template.Replace("{{examples}}", ExamplesText().TrimEnd(), StringComparison.Ordinal)
            .Replace("{{api}}", new Reference().Build().TrimEnd(), StringComparison.Ordinal);
        return text.TrimEnd() + "\n";
    }

    // ----- Examples -----

    private static string ExamplesText()
    {
        var folder = Path.Combine(Root, "tests", "RioCharts.Blazor.Tests", "Guide");
        var sb = new StringBuilder();
        foreach (var name in Examples)
        {
            var lines = File.ReadAllText(Path.Combine(folder, name + ".razor")).ReplaceLineEndings("\n").Split('\n');
            var header = HeaderRegex().Match(lines[0]);
            if (!header.Success)
            {
                throw new InvalidOperationException($"Guide/{name}.razor must start with @* Title | Description *@.");
            }

            sb.Append("### ").Append(header.Groups[1].Value.Trim()).Append("\n\n");
            sb.Append(header.Groups[2].Value.Trim()).Append("\n\n");
            sb.Append("```razor\n").Append(string.Join('\n', lines.Skip(1)).Trim('\n')).Append("\n```\n\n");
        }

        return sb.ToString();
    }

    [GeneratedRegex(@"^@\*(.*?)\|(.*)\*@\s*$")]
    private static partial Regex HeaderRegex();

    // ----- Reference -----

    private sealed class Reference
    {
        private readonly Dictionary<string, XElement> _docs = LoadDocs();
        private readonly NullabilityInfoContext _nullability = new();
        private readonly List<Type> _types = [];
        private readonly HashSet<Type> _seen = [];
        private readonly StringBuilder _sb = new();

        private static readonly Assembly Blazor = typeof(RioChart<>).Assembly;

        public string Build()
        {
            var components = Blazor.GetExportedTypes()
                .Where(t => typeof(IComponent).IsAssignableFrom(t) && !t.IsAbstract)
                .ToList();
            var series = components.Where(t => IsA(t, typeof(SeriesBase<>))).ToList();
            var annotations = components.Where(t => IsA(t, typeof(AnnotationBase))).ToList();
            var other = components.Except(series).Except(annotations).Where(t => t != typeof(RioChart<>)).ToList();

            _sb.Append("### Components\n\n");
            Component(typeof(RioChart<>), stopAt: typeof(ComponentBase));
            Methods(typeof(RioChart<>), "Methods (call through `@ref`)");

            Section(typeof(SeriesBase<>), "Parameters of every series (`SeriesBase<TItem>`)", "Every series component below takes these, plus its own.");
            foreach (var type in series)
            {
                Component(type, stopAt: typeof(SeriesBase<>));
            }

            Section(typeof(AnnotationBase), "Parameters of every annotation (`AnnotationBase`)", "Every annotation component below takes these, plus its own.");
            foreach (var type in annotations.Concat(other))
            {
                Component(type, stopAt: typeof(AnnotationBase));
            }

            _sb.Append("### Types used by parameters\n\n");
            for (var i = 0; i < _types.Count; i++)
            {
                TypeSection(_types[i]);
            }

            return _sb.ToString();
        }

        private static bool IsA(Type type, Type baseType)
        {
            for (var t = type.BaseType; t is not null; t = t.BaseType)
            {
                if (t == baseType || (t.IsGenericType && t.GetGenericTypeDefinition() == baseType))
                {
                    return true;
                }
            }

            return false;
        }

        private void Section(Type type, string title, string note)
        {
            _sb.Append("#### ").Append(title).Append("\n\n").Append(note).Append("\n\n");
            Parameters(type, typeof(ComponentBase));
        }

        private void Component(Type type, Type stopAt)
        {
            var tag = type.Name.Split('`')[0];
            _sb.Append("#### `<").Append(tag).Append(">`");
            if (type.IsGenericTypeDefinition)
            {
                _sb.Append(" (`").Append(Name(type)).Append("`)");
            }

            _sb.Append("\n\n");
            if (Summary(type) is { Length: > 0 } summary)
            {
                _sb.Append(summary).Append("\n\n");
            }

            Parameters(type, stopAt);
        }

        private void Parameters(Type type, Type stopAt)
        {
            var defaults = Instance(type);
            // The chart's parameters are spread over partial files: the basics go first.
            static int First(string name) => Array.IndexOf<string>(["Items", "ChildContent", "Title", "Height", "Width"], name) is var i and >= 0 ? i : 99;
            var lines = new List<(bool Event, int Order, string Text)>();
            for (var t = type; t is not null && t != stopAt && !(t.IsGenericType && t.GetGenericTypeDefinition() == stopAt); t = t.BaseType)
            {
                var declaring = t.IsGenericType ? t.GetGenericTypeDefinition() : t;
                foreach (var p in declaring.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (p.GetCustomAttribute<ParameterAttribute>() is null)
                    {
                        continue;
                    }

                    Collect(p.PropertyType);
                    var line = $"- `{p.Name}`: `{Name(p.PropertyType, _nullability.Create(p))}`";
                    if (DefaultText(defaults, p.Name) is { } value)
                    {
                        line += $" = `{value}`";
                    }

                    if (Summary(p) is { Length: > 0 } summary)
                    {
                        line += " — " + summary;
                    }

                    lines.Add((p.PropertyType.Name.StartsWith("EventCallback", StringComparison.Ordinal), First(p.Name), line));
                }
            }

            if (lines.Count == 0)
            {
                _sb.Append("No parameters of its own.\n\n");
                return;
            }

            // Settings first, then events; otherwise in declaration order.
            _sb.Append(string.Join('\n', lines.OrderBy(l => l.Event).ThenBy(l => l.Order).Select(l => l.Text))).Append("\n\n");
        }

        private void Methods(Type type, string title)
        {
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName && !Hidden(m) && m.GetBaseDefinition().DeclaringType == m.DeclaringType)
                .ToList();
            if (methods.Count == 0)
            {
                return;
            }

            _sb.Append("##### ").Append(title).Append("\n\n");
            foreach (var m in methods)
            {
                foreach (var p in m.GetParameters())
                {
                    Collect(p.ParameterType);
                }

                _sb.Append("- `").Append(Signature(m)).Append('`');
                if (Summary(m) is { Length: > 0 } summary)
                {
                    _sb.Append(" — ").Append(summary);
                }

                _sb.Append('\n');
            }

            _sb.Append('\n');
        }

        private static bool Hidden(MethodInfo m) =>
            m.Name is "Equals" or "GetHashCode" or "ToString" or "Deconstruct" or "PrintMembers" or "<Clone>$" or "Dispose" or "DisposeAsync"
                or "SetParametersAsync" or "Attach"
            || m.GetCustomAttribute<Microsoft.JSInterop.JSInvokableAttribute>() is not null
            || m.GetCustomAttribute<System.Runtime.CompilerServices.CompilerGeneratedAttribute>() is not null
            || m.GetCustomAttribute<System.ComponentModel.EditorBrowsableAttribute>()?.State == System.ComponentModel.EditorBrowsableState.Never;

        // ----- Types used by parameters -----

        /// <summary>Queues RioCharts types that a parameter, property or argument uses.</summary>
        private void Collect(Type type)
        {
            if (type.IsByRef || type.IsArray || type.IsPointer)
            {
                Collect(type.GetElementType()!);
                return;
            }

            if (type.IsGenericType)
            {
                foreach (var argument in type.GetGenericArguments())
                {
                    Collect(argument);
                }

                type = type.GetGenericTypeDefinition();
            }

            if (type.IsGenericParameter || type.Namespace?.StartsWith("RioCharts", StringComparison.Ordinal) != true
                || typeof(IComponent).IsAssignableFrom(type) || !type.IsPublic && !type.IsNestedPublic)
            {
                return;
            }

            if (_seen.Add(type))
            {
                _types.Add(type);
            }
        }

        private void TypeSection(Type type)
        {
            var kind = type.IsEnum ? "enum" : type.IsValueType ? "struct" : type.GetMethod("<Clone>$") is not null ? "record" : type.IsInterface ? "interface" : "class";
            _sb.Append("#### `").Append(Name(type)).Append("` (").Append(kind).Append(", ").Append(type.Namespace).Append(")\n\n");
            if (Summary(type) is { Length: > 0 } summary)
            {
                _sb.Append(summary).Append("\n\n");
            }

            if (type.IsEnum)
            {
                var flags = type.GetCustomAttribute<FlagsAttribute>() is not null;
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
                {
                    _sb.Append("- `").Append(field.Name).Append('`');
                    if (Summary(field) is { Length: > 0 } s)
                    {
                        _sb.Append(" — ").Append(s);
                    }

                    _sb.Append('\n');
                }

                if (flags)
                {
                    _sb.Append("\nFlags: combine values with `|`.\n");
                }

                _sb.Append('\n');
                return;
            }

            var defaults = Instance(type);
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Where(p => p.GetIndexParameters().Length == 0 && p.Name != "EqualityContract")
                .ToList();
            if (properties.Count > 60)
            {
                // E.g. ChartTexts: one string per text. The names say enough.
                _sb.Append(properties.Count).Append(" properties: ")
                    .Append(string.Join(", ", properties.Select(p => $"`{p.Name}`"))).Append(".\n\n");
            }
            else if (properties.Count > 0)
            {
                foreach (var p in properties)
                {
                    Collect(p.PropertyType);
                    var settable = p.SetMethod?.IsPublic == true;
                    var line = $"- `{p.Name}`: `{Name(p.PropertyType, _nullability.Create(p))}`";
                    if (!settable && !(p.GetMethod?.IsStatic ?? false))
                    {
                        line += " (read-only)";
                    }
                    else if (p.GetMethod?.IsStatic == true)
                    {
                        line += " (static)";
                    }

                    if (settable && DefaultText(defaults, p.Name) is { } value)
                    {
                        line += $" = `{value}`";
                    }

                    if (Summary(p) is { Length: > 0 } s)
                    {
                        line += " — " + s;
                    }

                    _sb.Append(line).Append('\n');
                }

                _sb.Append('\n');
            }

            var constructors = type.GetConstructors()
                .Where(c => c.GetParameters().Length > 0 && !(c.GetParameters() is [var only] && only.ParameterType == type))
                .ToList();
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName && !Hidden(m))
                .ToList();
            if (constructors.Count + methods.Count == 0)
            {
                return;
            }

            foreach (var c in constructors)
            {
                _sb.Append("- `new ").Append(Name(type).Split('<')[0]).Append('(').Append(Parameters(c)).Append(")`");
                if (Summary(c) is { Length: > 0 } s)
                {
                    _sb.Append(" — ").Append(s);
                }

                _sb.Append('\n');
            }

            foreach (var m in methods)
            {
                _sb.Append("- `").Append(Signature(m)).Append('`');
                if (Summary(m) is { Length: > 0 } s)
                {
                    _sb.Append(" — ").Append(s);
                }

                _sb.Append('\n');
            }

            _sb.Append('\n');
        }

        // ----- Names and signatures -----

        private string Signature(MethodInfo m)
        {
            var generic = m.IsGenericMethodDefinition ? "<" + string.Join(", ", m.GetGenericArguments().Select(a => a.Name)) + ">" : "";
            var returns = Name(m.ReturnType, _nullability.Create(m.ReturnParameter));
            return $"{(m.IsStatic ? "static " : "")}{returns} {m.Name}{generic}({Parameters(m)})";
        }

        private string Parameters(MethodBase method) => string.Join(", ", method.GetParameters().Select(p =>
        {
            var type = p.ParameterType.IsByRef ? p.ParameterType.GetElementType()! : p.ParameterType;
            var text = $"{Name(type, _nullability.Create(p))} {p.Name}";
            if (p.HasDefaultValue)
            {
                text += " = " + (p.DefaultValue switch
                {
                    null => p.ParameterType.IsValueType && Nullable.GetUnderlyingType(p.ParameterType) is null ? "default" : "null",
                    string s => Literal(s),
                    bool b => b ? "true" : "false",
                    Enum e => $"{e.GetType().Name}.{e}",
                    IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                    var v => v.ToString(),
                });
            }

            return text;
        }));

        private static readonly Dictionary<Type, string> Aliases = new()
        {
            [typeof(double)] = "double", [typeof(float)] = "float", [typeof(int)] = "int", [typeof(long)] = "long",
            [typeof(bool)] = "bool", [typeof(string)] = "string", [typeof(object)] = "object", [typeof(decimal)] = "decimal",
            [typeof(byte)] = "byte", [typeof(void)] = "void",
        };

        private static string Name(Type type) => Name(type, null);

        private static string Name(Type type, NullabilityInfo? info)
        {
            if (Nullable.GetUnderlyingType(type) is { } underlying)
            {
                return Name(underlying) + "?";
            }

            var nullable = info is { ReadState: NullabilityState.Nullable } && !type.IsValueType ? "?" : "";
            if (type.IsArray)
            {
                return Name(type.GetElementType()!, info?.ElementType) + "[]" + nullable;
            }

            if (type.IsGenericParameter)
            {
                return type.Name; // TItem as the app declares it
            }

            if (Aliases.TryGetValue(type, out var alias))
            {
                return alias + nullable;
            }

            var name = type.Name.Split('`')[0];
            if (type.IsNested && !type.IsGenericParameter)
            {
                name = Name(type.DeclaringType!) + "." + name;
            }

            if (type.IsGenericType)
            {
                var arguments = type.GetGenericArguments();
                var argumentInfos = info?.GenericTypeArguments ?? [];
                name += "<" + string.Join(", ", arguments.Select((a, i) => Name(a, i < argumentInfos.Length ? argumentInfos[i] : null))) + ">";
            }

            return name + nullable;
        }

        // ----- Defaults -----

        private static object? Instance(Type type)
        {
            try
            {
                if (type.IsAbstract || type.IsInterface || type.IsEnum)
                {
                    return null;
                }

                if (type.IsGenericTypeDefinition)
                {
                    type = type.MakeGenericType([.. type.GetGenericArguments().Select(_ => typeof(double))]);
                }

                return type.GetConstructor(Type.EmptyTypes) is null && !type.IsValueType ? null : Activator.CreateInstance(type);
            }
            catch (Exception e) when (e is TargetInvocationException or ArgumentException or NotSupportedException or MissingMethodException)
            {
                return null;
            }
        }

        /// <summary>The default value as C#, or null when it says nothing (null, false, 0, empty, the enum's first value).</summary>
        private static string? DefaultText(object? instance, string property)
        {
            if (instance?.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance) is not { } p || p.GetMethod is null)
            {
                return null;
            }

            object? value;
            try
            {
                value = p.GetValue(instance);
            }
            catch (TargetInvocationException)
            {
                return null;
            }

            return value switch
            {
                null or false => null,
                string { Length: 0 } => null,
                string s => Literal(s),
                true => "true",
                Enum e when Convert.ToInt64(e, CultureInfo.InvariantCulture) == 0 => null,
                Enum e => $"{e.GetType().Name}.{e.ToString().Replace(", ", $" | {e.GetType().Name}.", StringComparison.Ordinal)}",
                double d when d == 0 => null,
                double d => d.ToString(CultureInfo.InvariantCulture),
                int i when i == 0 => null,
                int i => i.ToString(CultureInfo.InvariantCulture),
                TimeSpan t => t.ToString(),
                _ => null, // objects and collections: the summary says what the default means
            };
        }

        /// <summary>A string as a C# literal.</summary>
        private static string Literal(string s) => "\"" + s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal).Replace("\t", "\\t", StringComparison.Ordinal) + "\"";

        // ----- XML docs -----

        private static Dictionary<string, XElement> LoadDocs()
        {
            var docs = new Dictionary<string, XElement>(StringComparer.Ordinal);
            foreach (var assembly in new[] { typeof(RioChart<>).Assembly, typeof(ChartSpec).Assembly })
            {
                var path = Path.ChangeExtension(assembly.Location, ".xml");
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException("XML docs not found next to the assembly; the guide needs them.", path);
                }

                foreach (var member in XDocument.Load(path).Descendants("member"))
                {
                    docs[member.Attribute("name")!.Value] = member;
                }
            }

            return docs;
        }

        private static string TypeId(Type type)
        {
            if (type.IsGenericType && !type.IsGenericTypeDefinition)
            {
                type = type.GetGenericTypeDefinition();
            }

            return type.FullName!.Replace('+', '.');
        }

        private string? Summary(MemberInfo member)
        {
            var id = member switch
            {
                Type t => "T:" + TypeId(t),
                PropertyInfo p => "P:" + TypeId(p.DeclaringType!) + "." + p.Name,
                FieldInfo f => "F:" + TypeId(f.DeclaringType!) + "." + f.Name,
                MethodBase m => "M:" + TypeId(m.DeclaringType!) + "." + (m is ConstructorInfo ? "#ctor" : m.Name)
                    + (m.IsGenericMethodDefinition ? "``" + m.GetGenericArguments().Length : "")
                    + (m.GetParameters().Length == 0 ? "" : "(" + string.Join(",", m.GetParameters().Select(p => XmlParameter(p.ParameterType))) + ")"),
                _ => null,
            };
            if (id is null)
            {
                return null;
            }

            if (_docs.TryGetValue(id, out var element) && element.Element("summary") is { } summary)
            {
                return Text(summary);
            }

            // Positional records: the property is documented as a parameter of the type.
            if (member is PropertyInfo property && _docs.TryGetValue("T:" + TypeId(property.DeclaringType!), out var typeDoc)
                && typeDoc.Elements("param").FirstOrDefault(e => e.Attribute("name")?.Value == property.Name) is { } param)
            {
                return Text(param);
            }

            // <inheritdoc/> on an override: the base member's docs.
            if (member is PropertyInfo { DeclaringType.BaseType: { } baseType } inherited
                && baseType.GetProperty(inherited.Name, BindingFlags.Public | BindingFlags.Instance) is { } baseProperty && baseProperty != inherited)
            {
                return Summary(baseProperty);
            }

            return null;
        }

        private static string XmlParameter(Type type)
        {
            if (type.IsByRef)
            {
                return XmlParameter(type.GetElementType()!) + "@";
            }

            if (type.IsArray)
            {
                return XmlParameter(type.GetElementType()!) + "[]";
            }

            if (type.IsGenericParameter)
            {
                return (type.DeclaringMethod is null ? "`" : "``") + type.GenericParameterPosition;
            }

            if (type.IsGenericType)
            {
                var definition = type.GetGenericTypeDefinition().FullName!;
                return definition[..definition.IndexOf('`', StringComparison.Ordinal)].Replace('+', '.')
                    + "{" + string.Join(",", type.GetGenericArguments().Select(XmlParameter)) + "}";
            }

            return type.FullName!.Replace('+', '.');
        }

        /// <summary>Doc XML as one line of Markdown: cref and c become code, paragraphs become spaces.</summary>
        private static string Text(XElement element)
        {
            var sb = new StringBuilder();
            foreach (var node in element.Nodes())
            {
                sb.Append(node switch
                {
                    XText t => t.Value,
                    XElement { Name.LocalName: "see" or "seealso" } e when e.Attribute("cref") is { } cref =>
                        "`" + CrefName(cref.Value) + "`",
                    XElement { Name.LocalName: "see" } e when e.Attribute("langword") is { } word => "`" + word.Value + "`",
                    XElement { Name.LocalName: "see" } e when e.Attribute("href") is { } href => string.IsNullOrWhiteSpace(e.Value) ? href.Value : $"{e.Value} ({href.Value})",
                    XElement { Name.LocalName: "paramref" or "typeparamref" } e => "`" + e.Attribute("name")?.Value + "`",
                    XElement { Name.LocalName: "c" } e => "`" + e.Value + "`",
                    XElement { Name.LocalName: "code" } e => " `" + Regex.Replace(e.Value.Trim(), @"\s+", " ") + "` ",
                    XElement { Name.LocalName: "para" or "br" } e => " " + Text(e) + " ",
                    XElement { Name.LocalName: "list" } e => " " + string.Join("; ", e.Elements("item").Select(Text)) + " ",
                    XElement e => Text(e),
                    _ => "",
                });
            }

            return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        }

        private static string CrefName(string cref)
        {
            var name = cref.Length > 2 && cref[1] == ':' ? cref[2..] : cref;
            var paren = name.IndexOf('(', StringComparison.Ordinal);
            if (paren >= 0)
            {
                name = name[..paren];
            }

            var parts = name.Split('.');
            // A member: Type.Member; a type: Type.
            var last = parts[^1].Split('`')[0];
            return cref.StartsWith("T:", StringComparison.Ordinal) || parts.Length < 2 ? last : parts[^2].Split('`')[0] + "." + last;
        }
    }
}
