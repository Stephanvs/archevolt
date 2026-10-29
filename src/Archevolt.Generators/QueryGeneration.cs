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
    static string BlockColumn(TermModel term) => term.Mode == "Read" ? $"block.Read<{term.Component.Type}>({term.Index})"
        : term.Component.Tracked ? $"block.Tracked<{term.Component.Type}>({term.Index}).WriteAll()" : $"block.Write<{term.Component.Type}>({term.Index})";

    static void BindColumns(StringBuilder text, IReadOnlyList<TermModel> terms, string targetPrefix = "", bool declarations = false)
    {
        foreach (var term in terms.Where(static term => term.Mode is "Read" or "Write" or "Optional"))
        {
            var variable = targetPrefix + ColumnName(term);
            if (declarations) text.Append(ColumnType(term)).Append(' ').Append(variable).AppendLine(";");
            if (term.Mode == "Optional") text.Append("block.TryRead<").Append(term.Component.Type).Append(">(").Append(term.Index).Append(", out ").Append(variable).AppendLine(");");
            else text.Append(variable).Append(" = block.").Append(term.Mode == "Read" ? "Read" : term.Component.Tracked ? "Tracked" : "Write")
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
        GenerateEnumerator(text, values, filters);
        GenerateRow(text, values);
        return Unit(symbol, "Query", Close(text, symbol), problems);
    }

    const string Inline = "[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]";
    const string Unsafe = "global::System.Runtime.CompilerServices.Unsafe";
    const string BaseOf = "global::System.Runtime.InteropServices.MemoryMarshal.GetReference";
    static bool Stamped(TermModel term) => term.Mode == "Write" && term.Component.Tracked;

    // The enumerator holds a ref to each column's current element and the rows left in the block. MoveNext advances
    // every ref, then tests the row count, so the per-row path is one block that ends in the loop's branch. With a
    // conditional advance, the JIT chose between two equally weighted layouts and the top-tested one ran about twice
    // as slow. NextBlock calls QueryCursor.Next on a local copy, so an out-of-line Next exposes that copy and not the
    // enumerator, whose fields can then stay in registers whatever the PGO profile.
    static void GenerateEnumerator(StringBuilder text, IReadOnlyList<TermModel> values, IReadOnlyList<TermModel> filters)
    {
        bool window = filters.Count != 0 || values.Any(Stamped);
        text.AppendLine("public ref struct Enumerator\n{\nprivate global::Archevolt.QueryCursor _cursor;\nprivate global::Archevolt.EntityColumn _entities;\nprivate int _count;\nprivate int _remaining;");
        if (window) text.AppendLine("private global::Archevolt.TickWindow _window;");
        var advance = new StringBuilder();
        var clear = new StringBuilder();
        void Ref(string type, string field, string stride)
        {
            text.Append("private ref ").Append(type).Append(' ').Append(field).AppendLine(";");
            advance.Append(field).Append(" = ref ").Append(Unsafe).Append(".Add(ref ").Append(field).Append(", ").Append(stride).AppendLine(");");
            clear.Append(field).Append(" = ref ").Append(Unsafe).Append(".NullRef<").Append(type).AppendLine(">();");
        }
        foreach (var term in values)
        {
            // An absent optional column binds a null ref and a stride of zero, so it stays null without a per-row branch.
            if (term.Mode == "Optional") text.Append("private int _stride").Append(term.Index).AppendLine(";");
            Ref(term.Component.Type, ColumnName(term), term.Mode == "Optional" ? "_stride" + term.Index : "1");
            if (Stamped(term)) Ref("ulong", "_changed" + term.Index, "1");
        }
        for (int i = 0; i < filters.Count; i++) Ref("ulong", "_ticks" + i, "1");
        text.Append("internal Enumerator(global::Archevolt.QueryCursor cursor) { this = default; _cursor = cursor;").Append(window ? " _window = cursor.Window;" : "").AppendLine(" }");
        text.AppendLine(Inline);
        if (filters.Count == 0)
            text.Append("public bool MoveNext()\n{\n").Append(advance).AppendLine("if (_remaining-- != 0) return true;\nreturn NextBlock();\n}");
        else
            text.Append("public bool MoveNext()\n{\nwhile (true)\n{\n").Append(advance).AppendLine("if (_remaining-- == 0 && !NextBlock()) return false;")
                .Append("if (").Append(string.Join(" && ", filters.Select((_, i) => $"_window.Contains(_ticks{i})"))).AppendLine(") return true;\n}\n}");
        // QueryCursor.Next never returns an empty block, and every column is sliced to block.Count rows. The refs step
        // one element past a column's end before NextBlock rebinds them. Columns live in native table slabs, so the GC
        // never sees those refs. Once the cursor is exhausted the refs are nulled, so Current cannot read past the end.
        text.AppendLine(Inline).Append("private bool NextBlock()\n{\nvar cursor = _cursor;\nbool found = cursor.Next(out var block);\n_cursor = cursor;\nif (!found)\n{\n_remaining = 0;\n")
            .Append(clear).AppendLine("return false;\n}\n_count = block.Count;\n_remaining = _count - 1;\n_entities = block.Entities;");
        foreach (var term in values)
        {
            var column = ColumnName(term);
            var type = term.Component.Type;
            if (term.Mode == "Optional")
                text.Append("block.TryRead<").Append(type).Append(">(").Append(term.Index).Append(", out var optional").Append(term.Index).AppendLine(");")
                    .Append(column).Append(" = ref ").Append(BaseOf).Append("(optional").Append(term.Index).AppendLine(");")
                    .Append("_stride").Append(term.Index).Append(" = optional").Append(term.Index).AppendLine(".IsEmpty ? 0 : 1;");
            else if (Stamped(term))
                text.Append("var tracked").Append(term.Index).Append(" = block.Tracked<").Append(type).Append(">(").Append(term.Index).AppendLine(");")
                    .Append(column).Append(" = ref ").Append(BaseOf).Append("(tracked").Append(term.Index).AppendLine(".Values);")
                    .Append("_changed").Append(term.Index).Append(" = ref ").Append(BaseOf).Append("(tracked").Append(term.Index).AppendLine(".Changed);");
            else
                text.Append(column).Append(" = ref ").Append(BaseOf).Append("(block.").Append(term.Mode).Append('<').Append(type).Append(">(").Append(term.Index).AppendLine("));");
        }
        for (int i = 0; i < filters.Count; i++)
            text.Append("_ticks").Append(i).Append(" = ref ").Append(BaseOf).Append("(block.").Append(filters[i].Mode).Append('(').Append(filters[i].Index).AppendLine("));");
        text.AppendLine("return true;\n}");
        var arguments = new List<string>();
        foreach (var term in values)
        {
            arguments.Add("ref " + ColumnName(term));
            if (Stamped(term)) arguments.Add("ref _changed" + term.Index);
        }
        arguments.Add("_entities");
        arguments.Add("_count - 1 - _remaining");
        if (values.Any(Stamped)) arguments.Add("_window.Now");
        text.Append("public Row Current\n{\n").Append(Inline).Append("\nget => new(").Append(string.Join(", ", arguments)).AppendLine(");\n}\n}");
    }

    // A row holds a ref to each column's current element. Entity reads index the entity column, because EntityKey is
    // internal to the runtime.
    static void GenerateRow(StringBuilder text, IReadOnlyList<TermModel> values)
    {
        bool stamped = values.Any(Stamped);
        var parameters = new List<string>();
        text.AppendLine("public readonly ref struct Row\n{");
        foreach (var term in values)
        {
            var reference = term.Mode == "Write" ? "ref " : "ref readonly ";
            text.Append("readonly ").Append(reference).Append(term.Component.Type).Append(" _value").Append(term.Index).AppendLine(";");
            parameters.Add(reference + term.Component.Type + " value" + term.Index);
            if (!Stamped(term)) continue;
            text.Append("readonly ref ulong _changed").Append(term.Index).AppendLine(";");
            parameters.Add("ref ulong changed" + term.Index);
        }
        text.AppendLine("readonly global::Archevolt.EntityColumn _entities;\nreadonly int _row;");
        parameters.Add("global::Archevolt.EntityColumn entities");
        parameters.Add("int row");
        if (stamped)
        {
            text.AppendLine("readonly ulong _now;");
            parameters.Add("ulong now");
        }
        text.Append("internal Row(").Append(string.Join(", ", parameters)).AppendLine(")\n{");
        foreach (var term in values)
        {
            text.Append("_value").Append(term.Index).Append(" = ref value").Append(term.Index).AppendLine(";");
            if (Stamped(term)) text.Append("_changed").Append(term.Index).Append(" = ref changed").Append(term.Index).AppendLine(";");
        }
        text.Append("_entities = entities;\n_row = row;\n").AppendLine(stamped ? "_now = now;\n}" : "}");
        text.AppendLine("public global::Archevolt.Entity Entity => _entities[_row];");
        foreach (var term in values)
        {
            if (term.Mode == "Optional") text.Append("public bool Has").Append(term.Alias).Append(" => !").Append(Unsafe).Append(".IsNullRef(in _value").Append(term.Index).AppendLine(");");
            text.Append("public ref ").Append(term.Mode == "Write" ? "" : "readonly ").Append(term.Component.Type).Append(' ').Append(Escape(term.Alias)).AppendLine("\n{\nget\n{");
            if (term.Mode == "Optional") text.Append("if (!Has").Append(term.Alias).AppendLine(") throw new global::System.InvalidOperationException(\"The optional component is absent.\");");
            if (Stamped(term)) text.Append("_changed").Append(term.Index).AppendLine(" = _now;");
            text.Append("return ref _value").Append(term.Index).AppendLine(";\n}\n}");
        }
        text.AppendLine("}");
    }
}
