using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Archevolt.Generators;

// In an optimized build the row loop of an [Each] system calls a copy of the callback marked AggressiveInlining. Without
// it the JIT inlines all but very small callbacks only when a profile shows the call is hot. A system whose first runs
// saw no rows has no such profile, and NativeAOT has none at all.
public sealed partial class ArchevoltGenerator
{
    sealed record InlinedCallback(string Name, string Member, string UnitUsings, string NamespaceUsings);

    // The copy is a member of the callback's type, and its file repeats the using directives of the callback's file, so
    // every name binds as it does in the callback. A callback gets no copy when that cannot be guaranteed, or when its
    // behavior depends on being the method that runs.
    static InlinedCallback? Inlined(SystemModel model, IMethodSymbol method, SemanticModel semantics, SyntaxNode syntax, CancellationToken cancellationToken)
    {
        if (model.Form != SystemForm.Each || semantics.Compilation.Options.OptimizationLevel != OptimizationLevel.Release) return null;
        if (syntax is not MethodDeclarationSyntax declaration) return null;
        var body = declaration.Body?.ToString() ?? (declaration.ExpressionBody is { } expression ? expression + ";" : null);
        if (body is null || HasDirective(declaration)) return null;
        var unit = declaration.SyntaxTree.GetCompilationUnitRoot(cancellationToken);
        var namespaces = declaration.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().ToArray();
        // Usings on an outer namespace declaration rank below the members of that namespace. The generated file has one
        // namespace declaration, where they would rank above those members and a name could bind to another symbol.
        if (unit.Externs.Count != 0 || namespaces.Any(static scope => scope.Externs.Count != 0) || namespaces.Skip(1).Any(static scope => scope.Usings.Count != 0)) return null;
        // A file-local type can reach the body without its name appearing there, through an extension method or a
        // using static, so one anywhere in the file rules the copy out.
        if (DeclaresFileLocalType(unit)) return null;
        // The copy carries none of the callback's attributes. It would run where a [Conditional] callback is compiled away, would not take
        // the lock of a Synchronized one, and would be inlined against NoInlining or NoOptimization.
        if ((method.MethodImplementationFlags & (MethodImplAttributes.NoInlining | MethodImplAttributes.NoOptimization | MethodImplAttributes.Synchronized)) != 0
            || Conditional(method)) return null;
        // Caller information would name the copy and the generated file.
        if (semantics.GetOperation(declaration, cancellationToken) is not { } operation || operation.Descendants().OfType<IArgumentOperation>().Any(CallerInformation)) return null;
        bool unsafeContext = declaration.Modifiers.Any(SyntaxKind.UnsafeKeyword)
            || declaration.Ancestors().OfType<TypeDeclarationSyntax>().Any(static type => type.Modifiers.Any(SyntaxKind.UnsafeKeyword));
        var name = "__Archevolt_" + method.Name + "_Inlined";
        var member = Inline + "\nprivate static " + (unsafeContext ? "unsafe " : "") + "void " + name + declaration.ParameterList + "\n" + body + "\n";
        // A global using already reaches the generated file.
        return new(name, Quiet(member), Quiet(Lines(unit.Usings.Where(static directive => !directive.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword)))),
            Quiet(Lines(namespaces.Take(1).SelectMany(static scope => scope.Usings))));
    }

    static bool HasDirective(SyntaxNode node) => node.ContainsDirectives && node.DescendantTrivia().Any(trivia => trivia.IsDirective && node.Span.Contains(trivia.Span));

    static bool DeclaresFileLocalType(SyntaxNode scope) => scope.ChildNodes().Any(static member => member is BaseNamespaceDeclarationSyntax
        ? DeclaresFileLocalType(member) : member is MemberDeclarationSyntax declaration && declaration.Modifiers.Any(SyntaxKind.FileKeyword));

    static bool CallerInformation(IArgumentOperation argument) => argument.ArgumentKind == ArgumentKind.DefaultValue && argument.Parameter is { } parameter
        && parameter.GetAttributes().Any(static attribute => attribute.AttributeClass is { Name: "CallerMemberNameAttribute" or "CallerFilePathAttribute" or "CallerLineNumberAttribute" } type
            && type.ContainingNamespace.ToDisplayString() == "System.Runtime.CompilerServices");

    static string Lines(IEnumerable<UsingDirectiveSyntax> usings) => string.Concat(usings.Select(static directive => directive + "\n"));

    // The generated file enables nullable analysis, and no #pragma of the callback's file reaches it.
    static string Quiet(string text) => text.Length == 0 ? "" : "#pragma warning disable\n" + text + "#pragma warning restore\n";
}
