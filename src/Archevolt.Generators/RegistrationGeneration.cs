using System.Text;
using Microsoft.CodeAnalysis;
using Archevolt.Scheduling;

namespace Archevolt.Generators;

public sealed partial class ArchevoltGenerator
{
    static IEnumerable<IMethodSymbol> Systems(INamedTypeSymbol owner) => owner.GetMembers().OfType<IMethodSymbol>()
        .Where(static method => FormOf(method) is not null).OrderBy(static method => method.Name, StringComparer.Ordinal);

    static GeneratedUnit GenerateModule(INamedTypeSymbol symbol, CancellationToken cancellationToken)
    {
        var problems = new List<Problem>();
        ValidatePartial(symbol, problems, cancellationToken);
        ValidateModuleGraph(symbol, symbol, new(SymbolEqualityComparer.Default), new(SymbolEqualityComparer.Default), problems);
        var components = new Dictionary<string, ComponentModel>(StringComparer.Ordinal);
        var resources = new Dictionary<string, ComponentModel>(StringComparer.Ordinal);
        var bundles = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
        var systems = new Dictionary<string, IMethodSymbol>(StringComparer.Ordinal);
        var messages = new Dictionary<string, ComponentModel>(StringComparer.Ordinal);
        var includedModules = new List<INamedTypeSymbol>();
        foreach (var attribute in symbol.GetAttributes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsAttribute(attribute, "IncludeModuleAttribute") && attribute.ConstructorArguments.FirstOrDefault().Value is INamedTypeSymbol includedModule)
            {
                if (!Has(includedModule, "EcsModuleAttribute")) Error(problems, symbol, "AV009", "An included module must declare [EcsModule].");
                includedModules.Add(includedModule);
            }
            else if (IsAttribute(attribute, "IncludeSystemsAttribute") && attribute.ConstructorArguments.FirstOrDefault().Value is INamedTypeSymbol owner)
            {
                foreach (var method in Systems(owner))
                {
                    var model = System(method, problems, cancellationToken);
                    systems[TypeName(owner) + "." + method.Name] = method;
                    foreach (var component in model.Components) components[component.Type] = component;
                    foreach (var resource in model.Resources) resources[resource.Type] = resource;
                    foreach (var parameter in model.Parameters.Where(static parameter => parameter.Kind is "MessageReader" or "MessageWriter")) messages[parameter.Component!.Type] = parameter.Component;
                }
            }
            else if (attribute.AttributeClass?.TypeArguments.FirstOrDefault() is INamedTypeSymbol type)
            {
                if (IsAttribute(attribute, "IncludeBundleAttribute"))
                {
                    if (!Has(type, "BundleAttribute")) Error(problems, symbol, "AV009", $"{type.Name} is not an attributed bundle.");
                    bundles[TypeName(type)] = type;
                    foreach (var member in BundleMembers(type, problems)) { var component = Component(member.Type); components[component.Type] = component; }
                }
                else if (IsAttribute(attribute, "IncludeComponentAttribute")) { ValidateComponent(type, symbol, problems); var component = Component(type); components[component.Type] = component; }
                else if (IsAttribute(attribute, "IncludeResourceAttribute"))
                {
                    if ((!type.IsUnmanagedType && !type.IsReferenceType) || !Has(type, "ResourceAttribute")) Error(problems, symbol, "AV002", "Included resources must declare [Resource] and use a supported value or managed class.");
                    var resource = Component(type); resources[resource.Type] = resource;
                }
                else if (IsAttribute(attribute, "IncludeMessageAttribute"))
                {
                    if (!type.IsUnmanagedType || !Has(type, "MessageAttribute")) Error(problems, symbol, "AV002", "Included messages must be unmanaged [Message] values.");
                    messages[TypeName(type)] = Component(type);
                }
                else if (IsAttribute(attribute, "IncludeModuleAttribute"))
                {
                    if (!Has(type, "EcsModuleAttribute")) Error(problems, symbol, "AV009", "An included module must declare [EcsModule].");
                    if (SymbolEqualityComparer.Default.Equals(type, symbol)) Error(problems, symbol, "AV009", "A module cannot include itself.");
                    includedModules.Add(type);
                }
            }
        }
        var text = Open(symbol);
        var manifest = "1\n" + string.Join("\n", components.Values.OrderBy(static component => component.Type, StringComparer.Ordinal).Select(static component =>
            $"component|{component.Type}|{component.Tracked}|{component.Tag}").Concat(systems.Values.Select(method => "system|" + TypeName(method.ContainingType) + "." + method.Name + "_Descriptor\n" +
            SystemManifest(System(method, problems, cancellationToken))))
            .Concat(resources.Values.Select(static resource => $"resource|{resource.Type}|{resource.Tracked}|{resource.OwnerThread}|{resource.Managed}")));
        text.Append("public const int ArchevoltManifestAbi = 1;\npublic const string ArchevoltManifest = ").Append(Literal(manifest)).AppendLine(";");
        text.Append("public static global::Archevolt.ModuleDescriptor Module { get; } = new global::Archevolt.ModuleBuilder(").Append(Literal(symbol.ToDisplayString())).AppendLine(", abiVersion: 1)");
        foreach (var component in components.Values.OrderBy(static component => component.Type, StringComparer.Ordinal))
            text.Append(component.Tag ? ".Tag<" : ".Component<").Append(component.Type).Append(">(").Append(Literal(component.Name)).Append(", tracked: ").Append(Bool(component.Tracked)).AppendLine(")");
        foreach (var resource in resources.Values.OrderBy(static component => component.Type, StringComparer.Ordinal))
        {
            text.Append(resource.Managed ? ".ManagedResource<" : ".Resource<").Append(resource.Type).Append(">(").Append(Literal(resource.Name)).Append(", tracked: ").Append(Bool(resource.Tracked));
            if (!resource.Managed) text.Append(", ownerThread: ").Append(Bool(resource.OwnerThread));
            text.AppendLine(")");
        }
        foreach (var message in messages.Values) text.Append(".Message<").Append(message.Type).AppendLine(">()");
        foreach (var module in includedModules) text.Append(".Include(").Append(TypeName(module)).AppendLine(".Module)");
        foreach (var bundle in bundles) text.Append(".Bundle<").Append(bundle.Key).Append(">(").Append(Literal(bundle.Value.ToDisplayString())).AppendLine(")");
        foreach (var system in systems) text.Append(".System(").Append(TypeName(system.Value.ContainingType)).Append('.').Append(Escape(system.Value.Name + "_Descriptor")).AppendLine(")");
        text.AppendLine(".Build();");
        var source = Close(text, symbol);
        var metadata = "[assembly: global::System.Reflection.AssemblyMetadata(" + Literal("Archevolt.Manifest." + symbol.ToDisplayString()) + ", " + Literal(manifest) + ")]\n";
        var namespaceIndex = source.IndexOf("namespace ", StringComparison.Ordinal);
        if (namespaceIndex >= 0) source = source.Insert(namespaceIndex, metadata);
        else source = source.Insert(source.IndexOf('\n', source.IndexOf("#nullable", StringComparison.Ordinal)) + 1, metadata);
        return Unit(symbol, "Module", source, problems);
    }

    sealed record RunModel(INamedTypeSymbol Owner, string Method, string Key, int Order, AttributeData Attribute);

    static void ValidateModuleGraph(INamedTypeSymbol root, INamedTypeSymbol current, HashSet<INamedTypeSymbol> active, HashSet<INamedTypeSymbol> visited, List<Problem> problems)
    {
        if (!active.Add(current)) { Error(problems, root, "AV009", "Module includes contain a cycle."); return; }
        if (visited.Add(current))
            foreach (var attribute in Attributes(current, "IncludeModuleAttribute"))
                if ((attribute.AttributeClass?.TypeArguments.FirstOrDefault() ?? attribute.ConstructorArguments.FirstOrDefault().Value as ITypeSymbol) is INamedTypeSymbol included)
                {
                    if (included.DeclaringSyntaxReferences.Length == 0 && included.GetMembers("ArchevoltManifestAbi").OfType<IFieldSymbol>().FirstOrDefault()?.ConstantValue is not 1)
                        Error(problems, root, "AV009", $"Imported module {included.Name} has no compatible generated manifest.");
                    ValidateModuleGraph(root, included, active, visited, problems);
                }
        active.Remove(current);
    }

    static SystemAccess RunAccess(RunModel run, Dictionary<string, int> identities, List<Problem> problems, CancellationToken cancellationToken)
    {
        int Id(string identity)
        {
            if (!identities.TryGetValue(identity, out var id)) { id = identities.Count; identities.Add(identity, id); }
            return id;
        }
        var method = Systems(run.Owner).FirstOrDefault(method => method.Name == run.Method);
        var manifest = method is not null ? SystemManifest(System(method, problems, cancellationToken)) :
            run.Owner.GetMembers("__Archevolt_" + run.Method + "_Access").OfType<IFieldSymbol>().FirstOrDefault()?.ConstantValue as string ?? "";
        if (method is null && manifest.Length == 0) Error(problems, run.Owner, "AV009", $"Imported system {run.Owner.Name}.{run.Method} has no generated access manifest.");
        var lines = manifest.Split('\n').Select(static line => line.Split('|')).ToArray();
        var clauses = new List<AccessClause>();
        foreach (var group in lines.Where(static line => line.Length == 4 && line[0] == "query").GroupBy(static line => line[1]))
        {
            int[] Select(params string[] modes) => group.Where(line => modes.Contains(line[2])).Select(line => Id(line[3])).ToArray();
            clauses.Add(new(Select("Read", "Optional"), Select("Write"), Select("Read", "Write", "With", "Added", "Changed"), Select("Without"), Select("Added", "Changed")));
        }
        var parameters = lines.Where(static line => line.Length >= 3 && line[0] == "parameter").ToArray();
        var traits = parameters.Any(static line => line[1] == "ManagedRes" || line[1] == "Local" && line[3] == "True") ? NodeTraits.OwnerThread | NodeTraits.Exclusive : NodeTraits.None;
        if (parameters.Any(static line => line.Length > 4 && line[4] == "True")) traits |= NodeTraits.OwnerThread;
        if (lines.Any(static line => line.Length == 2 && line[0] == "commands" && line[1] == "True")) traits |= NodeTraits.EmitsCommands;
        return new(run.Key, clauses,
            resourcesRead: parameters.Where(static line => line[1] is "Res" or "OptionalRes").Select(line => Id(line[2])),
            resourcesWritten: parameters.Where(static line => line[1] is "ResMut" or "ManagedRes").Select(line => Id(line[2])),
            messagesRead: parameters.Where(static line => line[1] is "MessageReader" or "RemovedComponents").Select(line => Id((line[1] == "RemovedComponents" ? "removed:" : "") + line[2])),
            messagesWritten: parameters.Where(static line => line[1] == "MessageWriter").Select(line => Id(line[2])), traits: traits);
    }

    static GeneratedUnit GenerateSchedule(INamedTypeSymbol symbol, CancellationToken cancellationToken)
    {
        var problems = new List<Problem>();
        ValidatePartial(symbol, problems, cancellationToken);
        var runs = new List<RunModel>();
        foreach (var attribute in Attributes(symbol, "RunAttribute"))
        {
            if (attribute.ConstructorArguments.Length != 2 || attribute.ConstructorArguments[0].Value is not INamedTypeSymbol owner ||
                attribute.ConstructorArguments[1].Value is not string methodName) { Error(problems, symbol, "AV007", "Run needs a known owner and method name."); continue; }
            var methods = Systems(owner).Where(method => method.Name == methodName && FormOf(method) != SystemForm.Condition).ToArray();
            var exported = owner.GetMembers(methodName + "_Descriptor").Any(member => member.DeclaredAccessibility == Accessibility.Public);
            if (methods.Length != 1 && !exported) { Error(problems, symbol, "AV007", $"Unknown or ambiguous system {owner.Name}.{methodName}."); continue; }
            var order = attribute.NamedArguments.FirstOrDefault(static pair => pair.Key == "Order").Value.Value is int value ? value : 0;
            var key = NamedString(attribute, "Instance") ?? owner.ToDisplayString() + "." + methodName;
            runs.Add(new(owner, methodName, key, order, attribute));
            foreach (var manifest in owner.ContainingAssembly.GetAttributes().Where(static attribute => attribute.AttributeClass?.ToDisplayString() == "System.Reflection.AssemblyMetadataAttribute"))
                if (manifest.ConstructorArguments[0].Value is string metadata && metadata.StartsWith("Archevolt.Manifest.", StringComparison.Ordinal) && (manifest.ConstructorArguments[1].Value as string)?.Split('\n')[0] != "1")
                    Error(problems, symbol, "AV009", "Referenced Archevolt module uses an incompatible manifest ABI.");
        }
        foreach (var group in runs.GroupBy(static run => run.Key).Where(static group => group.Count() > 1)) Error(problems, symbol, "AV007", $"Duplicate system instance key {group.Key}.");
        foreach (var group in runs.GroupBy(static run => run.Order).Where(static group => group.Count() > 1)) Error(problems, symbol, "AV007", $"Duplicate Run order {group.Key}.");
        var edges = runs.Select(static _ => new HashSet<int>()).ToArray();
        for (int index = 0; index < runs.Count; index++)
        {
            foreach (var direction in new[] { "Before", "After" })
            {
                var target = NamedString(runs[index].Attribute, direction);
                if (target is null) continue;
                var targetOwner = runs[index].Attribute.NamedArguments.FirstOrDefault(pair => pair.Key == direction + "Owner").Value.Value as INamedTypeSymbol;
                var matches = runs.Select((run, i) => (run, i)).Where(pair => (targetOwner is null ? pair.run.Key == target || pair.run.Method == target :
                    SymbolEqualityComparer.Default.Equals(pair.run.Owner, targetOwner) && pair.run.Method == target)).ToArray();
                if (matches.Length != 1) Error(problems, symbol, "AV007", $"Unknown or ambiguous {direction} dependency {target}.");
                else if (direction == "Before") edges[index].Add(matches[0].i);
                else edges[matches[0].i].Add(index);
            }
        }
        foreach (var groupKind in new[] { "Stage", "SystemSet" })
        {
            var groups = new Dictionary<string, int>(StringComparer.Ordinal);
            var groupOrders = new HashSet<int>();
            foreach (var attribute in Attributes(symbol, groupKind + "Attribute"))
            {
                var name = attribute.ConstructorArguments.FirstOrDefault().Value as string;
                int order = attribute.NamedArguments.FirstOrDefault(static pair => pair.Key == "Order").Value.Value is int number ? number : 0;
                if (string.IsNullOrWhiteSpace(name) || groups.ContainsKey(name!)) Error(problems, symbol, "AV007", $"{groupKind} names must be nonempty and unique.");
                else groups.Add(name!, order);
                if (!groupOrders.Add(order)) Error(problems, symbol, "AV007", $"{groupKind} orders must be unique.");
            }
            string property = groupKind == "Stage" ? "Stage" : "Set";
            var membership = runs.Select(run => NamedString(run.Attribute, property)).ToArray();
            for (int index = 0; index < runs.Count; index++)
            {
                if ((groupKind == "Stage" && groups.Count != 0 && membership[index] is null) ||
                    membership[index] is { } name && !groups.ContainsKey(name))
                    Error(problems, symbol, "AV007", $"System {runs[index].Key} needs a known {property} name.");
                if (membership[index] is not { } current || !groups.TryGetValue(current, out var currentOrder)) continue;
                for (int other = 0; other < runs.Count; other++)
                    if (membership[other] is { } target && groups.TryGetValue(target, out var targetOrder) && currentOrder < targetOrder)
                        edges[index].Add(other);
            }
        }
        var conditions = new List<RunModel?>();
        foreach (var run in runs)
        {
            var conditionName = NamedString(run.Attribute, "Condition");
            if (conditionName is null) { conditions.Add(null); continue; }
            var owner = run.Attribute.NamedArguments.FirstOrDefault(static pair => pair.Key == "ConditionOwner").Value.Value as INamedTypeSymbol ?? run.Owner;
            var method = Systems(owner).FirstOrDefault(method => method.Name == conditionName && FormOf(method) == SystemForm.Condition);
            var importedManifest = owner.GetMembers("__Archevolt_" + conditionName + "_Access").OfType<IFieldSymbol>().FirstOrDefault()?.ConstantValue as string;
            if (method is null && importedManifest?.StartsWith("condition|True\n", StringComparison.Ordinal) != true)
                Error(problems, symbol, "AV007", $"Unknown run condition {owner.Name}.{conditionName}.");
            conditions.Add(new(owner, conditionName, run.Key + ".condition", run.Order, run.Attribute));
        }
        var identities = new Dictionary<string, int>(StringComparer.Ordinal);
        var nodes = runs.Select((run, index) =>
        {
            var access = RunAccess(run, identities, problems, cancellationToken);
            if (conditions[index] is { } condition)
            {
                var reads = RunAccess(condition, identities, problems, cancellationToken);
                access = new SystemAccess(access.Name, access.Clauses, access.ResourcesRead, access.ResourcesWritten,
                    access.MessagesRead.Concat(reads.MessagesRead), access.MessagesWritten, access.Traits | reads.Traits,
                    conditionClauses: reads.Clauses, conditionResources: reads.ResourcesRead);
            }
            return new NodeInput(index, run.Key, access, run.Order, conditions[index] is not null);
        }).ToArray();
        var explicitEdges = edges.SelectMany((targets, before) => targets.Select(after => new EdgeInput(before, after))).ToArray();
        var plan = SchedulePlanner.Plan(new ScheduleInput(nodes, explicitEdges));
        foreach (var diagnostic in plan.Diagnostics) Error(problems, symbol, diagnostic.Code, diagnostic.Detail);
        var text = Open(symbol);
        text.AppendLine("public static global::Archevolt.BoundSchedule Create(global::Archevolt.World world) => global::Archevolt.BoundSchedule.Bind(world,");
        text.Append("new global::Archevolt.SystemDescriptor[] { ").Append(string.Join(", ", runs.Select(run => TypeName(run.Owner) + "." + Escape(run.Method + "_Descriptor")))).AppendLine(" },");
        text.Append("new global::Archevolt.Scheduling.EdgeInput[] { ").Append(string.Join(", ", explicitEdges.Select(static edge => $"new({edge.Before}, {edge.After})"))).AppendLine(" },");
        text.Append("new string[] { ").Append(string.Join(", ", runs.Select(run => Literal(run.Key)))).AppendLine(" },");
        text.Append("new int[] { ").Append(string.Join(", ", runs.Select(static run => run.Order))).Append(" }");
        if (conditions.Any(static condition => condition is not null))
            text.Append(", new global::Archevolt.SystemDescriptor?[] { ").Append(string.Join(", ", conditions.Select(static condition => condition is null ? "null" : TypeName(condition.Owner) + "." + Escape(condition.Method + "_Descriptor")))).Append(" }");
        text.AppendLine(");");
        return Unit(symbol, "Schedule", Close(text, symbol), problems);
    }
}
