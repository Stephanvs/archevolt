using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Archevolt.Generators;

public sealed partial class ArchevoltGenerator
{
    enum SystemForm { System, Condition, Each, EachBlock }

    static readonly (string Attribute, SystemForm Form)[] Forms = [("EcsSystemAttribute", SystemForm.System), ("RunConditionAttribute", SystemForm.Condition), ("EachAttribute", SystemForm.Each), ("EachBlockAttribute", SystemForm.EachBlock)];

    static SystemForm? FormOf(IMethodSymbol method) => Forms.Where(form => Has(method, form.Attribute)).Select(static form => (SystemForm?)form.Form).FirstOrDefault();

    sealed record SystemModel(IMethodSymbol Method, SystemForm Form, List<ParameterModel> Parameters, List<List<TermModel>> Queries,
        List<ComponentModel> Components, List<ComponentModel> Resources, bool Partitionable);

    static SystemModel System(IMethodSymbol method, List<Problem> problems, CancellationToken cancellationToken)
    {
        var form = FormOf(method) ?? SystemForm.System;
        if (method.Locations.Any(static location => location.IsInSource)) ValidatePartial(method.ContainingType, problems, cancellationToken);
        if (!method.IsStatic || (form == SystemForm.Condition ? method.ReturnType.SpecialType != SpecialType.System_Boolean : !method.ReturnsVoid) || method.IsAsync || method.IsGenericMethod || method.ReturnsByRef || method.ReturnsByRefReadonly ||
            method.MethodKind != MethodKind.Ordinary || method.ContainingType.GetMembers(method.Name).OfType<IMethodSymbol>().Count() != 1 ||
            method.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax(cancellationToken).DescendantNodes().OfType<YieldStatementSyntax>().Any()))
            Error(problems, method, "AV005", form == SystemForm.Condition ? "A run condition must be a synchronous, non-generic, non-overloaded static bool method." : "A system must be a synchronous, non-generic, non-overloaded static void method.");
        if (Forms.Count(entry => Has(method, entry.Attribute)) != 1)
            Error(problems, method, "AV005", "Choose exactly one of Each, EachBlock, EcsSystem, or RunCondition.");
        var parameters = new List<ParameterModel>();
        var queries = new List<List<TermModel>>();
        var components = new List<ComponentModel>();
        var resources = new List<ComponentModel>();
        var eachTerms = new List<TermModel>();
        foreach (var parameter in method.Parameters)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var type = parameter.Type;
            string kind;
            int index;
            ComponentModel? component = null;
            if (TypeName(type) == "global::Archevolt.Commands")
            {
                kind = "Commands"; index = 0;
                if (parameters.Any(static parameter => parameter.Kind == "Commands")) Error(problems, parameter, "AV004", "A system receives only one Commands capability.");
                if (parameter.RefKind != RefKind.None || parameter.ScopedKind == ScopedKind.None) Error(problems, parameter, "AV005", "Commands must be passed by scoped value.");
            }
            else if (type is INamedTypeSymbol local && local.ContainingNamespace.ToDisplayString() == "Archevolt" && local.Arity == 1 && local.Name == "Local")
            {
                kind = "Local";
                component = Component(local.TypeArguments[0]);
                index = parameters.Count(static parameter => parameter.Kind == "Local");
                if (parameter.RefKind != RefKind.None || parameter.ScopedKind == ScopedKind.None) Error(problems, parameter, "AV005", "Local parameters must be passed by scoped value.");
            }
            else if (type is INamedTypeSymbol message && message.ContainingNamespace.ToDisplayString() == "Archevolt" && message.Arity == 1 && message.Name is "MessageReader" or "MessageWriter" or "RemovedComponents")
            {
                kind = message.Name;
                component = Component(message.TypeArguments[0]);
                index = parameters.Count(static parameter => parameter.Kind is "MessageReader" or "MessageWriter" or "RemovedComponents");
                if (!message.TypeArguments[0].IsUnmanagedType || (kind != "RemovedComponents" && !Has(message.TypeArguments[0], "MessageAttribute")))
                    Error(problems, parameter, "AV002", "Messages need an unmanaged [Message] value type.");
                if (parameter.RefKind != RefKind.None || parameter.ScopedKind == ScopedKind.None) Error(problems, parameter, "AV005", "Message parameters must be passed by scoped value.");
                if (kind == "RemovedComponents") { ValidateComponent(message.TypeArguments[0], parameter, problems); components.Add(component); }
            }
            else if (type is INamedTypeSymbol wrapper && wrapper.ContainingNamespace.ToDisplayString() == "Archevolt" && wrapper.Arity == 1 && wrapper.Name is "Res" or "ResMut" or "ManagedRes" or "OptionalRes")
            {
                kind = wrapper.Name;
                component = Component(wrapper.TypeArguments[0]);
                if ((kind == "ManagedRes" ? !wrapper.TypeArguments[0].IsReferenceType : !wrapper.TypeArguments[0].IsUnmanagedType) || !Has(wrapper.TypeArguments[0], "ResourceAttribute"))
                    Error(problems, parameter, "AV002", "Resource parameters need an attributed type matching the managed or unmanaged capability.");
                index = resources.Count;
                resources.Add(component);
                if (parameter.RefKind != RefKind.None || parameter.ScopedKind == ScopedKind.None)
                    Error(problems, parameter, "AV005", "Resource capability parameters must be passed by scoped value.");
            }
            else if (form is SystemForm.System or SystemForm.Condition && type is INamedTypeSymbol query && Has(query, "QueryAttribute"))
            {
                kind = "Query";
                index = queries.Count;
                var terms = QueryTerms(query, problems);
                queries.Add(terms);
                components.AddRange(terms.Select(static term => term.Component));
                if (parameter.RefKind != RefKind.None || parameter.ScopedKind == ScopedKind.None)
                    Error(problems, parameter, "AV005", "Query capability parameters must be passed by scoped value.");
            }
            else if (form == SystemForm.Each && TypeName(type) == "global::Archevolt.Entity" && parameter.RefKind == RefKind.None)
            {
                kind = "Entity"; index = -1;
            }
            else if (form == SystemForm.Each && parameter.RefKind is RefKind.Ref or RefKind.In or RefKind.RefReadOnlyParameter)
            {
                kind = "Component";
                ValidateComponent(type, parameter, problems, valueAccess: true);
                component = Component(type);
                index = eachTerms.Count;
                if (eachTerms.Any(term => term.Component.Type == component.Type)) Error(problems, parameter, "AV004", "Each parameters cannot alias the same component.");
                eachTerms.Add(new(component, parameter.RefKind == RefKind.Ref ? "Write" : "Read", index, parameter.Name));
                components.Add(component);
                if (parameter.ScopedKind == ScopedKind.None) Error(problems, parameter, "AV005", "Each component refs must be scoped.");
            }
            else if (form == SystemForm.EachBlock && type is INamedTypeSymbol span && span.ContainingNamespace.ToDisplayString() == "System" && span.Arity == 1 && span.Name is "Span" or "ReadOnlySpan")
            {
                kind = "Column";
                ValidateComponent(span.TypeArguments[0], parameter, problems, valueAccess: true);
                component = Component(span.TypeArguments[0]);
                index = eachTerms.Count;
                if (eachTerms.Any(term => term.Component.Type == component.Type)) Error(problems, parameter, "AV004", "EachBlock parameters cannot alias the same component.");
                eachTerms.Add(new(component, span.Name == "Span" ? "Write" : "Read", index, parameter.Name));
                components.Add(component);
                if (parameter.RefKind != RefKind.None || parameter.ScopedKind == ScopedKind.None)
                    Error(problems, parameter, "AV005", "EachBlock component parameters must be scoped Span<T> to write or scoped ReadOnlySpan<T> to read.");
            }
            else if (form == SystemForm.EachBlock && TypeName(type) == "global::Archevolt.EntityColumn")
            {
                kind = "Entities"; index = -1;
                if (parameter.RefKind != RefKind.None || parameter.ScopedKind == ScopedKind.None) Error(problems, parameter, "AV005", "EntityColumn must be passed by scoped value.");
            }
            else
            {
                Error(problems, parameter, "AV005", $"Unsupported system parameter {parameter.Name}; use a scoped query, resource, or Each component ref.");
                kind = "Unsupported"; index = -1;
            }
            parameters.Add(new(TypeName(type), Escape(parameter.Name), kind, parameter.RefKind == RefKind.Ref ? "ref" : "in", index, component));
        }
        if (form is SystemForm.Each or SystemForm.EachBlock)
        {
            var terms = QueryTerms(method, problems);
            var indexes = eachTerms.GroupBy(static term => term.Component.Type).ToDictionary(static group => group.Key, static group => group.First().Index);
            foreach (var term in terms)
            {
                int index = -1;
                if (term.Mode is not ("With" or "Without") && !indexes.TryGetValue(term.Component.Type, out index)) { index = indexes.Count; indexes.Add(term.Component.Type, index); }
                eachTerms.Add(term with { Index = index });
            }
            ValidateRequirements(method, eachTerms, problems);
            queries.Add(eachTerms);
            components.AddRange(terms.Select(static term => term.Component));
            if (form == SystemForm.EachBlock)
            {
                foreach (var term in terms.Where(static term => term.Mode is "Added" or "Changed"))
                    Error(problems, method, "AV011", $"EachBlock receives whole blocks and cannot filter rows by {term.Mode}<{term.Component.Name}>; use [Each] until block change summaries land.");
                if (!parameters.Any(static parameter => parameter.Kind == "Column")) Error(problems, method, "AV005", "EachBlock requires at least one component span.");
            }
            else if (!parameters.Any(static parameter => parameter.Kind == "Component")) Error(problems, method, "AV005", "Each requires at least one component ref.");
        }
        for (int left = 0; left < queries.Count; left++)
            for (int right = left + 1; right < queries.Count; right++)
                if (Conflicts(queries[left], queries[right])) Error(problems, method, "AV004", "Mutable query accesses overlap without a With/Without disjointness proof.");
        foreach (var group in parameters.Where(static parameter => parameter.Kind is "Res" or "ResMut" or "ManagedRes" or "OptionalRes").GroupBy(static parameter => parameter.Component!.Type))
            if (group.Count() > 1 && group.Any(static parameter => parameter.Kind is "ResMut" or "ManagedRes")) Error(problems, method, "AV004", "Mutable resource parameters alias the same resource.");
        foreach (var group in parameters.Where(static parameter => parameter.Kind is "MessageReader" or "MessageWriter").GroupBy(static parameter => parameter.Component!.Type))
            if (group.Count() > 1 && group.Any(static parameter => parameter.Kind == "MessageWriter")) Error(problems, method, "AV004", "Message writer parameters alias the same message channel.");
        if (form == SystemForm.Condition && (queries.Any(static query => query.Any(static term => term.Mode == "Write")) || parameters.Any(static parameter => parameter.Kind is "ResMut" or "ManagedRes" or "MessageWriter" or "Commands")))
            Error(problems, method, "AV010", "Run conditions may read world capabilities and mutate only their instance-local state.");
        bool partitionRequested = form is SystemForm.Each or SystemForm.EachBlock && Attributes(method, form == SystemForm.Each ? "EachAttribute" : "EachBlockAttribute").Any(attribute => NamedBool(attribute, "Partitioned"));
        bool partitionSafe = parameters.All(static parameter => parameter.Kind is "Component" or "Entity" or "Column" or "Entities" or "Res" or "OptionalRes" && parameter.Component?.OwnerThread != true);
        if (partitionRequested && !partitionSafe)
            Error(problems, method, "AV010", form == SystemForm.EachBlock ? "Partitioned EachBlock permits only component spans, EntityColumn, and readonly unmanaged resources without owner-thread affinity."
                : "Partitioned Each permits only component refs, Entity, and readonly unmanaged resources without owner-thread affinity.");
        return new(method, form, parameters, queries, components, resources, partitionRequested && partitionSafe);
    }

    static bool Conflicts(IReadOnlyList<TermModel> left, IReadOnlyList<TermModel> right)
    {
        static bool Required(TermModel term) => term.Mode is "Read" or "Write" or "With" or "Added" or "Changed";
        if (left.Any(term => Required(term) && right.Any(other => other.Mode == "Without" && other.Component.Type == term.Component.Type)) ||
            right.Any(term => Required(term) && left.Any(other => other.Mode == "Without" && other.Component.Type == term.Component.Type))) return false;
        return left.Any(term => term.Mode is "Read" or "Write" or "Optional" && right.Any(other => other.Component.Type == term.Component.Type &&
            other.Mode is "Read" or "Write" or "Optional" && (term.Mode == "Write" || other.Mode == "Write")));
    }

    static string SystemManifest(SystemModel model) => "condition|" + (model.Form == SystemForm.Condition) + "\ncommands|" + model.Parameters.Any(static parameter => parameter.Kind == "Commands") + "\n" + string.Join("\n", model.Queries.SelectMany((query, index) => query.Select(term =>
        $"query|{index}|{term.Mode}|{term.Component.Type}"))
        .Concat(model.Parameters.Where(static parameter => parameter.Kind is "Res" or "ResMut" or "ManagedRes" or "OptionalRes" or "Local" or "MessageReader" or "MessageWriter" or "RemovedComponents")
            .Select(static parameter => $"parameter|{parameter.Kind}|{parameter.Component!.Type}|{parameter.Component.Managed}|{parameter.Component.OwnerThread}")));

    static GeneratedUnit GenerateSystem(IMethodSymbol method, CancellationToken cancellationToken)
    {
        var problems = new List<Problem>();
        var model = System(method, problems, cancellationToken);
        var text = Open(method.ContainingType);
        text.Append("public const string ").Append(Escape("__Archevolt_" + method.Name + "_Access")).Append(" = ").Append(Literal(SystemManifest(model))).AppendLine(";");
        var bodyName = "__Archevolt_" + method.Name + "Body";
        text.Append("private sealed class ").Append(bodyName).AppendLine(model.Form == SystemForm.Condition ? " : global::Archevolt.ConditionBody\n{" : " : global::Archevolt.SystemBody\n{");
        for (int index = 0; index < model.Queries.Count; index++) text.Append("readonly global::Archevolt.QueryState _query").Append(index).AppendLine(";");
        if (model.Parameters.Any(static parameter => parameter.Kind == "Commands")) text.AppendLine("readonly global::Archevolt.CommandsHandle _commands;");
        for (int index = 0; index < model.Resources.Count; index++) text.Append(model.Resources[index].Managed ? "readonly global::Archevolt.ManagedResourceHandle<" : "readonly global::Archevolt.ResourceHandle<").Append(model.Resources[index].Type).Append("> _resource").Append(index).AppendLine(";");
        foreach (var parameter in model.Parameters.Where(static parameter => parameter.Kind == "Local")) text.Append("readonly global::Archevolt.LocalHandle<").Append(parameter.Component!.Type).Append("> _local").Append(parameter.Index).AppendLine(";");
        foreach (var parameter in model.Parameters.Where(static parameter => parameter.Kind is "MessageReader" or "MessageWriter" or "RemovedComponents"))
            text.Append(parameter.Kind == "RemovedComponents" ? "readonly global::Archevolt.RemovedHandle<" : "readonly global::Archevolt.MessageHandle<").Append(parameter.Component!.Type).Append("> _message").Append(parameter.Index).AppendLine(";");
        text.Append("internal ").Append(bodyName).AppendLine("(global::Archevolt.SystemBinder binder)\n{");
        for (int index = 0; index < model.Queries.Count; index++) text.Append("_query").Append(index).Append(" = binder.Query(").Append(index).AppendLine(");");
        if (model.Parameters.Any(static parameter => parameter.Kind == "Commands")) text.AppendLine("_commands = binder.Commands();");
        for (int index = 0; index < model.Resources.Count; index++) text.Append("_resource").Append(index).Append(model.Resources[index].Managed ? " = binder.Managed<" : " = binder.Resource<").Append(model.Resources[index].Type).Append(">(").Append(index).AppendLine(");");
        foreach (var parameter in model.Parameters.Where(static parameter => parameter.Kind == "Local")) text.Append("_local").Append(parameter.Index).Append(" = binder.Local<").Append(parameter.Component!.Type).Append(">(").Append(parameter.Index).AppendLine(");");
        foreach (var parameter in model.Parameters.Where(static parameter => parameter.Kind is "MessageReader" or "MessageWriter" or "RemovedComponents"))
            text.Append("_message").Append(parameter.Index).Append(parameter.Kind == "RemovedComponents" ? " = binder.Removed<" : " = binder.Message<").Append(parameter.Component!.Type).Append(">(").Append(parameter.Index).AppendLine(");");
        text.AppendLine(model.Form == SystemForm.Condition ? "}\nprotected override bool Evaluate(global::Archevolt.SystemRun run)\n{" : "}\nprotected override void Run(global::Archevolt.SystemRun run)\n{");
        foreach (var parameter in model.Parameters.Where(static parameter => parameter.Kind is "Res" or "ResMut" or "ManagedRes" or "OptionalRes"))
            text.Append("var ").Append(parameter.Name).Append(" = _resource").Append(parameter.Index).Append(parameter.Kind == "ResMut" ? ".Write(run);" : parameter.Kind == "OptionalRes" ? ".ReadOptional(run);" : ".Read(run);").AppendLine();
        foreach (var parameter in model.Parameters.Where(static parameter => parameter.Kind == "Local")) text.Append("var ").Append(parameter.Name).Append(" = _local").Append(parameter.Index).AppendLine(".Get(run);");
        foreach (var parameter in model.Parameters.Where(static parameter => parameter.Kind == "Commands")) text.Append("var ").Append(parameter.Name).AppendLine(" = _commands.Open(run);");
        foreach (var parameter in model.Parameters.Where(static parameter => parameter.Kind is "MessageReader" or "MessageWriter" or "RemovedComponents"))
            text.Append("var ").Append(parameter.Name).Append(" = _message").Append(parameter.Index).Append(parameter.Kind == "MessageWriter" ? ".Write(run);" : ".Read(run);").AppendLine();
        if (model.Form == SystemForm.Each)
        {
            text.AppendLine("var cursor = _query0.Begin(run);\nwhile (cursor.Next(out var chunk))\n{");
            BindColumns(text, model.Queries[0], declarations: true);
            var filters = model.Queries[0].Where(static term => term.Mode is "Added" or "Changed").ToArray();
            for (int index = 0; index < filters.Length; index++) text.Append("var ticks").Append(index).Append(" = chunk.").Append(filters[index].Mode).Append('(').Append(filters[index].Index).AppendLine(");");
            if (model.Parameters.Any(static parameter => parameter.Kind == "Entity")) text.AppendLine("var entities = chunk.Entities;");
            text.AppendLine("int count = chunk.Count;\nfor (int row = 0; row < count; row++)\n{");
            if (filters.Length != 0) text.Append("if (!(").Append(string.Join(" && ", filters.Select((_, index) => $"cursor.Window.Contains(ticks{index}[row])"))).AppendLine(")) continue;");
            text.Append(Escape(method.Name)).Append('(').Append(string.Join(", ", model.Parameters.Select(parameter => parameter.Kind switch
            {
                "Component" => parameter.RefKind + " " + Element(model.Queries[0].First(term => term.Index == parameter.Index), "row"),
                "Entity" => "entities[row]", _ => parameter.Name
            }))).AppendLine(");\n}\n}");
        }
        else if (model.Form == SystemForm.EachBlock)
        {
            text.AppendLine("var cursor = _query0.Begin(run);\nwhile (cursor.Next(out var block))\n{");
            text.Append(Escape(method.Name)).Append('(').Append(string.Join(", ", model.Parameters.Select(parameter => parameter.Kind switch
            {
                "Column" => BlockColumn(model.Queries[0].First(term => term.Index == parameter.Index)),
                "Entities" => "block.Entities", _ => parameter.Name
            }))).AppendLine(");\n}");
        }
        else
        {
            foreach (var parameter in model.Parameters.Where(static parameter => parameter.Kind == "Query"))
                text.Append("var ").Append(parameter.Name).Append(" = new ").Append(parameter.Type).Append("(_query").Append(parameter.Index).AppendLine(", run);");
            if (model.Form == SystemForm.Condition) text.Append("return ");
            text.Append(Escape(method.Name)).Append('(').Append(string.Join(", ", model.Parameters.Select(static parameter => parameter.Name))).AppendLine(");");
        }
        text.AppendLine("}\n}");
        text.Append("public static global::Archevolt.SystemDescriptor ").Append(Escape(method.Name + "_Descriptor")).Append(" { get; } = global::Archevolt.SystemDescriptor.Create(")
            .Append(Literal(method.ContainingType.ToDisplayString() + "." + method.Name)).Append(", static binder => new ").Append(bodyName).AppendLine("(binder))");
        for (int index = 0; index < model.Queries.Count; index++)
        {
            text.Append(model.Form is SystemForm.Each or SystemForm.EachBlock ? ".EachQuery(" : ".Query(").Append(Shape(method.ToDisplayString() + "." + index, model.Queries[index]));
            if (model.Form is SystemForm.Each or SystemForm.EachBlock) text.Append(", partitionable: ").Append(Bool(model.Partitionable));
            text.AppendLine(")");
        }
        foreach (var parameter in model.Parameters.Where(static parameter => parameter.Kind is "Res" or "ResMut" or "ManagedRes" or "OptionalRes"))
        {
            text.Append(parameter.Kind == "ManagedRes" ? ".Managed<" : ".Res<").Append(parameter.Component!.Type).Append(">(");
            if (parameter.Kind != "ManagedRes") text.Append("write: ").Append(Bool(parameter.Kind == "ResMut"));
            if (parameter.Kind == "OptionalRes") text.Append(", optional: true");
            text.AppendLine(")");
        }
        foreach (var parameter in model.Parameters.Where(static parameter => parameter.Kind == "Local")) text.Append(".Local<").Append(parameter.Component!.Type).AppendLine(">()");
        if (model.Parameters.Any(static parameter => parameter.Kind == "Commands")) text.AppendLine(".Commands()");
        foreach (var parameter in model.Parameters.Where(static parameter => parameter.Kind is "MessageReader" or "MessageWriter" or "RemovedComponents"))
            text.Append('.').Append(parameter.Kind == "RemovedComponents" ? "Removed" : parameter.Kind).Append('<').Append(parameter.Component!.Type).AppendLine(">()");
        if (model.Form == SystemForm.Condition) text.AppendLine(".AsCondition()");
        text.AppendLine(".Build();");
        return Unit(method, model.Form switch { SystemForm.Each => "Each", SystemForm.EachBlock => "EachBlock", _ => "System" }, Close(text, method.ContainingType), problems);
    }
}
