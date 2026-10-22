using System.Collections;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Archevolt.Generators;

internal readonly struct ValueArray<T>(ImmutableArray<T> items) : IEquatable<ValueArray<T>>, IEnumerable<T>
{
    readonly ImmutableArray<T> _items = items.IsDefault ? ImmutableArray<T>.Empty : items;
    public bool Equals(ValueArray<T> other) => this.SequenceEqual(other);
    public override bool Equals(object? other) => other is ValueArray<T> array && Equals(array);
    public override int GetHashCode()
    {
        var hash = 17;
        foreach (var item in this) hash = unchecked(hash * 31 + (item?.GetHashCode() ?? 0));
        return hash;
    }
    public IEnumerator<T> GetEnumerator() => (_items.IsDefault ? ImmutableArray<T>.Empty : _items).AsEnumerable().GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

internal sealed record Problem(string Id, string Message, string Path, TextSpan Span, LinePositionSpan Lines)
{
    public Diagnostic Diagnostic => Microsoft.CodeAnalysis.Diagnostic.Create(Diagnostics.Get(Id), Location.Create(Path, Span, Lines), Message);
}

internal sealed record GeneratedUnit(string Hint, string Source, ValueArray<Problem> Problems);
internal sealed record ComponentModel(string Type, string Name, bool Tracked, bool Tag, bool OwnerThread = false, bool Managed = false);
internal sealed record TermModel(ComponentModel Component, string Mode, int Index, string Alias);
internal sealed record ParameterModel(string Type, string Name, string Kind, string RefKind, int Index, ComponentModel? Component);

internal static class Diagnostics
{
    static readonly Dictionary<string, DiagnosticDescriptor> Descriptors = Enumerable.Range(1, 11).ToDictionary(
        static index => $"AV{index:000}",
        static index => new DiagnosticDescriptor($"AV{index:000}", "Invalid Archevolt declaration", "{0}", "Archevolt",
            index == 8 ? DiagnosticSeverity.Warning : DiagnosticSeverity.Error, isEnabledByDefault: true));
    public static DiagnosticDescriptor Get(string id) => Descriptors[id];
}
