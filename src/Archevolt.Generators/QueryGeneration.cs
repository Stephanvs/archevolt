using System.Text;
using Microsoft.CodeAnalysis;

namespace Archevolt.Generators;

public sealed partial class ArchevoltGenerator
{
    static List<TermModel> QueryTerms(ISymbol symbol, List<Problem> problems)
    {
        var terms = new List<TermModel>();
        var indexes = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var attribute in symbol.GetAttributes())
        {
            var mode = AttributeName(attribute).Split('<')[0].Replace("Attribute", "");
            if (mode is not ("Read" or "Write" or "Optional" or "With" or "Without" or "Added" or "Changed") ||
                attribute.AttributeClass?.ContainingNamespace.ToDisplayString() != "Archevolt" || attribute.AttributeClass.TypeArguments.Length != 1) continue;
            var type = attribute.AttributeClass.TypeArguments[0];
            ValidateComponent(type, symbol, problems, mode is "Read" or "Write" or "Optional");
            var component = Component(type);
            if (mode is "Added" or "Changed" && !component.Tracked)
                Error(problems, symbol, "AV006", $"{type.Name} needs [TrackChanges] for a {mode} filter.");
            int index = -1;
            if (mode is not ("With" or "Without") && !indexes.TryGetValue(component.Type, out index)) { index = indexes.Count; indexes.Add(component.Type, index); }
            var alias = NamedString(attribute, "Name") ?? type.Name;
            if (!Microsoft.CodeAnalysis.CSharp.SyntaxFacts.IsValidIdentifier(alias) && !Microsoft.CodeAnalysis.CSharp.SyntaxFacts.IsValidIdentifier("@" + alias))
                Error(problems, symbol, "AV001", $"{alias} is not a valid query member name.");
            terms.Add(new(component, mode, index, alias));
        }
        foreach (var group in terms.Where(static term => term.Mode is "Read" or "Write" or "Optional").GroupBy(static term => term.Component.Type))
            if (group.Count() > 1) Error(problems, symbol, "AV003", $"Query repeats component {group.Key}.");
        foreach (var group in terms.Where(static term => term.Mode is "Read" or "Write" or "Optional").GroupBy(static term => term.Alias))
            if (group.Count() > 1 || group.Key is "Entity") Error(problems, symbol, "AV003", $"Query member {group.Key} needs a unique Name alias.");
        ValidateRequirements(symbol, terms, problems);
        return terms;
    }

    static void ValidateRequirements(ISymbol symbol, IReadOnlyList<TermModel> terms, List<Problem> problems)
    {
        foreach (var excluded in terms.Where(static term => term.Mode == "Without"))
            if (terms.Any(term => term.Component.Type == excluded.Component.Type && term.Mode is not ("Without" or "Optional")))
                Error(problems, symbol, "AV003", $"Query both requires and excludes {excluded.Component.Name}.");
    }

    static string Shape(string name, IEnumerable<TermModel> terms)
    {
        var text = new StringBuilder("global::Archevolt.QueryShape.Create(" + Literal(name) + ")");
        foreach (var term in terms)
        {
            text.Append('.').Append(term.Mode).Append('<').Append(term.Component.Type).Append('>').Append('(');
            if (term.Mode is not ("With" or "Without")) text.Append(term.Index);
            text.Append(')');
        }
        return text.Append(".Build()").ToString();
    }

    static string ColumnType(TermModel term) => term.Mode == "Write"
        ? term.Component.Tracked ? $"global::Archevolt.TrackedColumn<{term.Component.Type}>" : $"global::System.Span<{term.Component.Type}>"
        : $"global::System.ReadOnlySpan<{term.Component.Type}>";
    static string ColumnName(TermModel term) => "_column" + term.Index;
    static string Element(TermModel term, string row) => term.Mode == "Write" && term.Component.Tracked
        ? $"{ColumnName(term)}.Write({row})" : $"{ColumnName(term)}[{row}]";

    static void BindColumns(StringBuilder text, IReadOnlyList<TermModel> terms, string targetPrefix = "", string chunk = "chunk", bool declarations = false)
    {
        foreach (var term in terms.Where(static term => term.Mode is "Read" or "Write" or "Optional"))
        {
            var variable = targetPrefix + ColumnName(term);
            if (declarations) text.Append(ColumnType(term)).Append(' ').Append(variable).AppendLine(";");
            if (term.Mode == "Optional") text.Append(chunk).Append(".TryRead<").Append(term.Component.Type).Append(">(").Append(term.Index).Append(", out ").Append(variable).AppendLine(");");
            else text.Append(variable).Append(" = ").Append(chunk).Append('.').Append(term.Mode == "Read" ? "Read" : term.Component.Tracked ? "Tracked" : "Write")
                .Append('<').Append(term.Component.Type).Append(">(").Append(term.Index).AppendLine(");");
        }
    }

    static GeneratedUnit GenerateQuery(INamedTypeSymbol symbol, CancellationToken cancellationToken)
    {
        var problems = new List<Problem>();
        ValidatePartial(symbol, problems, cancellationToken);
        if (!symbol.IsRefLikeType || !symbol.IsReadOnly) Error(problems, symbol, "AV001", "A query must be a readonly ref partial struct.");
        var terms = QueryTerms(symbol, problems);
        bool publiclyVisible = true;
        for (var owner = symbol; owner is not null; owner = owner.ContainingType) publiclyVisible &= owner.DeclaredAccessibility == Accessibility.Public;
        if (publiclyVisible)
            foreach (var attribute in symbol.GetAttributes())
                if (attribute.AttributeClass?.TypeArguments.FirstOrDefault() is INamedTypeSymbol type && attribute.AttributeClass.ContainingNamespace.ToDisplayString() == "Archevolt")
                    for (var current = type; current is not null; current = current.ContainingType)
                        if (current.DeclaredAccessibility != Accessibility.Public) { Error(problems, symbol, "AV001", $"Public query {symbol.Name} cannot expose inaccessible component {type.Name}."); break; }
        var values = terms.Where(static term => term.Mode is "Read" or "Write" or "Optional").ToArray();
        var filters = terms.Where(static term => term.Mode is "Added" or "Changed").ToArray();
        var text = Open(symbol);
        text.Append("public static global::Archevolt.QueryShape Shape { get; } = ").Append(Shape(symbol.ToDisplayString(), terms)).AppendLine(";");
        text.AppendLine("readonly global::Archevolt.QueryState? _state;\nreadonly global::Archevolt.SystemRun _run;");
        text.Append("internal ").Append(Escape(symbol.Name)).AppendLine("(global::Archevolt.QueryState state, global::Archevolt.SystemRun run) { _state = state; _run = run; }");
        text.AppendLine("public Enumerator GetEnumerator() => new((_state ?? throw new global::System.InvalidOperationException(\"A default query has no active execution lease.\")).Begin(_run));");
        text.AppendLine("public ref struct Enumerator\n{\nprivate global::Archevolt.QueryCursor _cursor;\nprivate int _row;\nprivate int _count;\nprivate global::Archevolt.EntityColumn _entities;");
        foreach (var term in values) text.Append("private ").Append(ColumnType(term)).Append(' ').Append(ColumnName(term)).AppendLine(";");
        for (int i = 0; i < filters.Length; i++) text.Append("private global::System.ReadOnlySpan<ulong> _ticks").Append(i).AppendLine(";");
        text.AppendLine("internal Enumerator(global::Archevolt.QueryCursor cursor) { this = default; _cursor = cursor; _row = -1; }");
        text.AppendLine("public bool MoveNext()\n{\nwhile (true)\n{\nif (++_row < _count)\n{");
        if (filters.Length != 0)
            text.Append("if (!(").Append(string.Join(" && ", filters.Select((_, i) => $"_cursor.Window.Contains(_ticks{i}[_row])"))).AppendLine(")) continue;");
        text.AppendLine("return true;\n}\nif (!_cursor.Next(out var chunk)) return false;\n_count = chunk.Count;\n_row = -1;\n_entities = chunk.Entities;");
        BindColumns(text, terms);
        for (int i = 0; i < filters.Length; i++) text.Append("_ticks").Append(i).Append(" = chunk.").Append(filters[i].Mode).Append('(').Append(filters[i].Index).AppendLine(");");
        text.AppendLine("}\n}");
        text.Append("public Row Current => new(").Append(string.Join(", ", values.Select(ColumnName).Concat(new[] { "_entities", "_row" }))).AppendLine(");\n}");
        text.AppendLine("public readonly ref struct Row\n{\nreadonly global::Archevolt.EntityColumn _entities;\nreadonly int _row;");
        foreach (var term in values) text.Append("readonly ").Append(ColumnType(term)).Append(' ').Append(ColumnName(term)).AppendLine(";");
        text.Append("internal Row(").Append(string.Join(", ", values.Select(term => ColumnType(term) + " " + ColumnName(term))
            .Concat(new[] { "global::Archevolt.EntityColumn entities", "int row" }))).AppendLine(")\n{\n_entities = entities; _row = row;");
        foreach (var term in values) text.Append("this.").Append(ColumnName(term)).Append(" = ").Append(ColumnName(term)).AppendLine(";");
        text.AppendLine("}\npublic global::Archevolt.Entity Entity => _entities[_row];");
        foreach (var term in values)
        {
            if (term.Mode == "Optional") text.Append("public bool Has").Append(term.Alias).Append(" => !").Append(ColumnName(term)).AppendLine(".IsEmpty;");
            text.Append("public ref ").Append(term.Mode == "Write" ? "" : "readonly ").Append(term.Component.Type).Append(' ').Append(Escape(term.Alias)).AppendLine("\n{\nget\n{");
            if (term.Mode == "Optional") text.Append("if (!Has").Append(term.Alias).AppendLine(") throw new global::System.InvalidOperationException(\"The optional component is absent.\");");
            text.Append("return ref ").Append(Element(term, "_row")).AppendLine(";\n}\n}");
        }
        text.AppendLine("}");
        return Unit(symbol, "Query", Close(text, symbol), problems);
    }
}
