using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Archevolt.Generators;

// An [Each] callback runs as a SIMD kernel when the generator can write every float of its one written component as a
// formula over the floats of the same row, resource values, and constants. The analysis runs the callback symbolically:
// it follows locals, branches, and calls into methods of the same compilation, and it turns a branch into a select
// between both outcomes. The kernel performs the same IEEE operations in the same order as the callback.
public sealed partial class ArchevoltGenerator
{
    // A lane is the value of one float. Compare, logic, and not lanes are masks, and appear only under a select.
    abstract record Lane;
    sealed record ConstantLane(int Bits) : Lane;
    sealed record UniformLane(string Expression) : Lane;
    sealed record LoadLane(int Column, int Offset) : Lane;
    sealed record NegateLane(Lane Operand) : Lane;
    sealed record BinaryLane(char Operator, Lane Left, Lane Right) : Lane;
    sealed record FunctionLane(string Name, Lane Left, Lane? Right) : Lane;
    sealed record SelectLane(Lane Condition, Lane WhenTrue, Lane WhenFalse) : Lane;
    sealed record CompareLane(string Operator, Lane Left, Lane Right) : Lane;
    sealed record LogicLane(char Operator, Lane Left, Lane Right) : Lane;
    sealed record NotLane(Lane Operand) : Lane;

    sealed record KernelProbe(string Type, int Lanes, ImmutableArray<string> Leaves);

    // A flat kernel has one formula that every float shares, so it treats the columns as flat float arrays. Otherwise
    // Formula holds one entry per float of the written component, and the kernel works on whole rows.
    sealed record KernelModel(int Lanes, int Written, ImmutableArray<Lane> Formula, bool Flat, ImmutableArray<int> Loads, ImmutableArray<string> Uniforms, ImmutableArray<KernelProbe> Probes);

    abstract record Symbolic;
    sealed record Floats(ImmutableArray<Lane> Lanes, ITypeSymbol Type) : Symbolic;
    sealed record ResourcePath(string Path, ITypeSymbol Type) : Symbolic;
    sealed record Condition(Lane Mask) : Symbolic;

    // Slot is the local or by-value parameter that holds the floats, or null for the written component.
    sealed record Target(ISymbol? Slot, int Start, int Count, ITypeSymbol Type);

    abstract record Binding;
    sealed record ValueBinding(Symbolic Value) : Binding;
    sealed record ColumnBinding(int Column) : Binding;
    sealed record AliasBinding(Target Target) : Binding;

    sealed record LaneLayout(int Lanes, Dictionary<ISymbol, (int Start, ITypeSymbol Type)> Members, ImmutableArray<string> Leaves);

    static Lane[] Children(Lane lane) => lane switch
    {
        NegateLane negate => [negate.Operand],
        BinaryLane binary => [binary.Left, binary.Right],
        FunctionLane { Right: null } function => [function.Left],
        FunctionLane function => [function.Left, function.Right!],
        SelectLane select => [select.Condition, select.WhenTrue, select.WhenFalse],
        CompareLane compare => [compare.Left, compare.Right],
        LogicLane logic => [logic.Left, logic.Right],
        NotLane not => [not.Operand],
        _ => []
    };

    static Lane Rebuild(Lane lane, Lane[] children) => lane switch
    {
        NegateLane => new NegateLane(children[0]),
        BinaryLane binary => binary with { Left = children[0], Right = children[1] },
        FunctionLane function => function with { Left = children[0], Right = children.Length > 1 ? children[1] : null },
        SelectLane => new SelectLane(children[0], children[1], children[2]),
        CompareLane compare => compare with { Left = children[0], Right = children[1] },
        LogicLane logic => logic with { Left = children[0], Right = children[1] },
        NotLane => new NotLane(children[0]),
        _ => lane
    };

    static Lane? Rewrite(Lane lane, Func<LoadLane, Lane?> load)
    {
        if (lane is LoadLane leaf) return load(leaf);
        var children = Children(lane);
        if (children.Length == 0) return lane;
        var rewritten = new Lane[children.Length];
        for (int index = 0; index < children.Length; index++)
        {
            if (Rewrite(children[index], load) is not { } child) return null;
            rewritten[index] = child;
        }
        return Rebuild(lane, rewritten);
    }

    static bool Fits(Lane lane, ref int budget)
    {
        if (--budget < 0) return false;
        foreach (var child in Children(lane))
            if (!Fits(child, ref budget)) return false;
        return true;
    }

    static void Collect(Lane lane, SortedSet<int> loads, List<string> uniforms)
    {
        if (lane is LoadLane load) loads.Add(load.Column);
        else if (lane is UniformLane uniform && !uniforms.Contains(uniform.Expression)) uniforms.Add(uniform.Expression);
        foreach (var child in Children(lane)) Collect(child, loads, uniforms);
    }

    static KernelModel? Kernel(SystemModel model, IMethodSymbol method, SemanticModel semantics, SyntaxNode syntax, CancellationToken cancellationToken)
    {
        if (model.Form != SystemForm.Each || semantics.Compilation.Options.OptimizationLevel != OptimizationLevel.Release) return null;
        if (model.Queries[0].Any(static term => term.Mode is "Added" or "Changed" or "Optional")) return null;
        if (model.Parameters.Any(static parameter => parameter.Kind is not ("Component" or "Res"))) return null;
        // A [Conditional] callback is compiled away where its symbol is not defined. A kernel would still run.
        if (Conditional(method)) return null;
        var written = model.Parameters.Where(static parameter => parameter.Kind == "Component" && parameter.RefKind == "ref").ToArray();
        if (written.Length != 1 || syntax is not MethodDeclarationSyntax declaration) return null;
        if (semantics.GetOperation(declaration, cancellationToken) is not IMethodBodyOperation body) return null;
        return new KernelAnalysis(model, method, semantics.Compilation, cancellationToken).Run(body, written[0].Index);
    }

    static bool Conditional(IMethodSymbol method) =>
        method.GetAttributes().Any(static attribute => attribute.AttributeClass?.ToDisplayString() == "System.Diagnostics.ConditionalAttribute");

    sealed class KernelAnalysis(SystemModel model, IMethodSymbol method, Compilation compilation, CancellationToken cancellationToken)
    {
        // Every branch doubles the formula of a float it assigns. Past this size the row loop is the better code.
        const int MaximumFormulaSize = 512;
        const int MaximumCallDepth = 8;

        readonly Dictionary<ITypeSymbol, LaneLayout?> _layouts = new(SymbolEqualityComparer.Default);
        readonly Dictionary<int, ITypeSymbol> _columns = [];
        readonly HashSet<IMethodSymbol> _calls = new(SymbolEqualityComparer.Default);
        Dictionary<ISymbol, Binding> _bindings = new(SymbolEqualityComparer.Default);
        // Floats that an in parameter of a call in progress refers to. The callee reads them by reference, so a write to
        // them during the call would show through a parameter that the analysis bound to a value.
        readonly List<Target> _frozen = [];
        Lane[] _state = [];
        int _written;

        public KernelModel? Run(IMethodBodyOperation body, int written)
        {
            _written = written;
            LaneLayout? layout = null;
            for (int position = 0; position < method.Parameters.Length; position++)
            {
                var parameter = method.Parameters[position];
                var role = model.Parameters[position];
                if (role.Kind == "Res") _bindings[parameter] = new ValueBinding(new ResourcePath(role.Name, parameter.Type));
                else if (role.Index != written) _bindings[parameter] = new ColumnBinding(role.Index);
                else
                {
                    if ((layout = Layout(parameter.Type)) is null) return null;
                    _bindings[parameter] = new AliasBinding(new Target(null, 0, layout.Lanes, parameter.Type));
                    _columns[written] = parameter.Type;
                }
            }
            if (layout is null) return null;
            _state = Enumerable.Range(0, layout.Lanes).Select(lane => (Lane)new LoadLane(written, lane)).ToArray();
            return Body(body, out _) ? Template(layout) : null;
        }

        KernelModel? Template(LaneLayout layout)
        {
            if (!Bounded() || _state.Select(static (lane, index) => (lane, index)).All(entry => entry.lane.Equals(new LoadLane(_written, entry.index)))) return null;
            var loads = new SortedSet<int>();
            var uniforms = new List<string>();
            foreach (var lane in _state) Collect(lane, loads, uniforms);
            foreach (var column in loads)
                if (Layout(_columns[column]) is not { } loaded || loaded.Lanes != layout.Lanes) return null;
            var probes = new[] { _written }.Concat(loads.Where(column => column != _written))
                .Select(column => new KernelProbe(TypeName(_columns[column]), layout.Lanes, Layout(_columns[column])!.Leaves)).ToImmutableArray();
            var relative = _state.Select(static (lane, index) => Rewrite(lane, load => load.Offset == index ? load with { Offset = 0 } : null)).ToArray();
            bool flat = relative[0] is not null && relative.All(lane => relative[0]!.Equals(lane));
            if (!flat && layout.Lanes > 8) return null;
            return new(layout.Lanes, _written, flat ? [relative[0]!] : _state.ToImmutableArray(), flat, loads.ToImmutableArray(), uniforms.ToImmutableArray(), probes);
        }

        bool Bounded()
        {
            foreach (var lane in _state)
            {
                int budget = MaximumFormulaSize;
                if (!Fits(lane, ref budget)) return false;
            }
            return true;
        }

        bool Body(IMethodBodyOperation body, out Symbolic? result)
        {
            result = null;
            return (body.BlockBody ?? body.ExpressionBody) is { } block && Statements(block.Operations, top: true, ref result);
        }

        // A return is accepted only as the last top-level statement of a method, so no statement runs after one.
        bool Statements(ImmutableArray<IOperation> statements, bool top, ref Symbolic? result)
        {
            for (int index = 0; index < statements.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool last = top && index == statements.Length - 1;
                switch (statements[index])
                {
                    case IEmptyOperation:
                        continue;
                    case IReturnOperation { ReturnedValue: null } returned when returned.IsImplicit || last:
                        continue;
                    case IReturnOperation { ReturnedValue: { } value } when last:
                        if ((result = Evaluate(value)) is null) return false;
                        continue;
                    case IVariableDeclarationGroupOperation group:
                        foreach (var declaration in group.Declarations)
                            foreach (var declarator in declaration.Declarators)
                            {
                                var initializer = declarator.Initializer ?? declaration.Initializer;
                                if (declarator.Symbol.RefKind != RefKind.None || initializer is null || Evaluate(initializer.Value) is not { } value) return false;
                                _bindings[declarator.Symbol] = new ValueBinding(Materialize(value) ?? value);
                            }
                        continue;
                    case IExpressionStatementOperation { Operation: var operation }:
                        if (!Effect(operation)) return false;
                        continue;
                    case IBlockOperation block:
                        if (!Statements(block.Operations, top: false, ref result)) return false;
                        continue;
                    case IConditionalOperation conditional:
                        if (!Branch(conditional)) return false;
                        continue;
                    default:
                        return false;
                }
            }
            return true;
        }

        bool Effect(IOperation operation)
        {
            switch (operation)
            {
                case ISimpleAssignmentOperation { IsRef: false } assignment when Resolve(assignment.Target) is { } target:
                    return Materialize(Evaluate(assignment.Value)) is { } value && Write(target, value.Lanes);
                case ISimpleAssignmentOperation { IsRef: false, Target: ILocalReferenceOperation local } assignment:
                    if (Evaluate(assignment.Value) is not { } replaced) return false;
                    _bindings[local.Local] = new ValueBinding(replaced);
                    return true;
                case ICompoundAssignmentOperation compound when Resolve(compound.Target) is { } target
                    && compound.InConversion.IsIdentity && compound.OutConversion.IsIdentity:
                    return Read(target) is { } current
                        && Combine(compound.OperatorKind, compound.OperatorMethod, current, Evaluate(compound.Value), compound.Target.Type!, compound.IsLifted) is { } result
                        && Write(target, result.Lanes);
                case IInvocationOperation invocation:
                    return Call(invocation, out _);
                default:
                    return false;
            }
        }

        // Both outcomes are computed from the state before the branch. Each float that differs afterwards becomes a
        // select on the condition, so a write made in one outcome reaches only the rows that take it.
        bool Branch(IConditionalOperation conditional)
        {
            if (Evaluate(conditional.Condition) is not Condition condition) return false;
            var before = (State: (Lane[])_state.Clone(), Bindings: new Dictionary<ISymbol, Binding>(_bindings, SymbolEqualityComparer.Default));
            Symbolic? ignored = null;
            if (!Statements([conditional.WhenTrue], top: false, ref ignored)) return false;
            var whenTrue = (State: _state, Bindings: _bindings);
            _state = (Lane[])before.State.Clone();
            _bindings = new Dictionary<ISymbol, Binding>(before.Bindings, SymbolEqualityComparer.Default);
            if (conditional.WhenFalse is { } otherwise && !Statements([otherwise], top: false, ref ignored)) return false;
            var whenFalse = (State: _state, Bindings: _bindings);
            _state = before.State;
            _bindings = before.Bindings;
            for (int lane = 0; lane < _state.Length; lane++) _state[lane] = Select(condition.Mask, whenTrue.State[lane], whenFalse.State[lane]);
            foreach (var entry in before.Bindings.Where(static entry => entry.Value is ValueBinding).ToArray())
            {
                if (whenTrue.Bindings[entry.Key] is not ValueBinding left || whenFalse.Bindings[entry.Key] is not ValueBinding right
                    || Select(condition.Mask, left.Value, right.Value) is not { } merged) return false;
                _bindings[entry.Key] = new ValueBinding(merged);
            }
            return Bounded();
        }

        static Lane Select(Lane condition, Lane whenTrue, Lane whenFalse) => whenTrue.Equals(whenFalse) ? whenTrue : new SelectLane(condition, whenTrue, whenFalse);

        static Symbolic? Select(Lane condition, Symbolic whenTrue, Symbolic whenFalse) => (whenTrue, whenFalse) switch
        {
            (Floats left, Floats right) when left.Lanes.Length == right.Lanes.Length
                => new Floats(left.Lanes.Zip(right.Lanes, (a, b) => Select(condition, a, b)).ToImmutableArray(), left.Type),
            (ResourcePath left, ResourcePath right) when left.Equals(right) => left,
            (Condition left, Condition right) => left.Equals(right) ? left
                : new Condition(new LogicLane('|', new LogicLane('&', condition, left.Mask), new LogicLane('&', new NotLane(condition), right.Mask))),
            _ => null
        };

        Target? Resolve(IOperation target)
        {
            switch (target)
            {
                case IParameterReferenceOperation reference:
                    return _bindings.TryGetValue(reference.Parameter, out var binding) ? binding switch
                    {
                        AliasBinding alias => alias.Target,
                        ValueBinding { Value: Floats floats } => new Target(reference.Parameter, 0, floats.Lanes.Length, floats.Type),
                        _ => null
                    } : null;
                case ILocalReferenceOperation local:
                    return _bindings.TryGetValue(local.Local, out var held) && held is ValueBinding { Value: Floats value }
                        ? new Target(local.Local, 0, value.Lanes.Length, value.Type) : null;
                case IFieldReferenceOperation field when !field.Field.IsStatic && field.Instance is { } instance:
                    return Nested(instance, field.Field);
                case IPropertyReferenceOperation property when property.Instance is { } instance && property.Arguments.Length == 0:
                    return Nested(instance, property.Property);
                default:
                    return null;
            }
        }

        Target? Nested(IOperation instance, ISymbol symbol) =>
            Resolve(instance) is { } outer && Member(outer.Type, symbol) is { } member && Layout(member.Type) is { } layout
                ? new Target(outer.Slot, outer.Start + member.Start, layout.Lanes, member.Type) : null;

        Floats? Read(Target target)
        {
            IEnumerable<Lane> lanes = target.Slot is null ? _state : ((Floats)((ValueBinding)_bindings[target.Slot]).Value).Lanes;
            return new Floats(lanes.Skip(target.Start).Take(target.Count).ToImmutableArray(), target.Type);
        }

        bool Write(Target target, ImmutableArray<Lane> lanes)
        {
            if (lanes.Length != target.Count) return false;
            if (_frozen.Any(held => SymbolEqualityComparer.Default.Equals(held.Slot, target.Slot)
                && held.Start < target.Start + target.Count && target.Start < held.Start + held.Count)) return false;
            if (target.Slot is null) lanes.CopyTo(_state, target.Start);
            else
            {
                var held = (Floats)((ValueBinding)_bindings[target.Slot]).Value;
                var updated = held.Lanes.ToArray();
                lanes.CopyTo(updated, target.Start);
                _bindings[target.Slot] = new ValueBinding(new Floats(updated.ToImmutableArray(), held.Type));
            }
            return true;
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
                    return _bindings.TryGetValue(local.Local, out var held) && held is ValueBinding stored ? stored.Value : null;
                case IParameterReferenceOperation reference:
                    return _bindings.TryGetValue(reference.Parameter, out var binding) ? binding switch
                    {
                        ValueBinding value => value.Value,
                        ColumnBinding column => Load(column.Column, reference.Type!),
                        AliasBinding alias => Read(alias.Target),
                        _ => null
                    } : null;
                case IPropertyReferenceOperation property when property.Arguments.Length == 0 && property.Instance is { } instance:
                    var owner = Evaluate(instance);
                    if (owner is ResourcePath resource)
                    {
                        if (resource.Type is INamedTypeSymbol { Name: "Res", Arity: 1 } wrapper && wrapper.ContainingNamespace.ToDisplayString() == "Archevolt" && property.Property.Name == "Value")
                            return new ResourcePath(resource.Path + ".Value", wrapper.TypeArguments[0]);
                        return PlainProperty(property.Property) ? new ResourcePath(resource.Path + "." + Escape(property.Property.Name), property.Type!) : null;
                    }
                    return Slice(owner, property.Property, property.Type!);
                // A static readonly field cannot change after its type is initialized, so the kernel reads it once per block.
                case IFieldReferenceOperation { Field: { IsStatic: true, IsReadOnly: true } shared } when
                    (shared.Type.SpecialType == SpecialType.System_Single || VectorLanes(shared.Type) is not null) && compilation.IsSymbolAccessibleWithin(shared, method.ContainingType):
                    return new ResourcePath(TypeName(shared.ContainingType) + "." + Escape(shared.Name), shared.Type);
                case IFieldReferenceOperation field when !field.Field.IsStatic && field.Instance is { } instance:
                    var holder = Evaluate(instance);
                    if (holder is ResourcePath fieldResource)
                        return new ResourcePath(fieldResource.Path + "." + Escape(field.Field.Name), field.Type!);
                    return Slice(holder, field.Field, field.Type!);
                case IUnaryOperation { OperatorKind: UnaryOperatorKind.Plus, OperatorMethod: null } plus when plus.Type?.SpecialType == SpecialType.System_Single:
                    return Materialize(Evaluate(plus.Operand));
                case IUnaryOperation { OperatorKind: UnaryOperatorKind.Minus, OperatorMethod: null, IsLifted: false } minus when minus.Type?.SpecialType == SpecialType.System_Single:
                    return Materialize(Evaluate(minus.Operand)) is { Lanes.Length: 1 } negated ? new Floats([new NegateLane(negated.Lanes[0])], minus.Type) : null;
                case IUnaryOperation { OperatorKind: UnaryOperatorKind.Not, OperatorMethod: null, IsLifted: false } not:
                    return Evaluate(not.Operand) is Condition inverted ? new Condition(new NotLane(inverted.Mask)) : null;
                case IBinaryOperation { OperatorMethod: null, IsLifted: false } binary when Comparison(binary.OperatorKind) is { } comparison:
                    return Scalar(binary.LeftOperand) is { } smaller && Scalar(binary.RightOperand) is { } larger ? new Condition(new CompareLane(comparison, smaller, larger)) : null;
                case IBinaryOperation { OperatorMethod: null, IsLifted: false } binary when Logic(binary) is { } logic:
                    return Evaluate(binary.LeftOperand) is Condition first
                        && (binary.OperatorKind is BinaryOperatorKind.ConditionalAnd or BinaryOperatorKind.ConditionalOr ? Skippable(binary.RightOperand) : Evaluate(binary.RightOperand)) is Condition second
                        ? new Condition(new LogicLane(logic, first.Mask, second.Mask)) : null;
                case IBinaryOperation binary:
                    return Combine(binary.OperatorKind, binary.OperatorMethod, Evaluate(binary.LeftOperand), Evaluate(binary.RightOperand), binary.Type!, binary.IsLifted);
                case IConditionalOperation { IsRef: false } conditional:
                    return Evaluate(conditional.Condition) is Condition condition && Skippable(conditional.WhenTrue) is { } whenTrue && Skippable(conditional.WhenFalse) is { } whenFalse
                        ? Select(condition.Mask, Materialize(whenTrue) ?? whenTrue, Materialize(whenFalse) ?? whenFalse) : null;
                case IObjectCreationOperation creation when creation.Initializer is null && creation.Constructor is { } constructor:
                    return Construct(constructor, creation.Arguments, creation.Type!);
                case IWithOperation with when with.CloneMethod is null:
                    return With(with);
                case IInvocationOperation invocation:
                    return Call(invocation, out var returned) ? returned : null;
                default:
                    return null;
            }
        }

        // Evaluates an expression that the callback skips for some rows: an outcome of ?:, or the right operand of && or
        // ||. The kernel evaluates it for every row, which gives the same result only when the expression writes nothing.
        // A called method can write through a ref parameter.
        Symbolic? Skippable(IOperation? operation)
        {
            var state = (Lane[])_state.Clone();
            var bindings = new Dictionary<ISymbol, Binding>(_bindings, SymbolEqualityComparer.Default);
            var value = Evaluate(operation);
            return value is not null && state.SequenceEqual(_state)
                && bindings.All(entry => _bindings.TryGetValue(entry.Key, out var current) && Unchanged(entry.Value, current)) ? value : null;
        }

        static bool Unchanged(Binding before, Binding after) => before.Equals(after)
            || before is ValueBinding { Value: Floats held } && after is ValueBinding { Value: Floats kept } && held.Lanes.SequenceEqual(kept.Lanes);

        Lane? Scalar(IOperation operand) =>
            operand.Type?.SpecialType == SpecialType.System_Single && Materialize(Evaluate(operand)) is { Lanes.Length: 1 } value ? value.Lanes[0] : null;

        static string? Comparison(BinaryOperatorKind kind) => kind switch
        {
            BinaryOperatorKind.LessThan => "<",
            BinaryOperatorKind.LessThanOrEqual => "<=",
            BinaryOperatorKind.GreaterThan => ">",
            BinaryOperatorKind.GreaterThanOrEqual => ">=",
            BinaryOperatorKind.Equals => "==",
            BinaryOperatorKind.NotEquals => "!=",
            _ => null
        };

        // The kernel evaluates both operands of && and ||. Both are built from comparisons of floats, which cannot fail.
        static char? Logic(IBinaryOperation binary) => binary.Type?.SpecialType != SpecialType.System_Boolean ? null : binary.OperatorKind switch
        {
            BinaryOperatorKind.ConditionalAnd or BinaryOperatorKind.And => '&',
            BinaryOperatorKind.ConditionalOr or BinaryOperatorKind.Or => '|',
            _ => null
        };

        // A call either names one of the float functions that have an identical vector form, or it is followed into a
        // static method of this compilation. By-value and in arguments bind the callee's parameters to values. A ref
        // argument binds the parameter to the caller's floats. A [Conditional] method is not followed, because the
        // compiler removes the call where its symbol is not defined.
        bool Call(IInvocationOperation invocation, out Symbolic? result)
        {
            result = null;
            var callee = invocation.TargetMethod;
            if (Function(invocation) is { } function)
            {
                result = function;
                return true;
            }
            if (!callee.IsStatic || callee.MethodKind != MethodKind.Ordinary || callee.IsGenericMethod || callee.IsExtern || callee.IsAsync
                || callee.ReturnsByRef || callee.ReturnsByRefReadonly || Conditional(callee) || callee.DeclaringSyntaxReferences.Length != 1 || _calls.Count >= MaximumCallDepth) return false;
            if (callee.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken) is not MethodDeclarationSyntax declaration
                || compilation.GetSemanticModel(declaration.SyntaxTree).GetOperation(declaration, cancellationToken) is not IMethodBodyOperation body) return false;
            var arguments = new List<(IParameterSymbol Parameter, Binding Binding)>();
            int frozen = _frozen.Count;
            foreach (var argument in invocation.Arguments)
            {
                if (argument.Parameter is not { } parameter || argument.ArgumentKind is not (ArgumentKind.Explicit or ArgumentKind.DefaultValue)) return false;
                if (parameter.RefKind == RefKind.Ref)
                {
                    if (Resolve(argument.Value) is not { } target) return false;
                    arguments.Add((parameter, new AliasBinding(target)));
                }
                else if (parameter.RefKind is RefKind.None or RefKind.In or RefKind.RefReadOnlyParameter)
                {
                    if (Evaluate(argument.Value) is not { } value) return false;
                    arguments.Add((parameter, new ValueBinding(Materialize(value) ?? value)));
                    if (parameter.RefKind != RefKind.None && Resolve(argument.Value) is { } read) _frozen.Add(read);
                }
                else return false;
            }
            if (!_calls.Add(callee)) return false;
            foreach (var (parameter, binding) in arguments) _bindings[parameter] = binding;
            bool completed = Body(body, out result) && (callee.ReturnsVoid || result is not null);
            // The callee's parameters and locals go out of scope, so a later call of the same method starts without them.
            foreach (var symbol in _bindings.Keys.Where(symbol => SymbolEqualityComparer.Default.Equals(symbol.ContainingSymbol, callee)).ToArray()) _bindings.Remove(symbol);
            _frozen.RemoveRange(frozen, _frozen.Count - frozen);
            _calls.Remove(callee);
            return completed;
        }

        Floats? Function(IInvocationOperation invocation)
        {
            var callee = invocation.TargetMethod;
            if (!callee.IsStatic || callee.Name is not ("Abs" or "Sqrt" or "Min" or "Max") || callee.ContainingType.ToDisplayString() is not ("System.MathF" or "System.Math" or "float")
                || callee.ReturnType.SpecialType != SpecialType.System_Single || callee.Parameters.Length != (callee.Name is "Min" or "Max" ? 2 : 1)
                || callee.Parameters.Any(static parameter => parameter.Type.SpecialType != SpecialType.System_Single)) return null;
            var arguments = new Lane?[2];
            foreach (var argument in invocation.Arguments)
            {
                if (argument.Parameter is not { } parameter || Scalar(argument.Value) is not { } value) return null;
                arguments[parameter.Ordinal] = value;
            }
            return arguments[0] is { } first && (callee.Parameters.Length == 1 || arguments[1] is not null)
                ? new Floats([new FunctionLane(callee.Name, first, arguments[1])], callee.ReturnType) : null;
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

        Floats? Combine(BinaryOperatorKind kind, IMethodSymbol? operatorMethod, Symbolic? left, Symbolic? right, ITypeSymbol type, bool isLifted)
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

    static int? VectorLanes(ITypeSymbol type) => type is INamedTypeSymbol { IsGenericType: false, DeclaringSyntaxReferences.Length: 0 } named && named.ContainingNamespace.ToDisplayString() == "System.Numerics"
        ? named.Name switch { "Vector2" => 2, "Vector3" => 3, "Vector4" => 4, _ => null } : null;

    static int Bits(float value) => BitConverter.ToInt32(BitConverter.GetBytes(value), 0);

    const string Vector128Type = "global::System.Runtime.Intrinsics.Vector128";
    const string Vector256Type = "global::System.Runtime.Intrinsics.Vector256";
    const string Marshal = "global::System.Runtime.InteropServices.MemoryMarshal";

    static string FloatLiteral(int bits) => "global::System.BitConverter.Int32BitsToSingle(unchecked((int)0x" + bits.ToString("X8", CultureInfo.InvariantCulture) + "))";

    static string KernelParameter(int column) => "__column" + column;

    static void AppendKernelMembers(StringBuilder text, KernelModel kernel)
    {
        // A row kernel holds whole rows in a vector, so a row wider than four floats needs 256-bit vectors.
        bool narrow = kernel.Flat || kernel.Lanes <= 4;
        text.Append("static readonly bool __kernel = ").Append(narrow ? Vector128Type : Vector256Type).AppendLine(".IsHardwareAccelerated && __Probe();");
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
        new KernelLoop(kernel, Vector256Type, 8).Append(text, first: true);
        if (narrow) new KernelLoop(kernel, Vector128Type, 4).Append(text, first: false);
        if (kernel.Flat)
        {
            text.AppendLine("for (; __index < __count; __index++)");
            text.Append(KernelParameter(kernel.Written)).Append("[__index] = ").Append(ScalarExpression(kernel, kernel.Formula[0], load => KernelParameter(load.Column) + "[__index]")).AppendLine(";");
        }
        else
        {
            // Every float of a row is computed before any is stored, because a formula may read the others.
            var assigned = Enumerable.Range(0, kernel.Lanes).Where(lane => !kernel.Formula[lane].Equals(new LoadLane(kernel.Written, lane))).ToArray();
            text.Append("for (; __index < __count; __index += ").Append(kernel.Lanes).AppendLine(")\n{");
            foreach (var lane in assigned)
                text.Append("float __row").Append(lane).Append(" = ").Append(ScalarExpression(kernel, kernel.Formula[lane], load => $"{KernelParameter(load.Column)}[__index + {load.Offset}]")).AppendLine(";");
            foreach (var lane in assigned)
                text.Append(KernelParameter(kernel.Written)).Append("[__index + ").Append(lane).Append("] = __row").Append(lane).AppendLine(";");
            text.AppendLine("}");
        }
        text.AppendLine("}");
    }

    // Writes the loop for one vector width. Each vector lane has its own formula. Lanes whose formulas have the same
    // operation share one vector instruction, with a per-lane constant, shuffle, or mask where they differ. Lanes whose
    // operations differ are computed apart and blended.
    sealed class KernelLoop(KernelModel kernel, string vector, int width)
    {
        readonly List<string> _constants = [];
        readonly Dictionary<string, string> _names = new(StringComparer.Ordinal);
        readonly StringBuilder _body = new();
        readonly SortedSet<int> _loads = [];

        // A row kernel advances by whole rows. The lanes past the last whole row keep the values they loaded, and the
        // next iteration computes them.
        int Stride => kernel.Flat ? width : width / kernel.Lanes * kernel.Lanes;

        public void Append(StringBuilder text, bool first)
        {
            var lanes = new Lane?[width];
            for (int lane = 0; lane < width; lane++)
            {
                int position = lane;
                int row = lane - lane % kernel.Lanes;
                lanes[lane] = kernel.Flat ? Rewrite(kernel.Formula[0], load => load with { Offset = position })
                    : lane < Stride ? Rewrite(kernel.Formula[lane % kernel.Lanes], load => load with { Offset = row + load.Offset })
                    : new LoadLane(kernel.Written, lane);
            }
            string result = Emit(lanes);
            text.Append(first ? "if (" : "else if (").Append(vector).AppendLine(".IsHardwareAccelerated)\n{");
            foreach (var constant in _constants) text.AppendLine(constant);
            text.Append("for (; __index <= __count - ").Append(width).Append("; __index += ").Append(Stride).AppendLine(")\n{");
            foreach (var column in _loads) text.Append("var __load").Append(column).Append(" = ").Append(vector).Append(".LoadUnsafe(ref __base").Append(column).AppendLine(", (nuint)__index);");
            text.Append(_body);
            text.Append(vector).Append(".StoreUnsafe(").Append(result).Append(", ref __base").Append(kernel.Written).AppendLine(", (nuint)__index);");
            text.AppendLine("}\n}");
        }

        string Constant(string expression) => Name(expression, "__k", declaration => _constants.Add(declaration));
        string Value(string expression) => Name(expression, "__t", declaration => _body.AppendLine(declaration));

        string Name(string expression, string prefix, Action<string> declare)
        {
            if (_names.TryGetValue(expression, out var name)) return name;
            name = prefix + _names.Count.ToString(CultureInfo.InvariantCulture);
            _names.Add(expression, name);
            declare("var " + name + " = " + expression + ";");
            return name;
        }

        string Create(IEnumerable<string> elements)
        {
            var values = elements.ToArray();
            return Constant(vector + ".Create(" + (values.Distinct().Count() == 1 ? values[0] : string.Join(", ", values)) + ")");
        }

        // A null lane may hold any value. Its result is discarded by a blend further up.
        string Emit(Lane?[] lanes)
        {
            var shapes = lanes.Where(static lane => lane is not null).Select(static lane => Shape(lane!)).Distinct().ToArray();
            if (shapes.Length > 1)
            {
                string? blended = null;
                foreach (var shape in shapes)
                {
                    var group = lanes.Select(lane => lane is not null && Shape(lane) == shape ? lane : null).ToArray();
                    string value = Emit(group);
                    blended = blended is null ? value
                        : Value(vector + ".ConditionalSelect(" + vector + ".AsSingle(" + Create(group.Select(static lane => lane is null ? "0" : "-1")) + "), " + value + ", " + blended + ")");
                }
                return blended!;
            }
            var first = lanes.First(static lane => lane is not null)!;
            switch (first)
            {
                case ConstantLane or UniformLane:
                    return Create(lanes.Select(lane => Leaf(lane ?? first)));
                case LoadLane load:
                    _loads.Add(load.Column);
                    var sources = lanes.Select(static (lane, position) => lane is LoadLane source ? source.Offset : position).ToArray();
                    return sources.Select(static (source, position) => source == position).All(static same => same) ? "__load" + load.Column
                        : Value(vector + ".Shuffle(__load" + load.Column + ", " + Create(sources.Select(static source => source.ToString(CultureInfo.InvariantCulture))) + ")");
                default:
                    var arguments = Enumerable.Range(0, Children(first).Length)
                        .Select(child => Emit(lanes.Select(lane => lane is null ? null : Children(lane)[child]).ToArray())).ToArray();
                    return Value(Operation(first, arguments));
            }
        }

        string Leaf(Lane lane) => lane is ConstantLane constant ? FloatLiteral(constant.Bits) : "__uniform" + kernel.Uniforms.IndexOf(((UniformLane)lane).Expression);

        static string Shape(Lane lane) => lane switch
        {
            ConstantLane or UniformLane => "leaf",
            LoadLane load => "load" + load.Column,
            BinaryLane binary => "binary" + binary.Operator,
            FunctionLane function => "function" + function.Name,
            CompareLane compare => "compare" + compare.Operator,
            LogicLane logic => "logic" + logic.Operator,
            _ => lane.GetType().Name
        };

        string Operation(Lane lane, string[] arguments) => lane switch
        {
            // Flipping the sign bit is what scalar negation does, for NaN and zero too.
            NegateLane => vector + ".Xor(" + arguments[0] + ", " + Create([FloatLiteral(unchecked((int)0x80000000))]) + ")",
            BinaryLane binary => "(" + arguments[0] + " " + binary.Operator + " " + arguments[1] + ")",
            FunctionLane function => vector + "." + function.Name + "(" + string.Join(", ", arguments) + ")",
            SelectLane => vector + ".ConditionalSelect(" + string.Join(", ", arguments) + ")",
            CompareLane { Operator: "!=" } => "~" + vector + ".Equals(" + arguments[0] + ", " + arguments[1] + ")",
            CompareLane compare => vector + "." + compare.Operator switch
            {
                "<" => "LessThan", "<=" => "LessThanOrEqual", ">" => "GreaterThan", ">=" => "GreaterThanOrEqual", _ => "Equals"
            } + "(" + arguments[0] + ", " + arguments[1] + ")",
            LogicLane logic => "(" + arguments[0] + " " + logic.Operator + " " + arguments[1] + ")",
            NotLane => "~" + arguments[0],
            _ => throw new InvalidOperationException()
        };
    }

    static string ScalarExpression(KernelModel kernel, Lane lane, Func<LoadLane, string> load)
    {
        var arguments = Children(lane).Select(child => ScalarExpression(kernel, child, load)).ToArray();
        return lane switch
        {
            ConstantLane constant => FloatLiteral(constant.Bits),
            UniformLane uniform => "__uniform" + kernel.Uniforms.IndexOf(uniform.Expression),
            LoadLane leaf => load(leaf),
            NegateLane => "-(" + arguments[0] + ")",
            BinaryLane binary => "(" + arguments[0] + " " + binary.Operator + " " + arguments[1] + ")",
            FunctionLane function => "global::System.MathF." + function.Name + "(" + string.Join(", ", arguments) + ")",
            SelectLane => "(" + arguments[0] + " ? " + arguments[1] + " : " + arguments[2] + ")",
            CompareLane compare => "(" + arguments[0] + " " + compare.Operator + " " + arguments[1] + ")",
            LogicLane logic => "(" + arguments[0] + " " + logic.Operator + " " + arguments[1] + ")",
            NotLane => "!(" + arguments[0] + ")",
            _ => throw new InvalidOperationException()
        };
    }

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
