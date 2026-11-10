using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Archevolt.Generators;

// An [Each] callback runs as a SIMD kernel when every float lane of its one written component gets the same formula
// over the same lane of other components, resource values, and constants. The kernel applies that formula to the
// columns as flat float arrays, so it performs the same IEEE operations in the same order as the callback.
public sealed partial class ArchevoltGenerator
{
    abstract record Lane;
    sealed record ConstantLane(int Bits) : Lane;
    sealed record UniformLane(string Expression) : Lane;
    sealed record LoadLane(int Column, int Offset) : Lane;
    sealed record NegateLane(Lane Operand) : Lane;
    sealed record BinaryLane(char Operator, Lane Left, Lane Right) : Lane;

    sealed record KernelProbe(string Type, int Lanes, ImmutableArray<string> Leaves);
    sealed record KernelModel(int Lanes, int Written, Lane Template, ImmutableArray<int> Loads, ImmutableArray<string> Uniforms, ImmutableArray<KernelProbe> Probes);

    abstract record Symbolic;
    sealed record Floats(ImmutableArray<Lane> Lanes, ITypeSymbol Type) : Symbolic;
    sealed record ResourcePath(string Path, ITypeSymbol Type) : Symbolic;

    sealed record LaneLayout(int Lanes, Dictionary<ISymbol, (int Start, ITypeSymbol Type)> Members, ImmutableArray<string> Leaves);

    static KernelModel? Kernel(SystemModel model, IMethodSymbol method, SemanticModel semantics, SyntaxNode syntax, CancellationToken cancellationToken)
    {
        if (model.Form != SystemForm.Each || semantics.Compilation.Options.OptimizationLevel != OptimizationLevel.Release) return null;
        if (model.Queries[0].Any(static term => term.Mode is "Added" or "Changed" or "Optional")) return null;
        if (model.Parameters.Any(static parameter => parameter.Kind is not ("Component" or "Res"))) return null;
        var written = model.Parameters.Where(static parameter => parameter.Kind == "Component" && parameter.RefKind == "ref").ToArray();
        if (written.Length != 1 || syntax is not MethodDeclarationSyntax declaration) return null;
        if (semantics.GetOperation(declaration, cancellationToken) is not IMethodBodyOperation body) return null;
        var analysis = new KernelAnalysis(model, method);
        return analysis.Run(body, written[0].Index);
    }

    sealed class KernelAnalysis(SystemModel model, IMethodSymbol method)
    {
        readonly Dictionary<ITypeSymbol, LaneLayout?> _layouts = new(SymbolEqualityComparer.Default);
        readonly Dictionary<ILocalSymbol, Symbolic> _locals = new(SymbolEqualityComparer.Default);
        readonly Dictionary<int, ITypeSymbol> _columns = [];
        Lane[] _state = [];
        ITypeSymbol _writtenType = null!;
        int _written;

        public KernelModel? Run(IMethodBodyOperation body, int written)
        {
            _written = written;
            var parameter = method.Parameters[model.Parameters.FindIndex(static parameter => parameter.Kind == "Component" && parameter.RefKind == "ref")];
            _writtenType = parameter.Type;
            if (Layout(_writtenType) is not { } layout) return null;
            _state = Enumerable.Range(0, layout.Lanes).Select(lane => (Lane)new LoadLane(written, lane)).ToArray();
            _columns[written] = _writtenType;
            var block = body.BlockBody ?? body.ExpressionBody;
            if (block is null) return null;
            bool assigned = false;
            foreach (var statement in block.Operations)
            {
                switch (statement)
                {
                    case IReturnOperation { IsImplicit: true, ReturnedValue: null }:
                    case IEmptyOperation:
                        continue;
                    case IVariableDeclarationGroupOperation group:
                        foreach (var declaration in group.Declarations)
                            foreach (var declarator in declaration.Declarators)
                            {
                                var initializer = declarator.Initializer ?? declaration.Initializer;
                                if (declarator.Symbol.RefKind != RefKind.None || initializer is null || Evaluate(initializer.Value) is not { } value) return null;
                                _locals[declarator.Symbol] = value;
                            }
                        continue;
                    case IExpressionStatementOperation { Operation: var operation } when Assign(operation):
                        assigned = true;
                        continue;
                    default:
                        return null;
                }
            }
            if (!assigned) return null;
            return Template(layout);
        }

        KernelModel? Template(LaneLayout layout)
        {
            Lane? template = null;
            for (int lane = 0; lane < _state.Length; lane++)
            {
                if (Relative(_state[lane], lane) is not { } relative) return null;
                if (template is null) template = relative;
                else if (!template.Equals(relative)) return null;
            }
            if (template is null || template.Equals(new LoadLane(_written, 0))) return null;
            var loads = new SortedSet<int>();
            var uniforms = new List<string>();
            Collect(template, loads, uniforms);
            foreach (var column in loads)
                if (Layout(_columns[column]) is not { } loaded || loaded.Lanes != layout.Lanes) return null;
            var probes = new[] { _written }.Concat(loads.Where(column => column != _written))
                .Select(column => new KernelProbe(TypeName(_columns[column]), layout.Lanes, Layout(_columns[column])!.Leaves)).ToImmutableArray();
            return new(layout.Lanes, _written, template, loads.ToImmutableArray(), uniforms.ToImmutableArray(), probes);
        }

        static Lane? Relative(Lane lane, int offset) => lane switch
        {
            LoadLane load => load.Offset == offset ? load with { Offset = 0 } : null,
            NegateLane negate => Relative(negate.Operand, offset) is { } operand ? new NegateLane(operand) : null,
            BinaryLane binary => Relative(binary.Left, offset) is { } left && Relative(binary.Right, offset) is { } right ? binary with { Left = left, Right = right } : null,
            _ => lane
        };

        static void Collect(Lane lane, SortedSet<int> loads, List<string> uniforms)
        {
            switch (lane)
            {
                case LoadLane load: loads.Add(load.Column); break;
                case UniformLane uniform when !uniforms.Contains(uniform.Expression): uniforms.Add(uniform.Expression); break;
                case NegateLane negate: Collect(negate.Operand, loads, uniforms); break;
                case BinaryLane binary: Collect(binary.Left, loads, uniforms); Collect(binary.Right, loads, uniforms); break;
            }
        }

        bool Assign(IOperation operation)
        {
            switch (operation)
            {
                case ISimpleAssignmentOperation { IsRef: false } assignment when Place(assignment.Target) is { } place:
                    if (Materialize(Evaluate(assignment.Value)) is not { } value || value.Lanes.Length != place.Count) return false;
                    value.Lanes.CopyTo(_state, place.Start);
                    return true;
                case ICompoundAssignmentOperation compound when Place(compound.Target) is { } place
                    && compound.InConversion.IsIdentity && compound.OutConversion.IsIdentity:
                    var current = new Floats(_state.Skip(place.Start).Take(place.Count).ToImmutableArray(), compound.Target.Type!);
                    if (Combine(compound.OperatorKind, compound.OperatorMethod, current, Evaluate(compound.Value), compound.Target.Type!, compound.IsChecked, compound.IsLifted) is not { } result
                        || result.Lanes.Length != place.Count) return false;
                    result.Lanes.CopyTo(_state, place.Start);
                    return true;
                default:
                    return false;
            }
        }

        (int Start, int Count)? Place(IOperation target)
        {
            switch (target)
            {
                case IParameterReferenceOperation reference when IsWritten(reference.Parameter):
                    return (0, _state.Length);
                case IFieldReferenceOperation field when !field.Field.IsStatic && field.Instance is { } instance:
                    return Nested(instance, field.Field);
                case IPropertyReferenceOperation property when property.Instance is { } instance && property.Arguments.Length == 0:
                    return Nested(instance, property.Property);
                default:
                    return null;
            }
        }

        (int Start, int Count)? Nested(IOperation instance, ISymbol symbol) =>
            Place(instance) is { } outer && Member(instance.Type, symbol) is { } member && Layout(member.Type) is { } layout
                ? (outer.Start + member.Start, layout.Lanes) : null;

        bool IsWritten(IParameterSymbol parameter) => Kind(parameter) is ("Component", var index) && index == _written;

        (string Kind, int Index)? Kind(IParameterSymbol parameter)
        {
            int position = method.Parameters.IndexOf(parameter);
            return position < 0 ? null : (model.Parameters[position].Kind, model.Parameters[position].Index);
        }

        Symbolic? Evaluate(IOperation? operation)
        {
            if (operation is null) return null;
            if (operation.ConstantValue is { HasValue: true, Value: float constant } && operation.Type?.SpecialType == SpecialType.System_Single)
                return new Floats([new ConstantLane(Bits(constant))], operation.Type);
            switch (operation)
            {
                case IConversionOperation conversion when !conversion.IsTryCast && !conversion.Conversion.IsUserDefined
                    && SymbolEqualityComparer.Default.Equals(conversion.Operand.Type, conversion.Type):
                    return Evaluate(conversion.Operand);
                case ILocalReferenceOperation local:
                    return _locals.TryGetValue(local.Local, out var value) ? value : null;
                case IParameterReferenceOperation reference:
                    return Kind(reference.Parameter) switch
                    {
                        ("Component", var index) when index == _written => new Floats(_state.ToImmutableArray(), reference.Type!),
                        ("Component", var index) => Load(index, reference.Type!),
                        ("Res", _) => new ResourcePath(model.Parameters[method.Parameters.IndexOf(reference.Parameter)].Name, reference.Type!),
                        _ => null
                    };
                case IPropertyReferenceOperation property when property.Arguments.Length == 0 && property.Instance is { } instance:
                    if (Evaluate(instance) is ResourcePath resource)
                    {
                        if (resource.Type is INamedTypeSymbol { Name: "Res", Arity: 1 } wrapper && wrapper.ContainingNamespace.ToDisplayString() == "Archevolt" && property.Property.Name == "Value")
                            return new ResourcePath(resource.Path + ".Value", wrapper.TypeArguments[0]);
                        return PlainProperty(property.Property) ? new ResourcePath(resource.Path + "." + Escape(property.Property.Name), property.Type!) : null;
                    }
                    return Slice(Evaluate(instance), property.Property, property.Type!);
                case IFieldReferenceOperation field when !field.Field.IsStatic && field.Instance is { } instance:
                    if (Evaluate(instance) is ResourcePath fieldResource)
                        return new ResourcePath(fieldResource.Path + "." + Escape(field.Field.Name), field.Type!);
                    return Slice(Evaluate(instance), field.Field, field.Type!);
                case IUnaryOperation { OperatorKind: UnaryOperatorKind.Plus, OperatorMethod: null } plus when plus.Type?.SpecialType == SpecialType.System_Single:
                    return Materialize(Evaluate(plus.Operand));
                case IUnaryOperation { OperatorKind: UnaryOperatorKind.Minus, OperatorMethod: null, IsLifted: false } minus when minus.Type?.SpecialType == SpecialType.System_Single:
                    return Materialize(Evaluate(minus.Operand)) is { Lanes.Length: 1 } negated ? new Floats([new NegateLane(negated.Lanes[0])], minus.Type) : null;
                case IBinaryOperation binary:
                    return Combine(binary.OperatorKind, binary.OperatorMethod, Evaluate(binary.LeftOperand), Evaluate(binary.RightOperand), binary.Type!, binary.IsChecked, binary.IsLifted);
                case IObjectCreationOperation creation when creation.Initializer is null && creation.Constructor is { } constructor:
                    return Construct(constructor, creation.Arguments, creation.Type!);
                case IWithOperation with when with.CloneMethod is null:
                    return With(with);
                default:
                    return null;
            }
        }

        Floats? Load(int column, ITypeSymbol type)
        {
            if (Layout(type) is not { } layout) return null;
            _columns[column] = type;
            return new Floats(Enumerable.Range(0, layout.Lanes).Select(lane => (Lane)new LoadLane(column, lane)).ToImmutableArray(), type);
        }

        Floats? Slice(Symbolic? instance, ISymbol member, ITypeSymbol type)
        {
            if (Materialize(instance) is not { } value || Member(value.Type, member) is not { } location || Layout(type) is not { } layout) return null;
            return new Floats(value.Lanes.Skip(location.Start).Take(layout.Lanes).ToImmutableArray(), type);
        }

        Floats? Materialize(Symbolic? value)
        {
            switch (value)
            {
                case Floats floats:
                    return floats;
                case ResourcePath resource when resource.Type.SpecialType == SpecialType.System_Single:
                    return new Floats([new UniformLane(resource.Path)], resource.Type);
                case ResourcePath resource when VectorLanes(resource.Type) is { } lanes:
                    return new Floats(Enumerable.Range(0, lanes).Select(lane => (Lane)new UniformLane(resource.Path + "." + "XYZW"[lane])).ToImmutableArray(), resource.Type);
                default:
                    return null;
            }
        }

        Floats? Combine(BinaryOperatorKind kind, IMethodSymbol? operatorMethod, Symbolic? left, Symbolic? right, ITypeSymbol type, bool isChecked, bool isLifted)
        {
            if (isLifted || Materialize(left) is not { } l || Materialize(right) is not { } r) return null;
            char? symbol = kind switch
            {
                BinaryOperatorKind.Add => '+',
                BinaryOperatorKind.Subtract => '-',
                BinaryOperatorKind.Multiply => '*',
                BinaryOperatorKind.Divide => '/',
                _ => null
            };
            if (symbol is not { } op) return null;
            if (operatorMethod is null)
            {
                if (type.SpecialType != SpecialType.System_Single || l.Lanes.Length != 1 || r.Lanes.Length != 1
                    || l.Type.SpecialType != SpecialType.System_Single || r.Type.SpecialType != SpecialType.System_Single) return null;
                return new Floats([new BinaryLane(op, l.Lanes[0], r.Lanes[0])], type);
            }
            if (op == '/' || VectorLanes(operatorMethod.ContainingType) is not { } lanes || !SymbolEqualityComparer.Default.Equals(operatorMethod.ContainingType, type)) return null;
            static Lane At(Floats value, int lane) => value.Lanes.Length == 1 ? value.Lanes[0] : value.Lanes[lane];
            bool Fits(Floats value) => value.Lanes.Length == lanes && SymbolEqualityComparer.Default.Equals(value.Type, type)
                || value.Lanes.Length == 1 && value.Type.SpecialType == SpecialType.System_Single;
            if (!Fits(l) || !Fits(r) || l.Lanes.Length == 1 && r.Lanes.Length == 1) return null;
            return new Floats(Enumerable.Range(0, lanes).Select(lane => (Lane)new BinaryLane(op, At(l, lane), At(r, lane))).ToImmutableArray(), type);
        }

        Floats? Construct(IMethodSymbol constructor, ImmutableArray<IArgumentOperation> arguments, ITypeSymbol type)
        {
            if (Layout(type) is not { } layout) return null;
            var values = new Floats?[constructor.Parameters.Length];
            foreach (var argument in arguments)
            {
                if (argument.Parameter is not { } parameter || argument.ArgumentKind == ArgumentKind.ParamArray) return null;
                values[parameter.Ordinal] = Materialize(Evaluate(argument.Value));
                if (values[parameter.Ordinal] is null) return null;
            }
            if (VectorLanes(type) is { } vectorLanes)
            {
                if (constructor.Parameters.Any(static parameter => parameter.Type.SpecialType != SpecialType.System_Single)) return null;
                if (values.Length == 1) return new Floats(Enumerable.Repeat(values[0]!.Lanes[0], vectorLanes).ToImmutableArray(), type);
                return values.Length == vectorLanes ? new Floats(values.Select(static value => value!.Lanes[0]).ToImmutableArray(), type) : null;
            }
            if (constructor.DeclaringSyntaxReferences.Length != 1 || constructor.DeclaringSyntaxReferences[0].GetSyntax() is not RecordDeclarationSyntax record
                || record.ParameterList is null) return null;
            var lanes = new Lane?[layout.Lanes];
            foreach (var parameter in constructor.Parameters)
            {
                var property = type.GetMembers(parameter.Name).OfType<IPropertySymbol>().FirstOrDefault();
                if (property is null || property.DeclaringSyntaxReferences.Length != 1 || parameter.DeclaringSyntaxReferences.Length != 1
                    || property.DeclaringSyntaxReferences[0].SyntaxTree != parameter.DeclaringSyntaxReferences[0].SyntaxTree
                    || property.DeclaringSyntaxReferences[0].Span != parameter.DeclaringSyntaxReferences[0].Span
                    || Member(type, property) is not { } member || values[parameter.Ordinal] is not { } value || Layout(member.Type) is not { } memberLayout
                    || value.Lanes.Length != memberLayout.Lanes) return null;
                for (int lane = 0; lane < value.Lanes.Length; lane++)
                {
                    if (lanes[member.Start + lane] is not null) return null;
                    lanes[member.Start + lane] = value.Lanes[lane];
                }
            }
            return lanes.All(static lane => lane is not null) ? new Floats(lanes.Select(static lane => lane!).ToImmutableArray(), type) : null;
        }

        Floats? With(IWithOperation with)
        {
            if (Materialize(Evaluate(with.Operand)) is not { } operand || with.Initializer is not IObjectOrCollectionInitializerOperation initializer) return null;
            var lanes = operand.Lanes.ToArray();
            foreach (var entry in initializer.Initializers)
            {
                if (entry is not ISimpleAssignmentOperation { IsRef: false } assignment) return null;
                ISymbol? member = assignment.Target switch
                {
                    IPropertyReferenceOperation { Instance: IInstanceReferenceOperation, Arguments.Length: 0 } property => property.Property,
                    IFieldReferenceOperation { Instance: IInstanceReferenceOperation } field => field.Field,
                    _ => null
                };
                if (member is null || Member(operand.Type, member) is not { } location || Materialize(Evaluate(assignment.Value)) is not { } value
                    || Layout(location.Type) is not { } memberLayout || value.Lanes.Length != memberLayout.Lanes) return null;
                value.Lanes.CopyTo(lanes, location.Start);
            }
            return new Floats(lanes.ToImmutableArray(), operand.Type);
        }

        (int Start, ITypeSymbol Type)? Member(ITypeSymbol? type, ISymbol member) =>
            type is not null && Layout(type) is { } layout && layout.Members.TryGetValue(member, out var location) ? location : null;

        LaneLayout? Layout(ITypeSymbol type)
        {
            if (_layouts.TryGetValue(type, out var cached)) return cached;
            _layouts[type] = null;
            var layout = Compute(type);
            _layouts[type] = layout;
            return layout;
        }

        LaneLayout? Compute(ITypeSymbol type)
        {
            if (type.SpecialType == SpecialType.System_Single)
                return new(1, new(SymbolEqualityComparer.Default), [""]);
            bool vector = VectorLanes(type) is not null;
            if (type is not INamedTypeSymbol { TypeKind: TypeKind.Struct, IsRefLikeType: false } named || named.IsGenericType) return null;
            if (!vector && (named.DeclaringSyntaxReferences.Length == 0 || named.GetAttributes().Any(static attribute => attribute.AttributeClass?.Name == "StructLayoutAttribute")
                || named.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax() is TypeDeclarationSyntax declaration
                    && declaration.AttributeLists.SelectMany(static list => list.Attributes).Any(static attribute => attribute.Name.ToString().Contains("StructLayout")))))
                return null;
            var members = new Dictionary<ISymbol, (int Start, ITypeSymbol Type)>(SymbolEqualityComparer.Default);
            var leaves = ImmutableArray.CreateBuilder<string>();
            int lanes = 0;
            foreach (var field in named.GetMembers().OfType<IFieldSymbol>().Where(static field => !field.IsStatic && !field.IsConst))
            {
                if (field.IsFixedSizeBuffer || field.RefKind != RefKind.None || Layout(field.Type) is not { } inner) return null;
                string name;
                if (field.AssociatedSymbol is IPropertySymbol property)
                {
                    if (!PlainProperty(property)) return null;
                    members[property] = (lanes, field.Type);
                    name = property.Name;
                }
                else
                {
                    if (field.IsImplicitlyDeclared || field.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal)) return null;
                    name = field.Name;
                }
                members[field] = (lanes, field.Type);
                foreach (var leaf in inner.Leaves) leaves.Add(leaf.Length == 0 ? Escape(name) : Escape(name) + "." + leaf);
                lanes += inner.Lanes;
            }
            return lanes == 0 || vector && lanes != VectorLanes(type) ? null : new(lanes, members, leaves.ToImmutable());
        }

        static bool PlainProperty(IPropertySymbol property)
        {
            if (property.IsStatic || property.IsIndexer || property.ReturnsByRef || property.ReturnsByRefReadonly || property.GetMethod is null
                || property.GetMethod.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal) || property.DeclaringSyntaxReferences.Length != 1) return false;
            var syntax = property.DeclaringSyntaxReferences[0].GetSyntax();
            if (syntax is ParameterSyntax) return true;
            return syntax is PropertyDeclarationSyntax { ExpressionBody: null, AccessorList: { } accessors }
                && accessors.Accessors.All(static accessor => accessor.Body is null && accessor.ExpressionBody is null);
        }
    }

    static int? VectorLanes(ITypeSymbol type) => type is INamedTypeSymbol { IsGenericType: false } named && named.ContainingNamespace.ToDisplayString() == "System.Numerics"
        ? named.Name switch { "Vector2" => 2, "Vector3" => 3, "Vector4" => 4, _ => null } : null;

    static int Bits(float value) => BitConverter.ToInt32(BitConverter.GetBytes(value), 0);

    const string Vector128Type = "global::System.Runtime.Intrinsics.Vector128";
    const string Vector256Type = "global::System.Runtime.Intrinsics.Vector256";
    const string Marshal = "global::System.Runtime.InteropServices.MemoryMarshal";

    static string FloatLiteral(int bits) => "global::System.BitConverter.Int32BitsToSingle(unchecked((int)0x" + bits.ToString("X8", CultureInfo.InvariantCulture) + "))";

    static string KernelParameter(int column) => "__column" + column;

    static void AppendKernelMembers(StringBuilder text, KernelModel kernel)
    {
        text.Append("static readonly bool __kernel = ").Append(Vector128Type).AppendLine(".IsHardwareAccelerated && __Probe();");
        text.AppendLine("static bool __Probe()\n{");
        foreach (var probe in kernel.Probes)
        {
            text.AppendLine("{");
            text.Append("if (global::System.Runtime.CompilerServices.Unsafe.SizeOf<").Append(probe.Type).Append(">() != ").Append(probe.Lanes * 4).AppendLine(") return false;");
            text.Append(probe.Type).AppendLine(" __probe = default;");
            text.Append("var __lanes = ").Append(Marshal).Append(".CreateSpan(ref global::System.Runtime.CompilerServices.Unsafe.As<").Append(probe.Type)
                .Append(", float>(ref __probe), ").Append(probe.Lanes).AppendLine(");");
            text.AppendLine("for (int __lane = 0; __lane < __lanes.Length; __lane++) __lanes[__lane] = __lane + 1;");
            text.Append("if (").Append(string.Join(" || ", probe.Leaves.Select((leaf, lane) => $"__probe.{leaf} != {lane + 1}"))).AppendLine(") return false;");
            text.AppendLine("}");
        }
        text.AppendLine("return true;\n}");
        var columns = new[] { kernel.Written }.Concat(kernel.Loads.Where(column => column != kernel.Written)).ToArray();
        text.Append("static void __Kernel(").Append(string.Join(", ", columns.Select(column => (column == kernel.Written ? "global::System.Span<float> " : "global::System.ReadOnlySpan<float> ") + KernelParameter(column))
            .Concat(kernel.Uniforms.Select(static (_, index) => "float __uniform" + index)))).AppendLine(")\n{");
        text.Append("int __count = ").Append(KernelParameter(kernel.Written)).AppendLine(".Length;");
        foreach (var column in columns.Where(column => column != kernel.Written))
            text.Append(KernelParameter(column)).Append(" = ").Append(KernelParameter(column)).AppendLine(".Slice(0, __count);");
        foreach (var column in columns)
            text.Append("ref float __base").Append(column).Append(" = ref ").Append(Marshal).Append(".GetReference(").Append(KernelParameter(column)).AppendLine(");");
        text.AppendLine("int __index = 0;");
        AppendVectorLoop(text, kernel, Vector256Type, columns, first: true);
        AppendVectorLoop(text, kernel, Vector128Type, columns, first: false);
        text.AppendLine("for (; __index < __count; __index++)");
        text.Append(KernelParameter(kernel.Written)).Append("[__index] = ").Append(Scalar(kernel, kernel.Template)).AppendLine(";");
        text.AppendLine("}");
    }

    static void AppendVectorLoop(StringBuilder text, KernelModel kernel, string vector, int[] columns, bool first)
    {
        text.Append(first ? "if (" : "else if (").Append(vector).AppendLine(".IsHardwareAccelerated)\n{");
        var constants = new List<int>();
        CollectConstants(kernel.Template, constants);
        foreach (var bits in constants) text.Append("var __constant").Append((uint)bits).Append(" = ").Append(vector).Append(".Create(").Append(FloatLiteral(bits)).AppendLine(");");
        for (int index = 0; index < kernel.Uniforms.Length; index++) text.Append("var __broadcast").Append(index).Append(" = ").Append(vector).Append(".Create(__uniform").Append(index).AppendLine(");");
        if (HasNegation(kernel.Template)) text.Append("var __sign = ").Append(vector).Append(".Create(").Append(FloatLiteral(unchecked((int)0x80000000))).AppendLine(");");
        text.Append("for (; __index <= __count - ").Append(vector).Append("<float>.Count; __index += ").Append(vector).AppendLine("<float>.Count)\n{");
        foreach (var column in kernel.Loads) text.Append("var __load").Append(column).Append(" = ").Append(vector).Append(".LoadUnsafe(ref __base").Append(column).AppendLine(", (nuint)__index);");
        text.Append(vector).Append(".StoreUnsafe(").Append(VectorExpression(kernel, kernel.Template, vector)).Append(", ref __base").Append(kernel.Written).AppendLine(", (nuint)__index);");
        text.AppendLine("}\n}");
    }

    static void CollectConstants(Lane lane, List<int> constants)
    {
        switch (lane)
        {
            case ConstantLane constant when !constants.Contains(constant.Bits): constants.Add(constant.Bits); break;
            case NegateLane negate: CollectConstants(negate.Operand, constants); break;
            case BinaryLane binary: CollectConstants(binary.Left, constants); CollectConstants(binary.Right, constants); break;
        }
    }

    static bool HasNegation(Lane lane) => lane switch
    {
        NegateLane => true,
        BinaryLane binary => HasNegation(binary.Left) || HasNegation(binary.Right),
        _ => false
    };

    static string VectorExpression(KernelModel kernel, Lane lane, string vector) => lane switch
    {
        ConstantLane constant => "__constant" + (uint)constant.Bits,
        UniformLane uniform => "__broadcast" + kernel.Uniforms.IndexOf(uniform.Expression),
        LoadLane load => "__load" + load.Column,
        NegateLane negate => vector + ".Xor(" + VectorExpression(kernel, negate.Operand, vector) + ", __sign)",
        BinaryLane binary => "(" + VectorExpression(kernel, binary.Left, vector) + " " + binary.Operator + " " + VectorExpression(kernel, binary.Right, vector) + ")",
        _ => throw new InvalidOperationException()
    };

    static string Scalar(KernelModel kernel, Lane lane) => lane switch
    {
        ConstantLane constant => FloatLiteral(constant.Bits),
        UniformLane uniform => "__uniform" + kernel.Uniforms.IndexOf(uniform.Expression),
        LoadLane load => KernelParameter(load.Column) + "[__index]",
        NegateLane negate => "-(" + Scalar(kernel, negate.Operand) + ")",
        BinaryLane binary => "(" + Scalar(kernel, binary.Left) + " " + binary.Operator + " " + Scalar(kernel, binary.Right) + ")",
        _ => throw new InvalidOperationException()
    };

    static void AppendKernelCall(StringBuilder text, KernelModel kernel, IReadOnlyList<TermModel> terms)
    {
        var columns = new[] { kernel.Written }.Concat(kernel.Loads.Where(column => column != kernel.Written));
        text.Append("if (__kernel && count <= ").Append(int.MaxValue / kernel.Lanes).AppendLine(")\n{");
        var written = terms.First(term => term.Index == kernel.Written);
        if (Stamped(written)) text.Append(ColumnName(written)).AppendLine(".Changed.Fill(__now);");
        text.Append("__Kernel(").Append(string.Join(", ", columns.Select(column =>
            {
                var term = terms.First(candidate => candidate.Index == column);
                var span = term.Mode == "Write" && term.Component.Tracked ? ColumnName(term) + ".Values" : ColumnName(term);
                return Marshal + ".Cast<" + term.Component.Type + ", float>(" + span + ")";
            }).Concat(kernel.Uniforms))).AppendLine(");");
        text.AppendLine("continue;\n}");
    }
}
