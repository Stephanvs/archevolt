using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Archevolt.Generators;

[Generator(LanguageNames.CSharp)]
public sealed partial class ArchevoltGenerator : IIncrementalGenerator
{
    static readonly SymbolDisplayFormat Names = SymbolDisplayFormat.FullyQualifiedFormat;
    static readonly SymbolDisplayFormat NamespaceNames = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        Register(context, "ComponentAttribute", static (symbol, ct) => GenerateComponent((INamedTypeSymbol)symbol, ct));
        Register(context, "BundleAttribute", static (symbol, ct) => GenerateBundle((INamedTypeSymbol)symbol, ct));
        Register(context, "QueryAttribute", static (symbol, ct) => GenerateQuery((INamedTypeSymbol)symbol, ct));
        Register(context, "EachAttribute", static (symbol, ct) => GenerateSystem((IMethodSymbol)symbol, true, ct));
        Register(context, "EcsSystemAttribute", static (symbol, ct) => GenerateSystem((IMethodSymbol)symbol, false, ct));
        Register(context, "RunConditionAttribute", static (symbol, ct) => GenerateSystem((IMethodSymbol)symbol, false, ct));
        Register(context, "EcsModuleAttribute", static (symbol, ct) => GenerateModule((INamedTypeSymbol)symbol, ct));
        Register(context, "ScheduleAttribute", static (symbol, ct) => GenerateSchedule((INamedTypeSymbol)symbol, ct));
    }

    static void Register(IncrementalGeneratorInitializationContext context, string attribute, Func<ISymbol, CancellationToken, GeneratedUnit> transform)
    {
        var models = context.SyntaxProvider.ForAttributeWithMetadataName("Archevolt." + attribute,
            static (_, _) => true, (syntax, cancellationToken) => transform(syntax.TargetSymbol, cancellationToken))
            .WithTrackingName(attribute + "Model");
        context.RegisterSourceOutput(models, static (output, model) =>
        {
            foreach (var problem in model.Problems) output.ReportDiagnostic(problem.Diagnostic);
            if (model.Source.Length != 0) output.AddSource(model.Hint, model.Source);
        });
    }

    static string TypeName(ITypeSymbol symbol) => symbol.ToDisplayString(Names);
    static string Escape(string name) => SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;
    static string Literal(string value) => SymbolDisplay.FormatLiteral(value, true);
    static bool Has(ISymbol symbol, string name) => Attributes(symbol, name).Any();
    static string AttributeName(AttributeData attribute) => attribute.AttributeClass?.OriginalDefinition.ToDisplayString().Split('.').Last() ?? "";
    static bool IsAttribute(AttributeData attribute, string name) => attribute.AttributeClass?.ContainingNamespace.ToDisplayString() == "Archevolt" && AttributeName(attribute).Split('<')[0] == name;
    static IEnumerable<AttributeData> Attributes(ISymbol symbol, string name) => symbol.GetAttributes().Where(attribute => IsAttribute(attribute, name));
    static string? NamedString(AttributeData attribute, string name) => attribute.NamedArguments.FirstOrDefault(pair => pair.Key == name).Value.Value as string;
    static bool NamedBool(AttributeData attribute, string name) => attribute.NamedArguments.FirstOrDefault(pair => pair.Key == name).Value.Value is true;
    static ComponentModel Component(ITypeSymbol symbol) => new(TypeName(symbol), symbol.ToDisplayString(), Has(symbol, "TrackChangesAttribute"),
        symbol is INamedTypeSymbol named && !named.GetMembers().OfType<IFieldSymbol>().Any(static field => !field.IsStatic),
        Attributes(symbol, "ResourceAttribute").Any(attribute => NamedBool(attribute, "OwnerThread")), !symbol.IsUnmanagedType);

    static GeneratedUnit Unit(ISymbol symbol, string kind, string source, List<Problem> problems)
    {
        var name = TypeName(symbol is IMethodSymbol method ? method.ContainingType : (ITypeSymbol)symbol) + (symbol is IMethodSymbol m ? "." + m.Name : "");
        ulong hash = 14695981039346656037UL;
        foreach (var character in name) hash = unchecked((hash ^ character) * 1099511628211UL);
        var readable = string.Concat(name.Replace("global::", "").Select(static character => character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '.' ? character : '_').Take(96));
        var hint = readable + "." + hash.ToString("x16", CultureInfo.InvariantCulture);
        return new(hint + "." + kind + ".g.cs", problems.Any(static problem => problem.Id != "AV008") ? "" : source,
            new ValueArray<Problem>(problems.ToImmutableArray()));
    }

    static void Error(List<Problem> problems, ISymbol symbol, string id, string message)
    {
        var location = symbol.Locations.FirstOrDefault(static location => location.IsInSource) ?? Location.None;
        var line = location.GetLineSpan();
        problems.Add(new(id, message, line.Path, location.SourceSpan, line.Span));
    }

    static bool ValidatePartial(INamedTypeSymbol symbol, List<Problem> problems, CancellationToken cancellationToken)
    {
        bool valid = true;
        for (var current = symbol; current is not null; current = current.ContainingType)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (current.Arity != 0 || current.DeclaringSyntaxReferences.Length == 0 ||
                current.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax(cancellationToken) is not TypeDeclarationSyntax declaration ||
                    !declaration.Modifiers.Any(SyntaxKind.PartialKeyword)))
            {
                Error(problems, current, "AV001", $"{current.Name} and every containing type must be non-generic partial declarations.");
                valid = false;
            }
        }
        return valid;
    }

    static StringBuilder Open(INamedTypeSymbol symbol, string interfaces = "")
    {
        var text = new StringBuilder("// <auto-generated/>\n#nullable enable\n");
        if (!symbol.ContainingNamespace.IsGlobalNamespace) text.Append("namespace ").Append(symbol.ContainingNamespace.ToDisplayString(NamespaceNames)).AppendLine(";");
        var owners = new Stack<INamedTypeSymbol>();
        for (var current = symbol; current is not null; current = current.ContainingType) owners.Push(current);
        while (owners.Count != 0)
        {
            var current = owners.Pop();
            text.Append(current.DeclaredAccessibility switch
            {
                Accessibility.Public => "public ", Accessibility.Private => "private ", Accessibility.Protected => "protected ",
                Accessibility.ProtectedOrInternal => "protected internal ", Accessibility.ProtectedAndInternal => "private protected ", _ => "internal "
            });
            if (current.IsStatic) text.Append("static ");
            if (current.IsReadOnly) text.Append("readonly ");
            if (current.IsRefLikeType) text.Append("ref ");
            text.Append("partial ");
            if (current.IsRecord) text.Append("record ");
            text.Append(current.TypeKind == TypeKind.Struct ? "struct " : "class ").Append(Escape(current.Name));
            if (SymbolEqualityComparer.Default.Equals(current, symbol)) text.Append(interfaces);
            text.AppendLine("\n{");
        }
        return text;
    }

    static string Close(StringBuilder text, INamedTypeSymbol symbol)
    {
        for (var current = symbol; current is not null; current = current.ContainingType) text.AppendLine("}");
        return text.ToString();
    }

    static string Descriptor(ComponentModel component) => $"global::Archevolt.ComponentDescriptor.Of<{component.Type}>({Literal(component.Name)}, tracked: {Bool(component.Tracked)}, tag: {Bool(component.Tag)})";
    static string Bool(bool value) => value ? "true" : "false";

    static GeneratedUnit GenerateComponent(INamedTypeSymbol symbol, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var problems = new List<Problem>();
        ValidateComponent(symbol, symbol, problems);
        return Unit(symbol, "Component", "", problems);
    }

    static void ValidateComponent(ITypeSymbol type, ISymbol location, List<Problem> problems, bool valueAccess = false)
    {
        if (!type.IsUnmanagedType || type is not INamedTypeSymbol named || named.Arity != 0 || named.IsRefLikeType ||
            type.AllInterfaces.Any(static iface => iface.ToDisplayString() == "System.IDisposable"))
            Error(problems, location, "AV002", $"{type.Name} must be a closed unmanaged, non-owning value type.");
        else if (!Has(type, "ComponentAttribute"))
            Error(problems, location, "AV009", $"{type.Name} must declare [Component].");
        else if (valueAccess && Component(type).Tag)
            Error(problems, location, "AV005", $"Tag {type.Name} has membership only and cannot be accessed as a value.");
    }

    static List<(ITypeSymbol Type, string Path)> BundleMembers(INamedTypeSymbol bundle, List<Problem> problems, HashSet<INamedTypeSymbol>? visiting = null, string path = "value")
    {
        visiting ??= new(SymbolEqualityComparer.Default);
        var result = new List<(ITypeSymbol, string)>();
        if (!visiting.Add(bundle)) { Error(problems, bundle, "AV003", "Bundles cannot contain themselves."); return result; }
        var members = bundle.GetMembers().Where(static member => !member.IsStatic &&
            (member is IFieldSymbol { IsImplicitlyDeclared: false } || member is IPropertySymbol { IsIndexer: false, GetMethod: not null } property && !property.IsImplicitlyDeclared))
            .OrderBy(static member => member.Locations.FirstOrDefault()?.SourceSpan.Start ?? int.MaxValue);
        foreach (var member in members)
        {
            var type = member is IFieldSymbol field ? field.Type : ((IPropertySymbol)member).Type;
            var memberPath = path + "." + Escape(member.Name);
            if (type is INamedTypeSymbol nested && Has(nested, "BundleAttribute")) result.AddRange(BundleMembers(nested, problems, visiting, memberPath));
            else { ValidateComponent(type, member, problems); result.Add((type, memberPath)); }
        }
        visiting.Remove(bundle);
        foreach (var group in result.GroupBy(static member => TypeName(member.Item1), StringComparer.Ordinal).Where(static group => group.Count() > 1))
            Error(problems, bundle, "AV003", $"Bundle {bundle.Name} repeats component {group.Key}.");
        return result;
    }

    static GeneratedUnit GenerateBundle(INamedTypeSymbol symbol, CancellationToken cancellationToken)
    {
        var problems = new List<Problem>();
        ValidatePartial(symbol, problems, cancellationToken);
        if (!symbol.IsUnmanagedType || symbol.IsRefLikeType) Error(problems, symbol, "AV002", "A bundle must be an unmanaged value type.");
        var members = BundleMembers(symbol, problems);
        var text = Open(symbol, " : global::Archevolt.IBundle<" + TypeName(symbol) + ">");
        text.Append("public static global::Archevolt.BundleDescriptor Descriptor { get; } = global::Archevolt.BundleDescriptor.Create<").Append(TypeName(symbol)).Append(">(")
            .Append(string.Join(", ", members.Select(static member => Descriptor(Component(member.Type))))).AppendLine(");");
        text.Append("public static void Write(in ").Append(TypeName(symbol)).AppendLine(" value, ref global::Archevolt.BundleWriter writer)\n{");
        foreach (var member in members) text.Append("writer.Put(").Append(member.Path).AppendLine(");");
        text.AppendLine("}");
        return Unit(symbol, "Bundle", Close(text, symbol), problems);
    }
}
