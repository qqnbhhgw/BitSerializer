using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BitSerializer.Generator;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UninitializedFieldCodeFixProvider)), Shared]
public class UninitializedFieldCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create("BITS014");

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics[0];
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var node = root.FindToken(diagnosticSpan.Start).Parent;
        if (node is null) return;

        var propDecl = node.AncestorsAndSelf().OfType<PropertyDeclarationSyntax>().FirstOrDefault();
        if (propDecl is null) return;

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (semanticModel is null) return;

        var propSymbol = semanticModel.GetDeclaredSymbol(propDecl, context.CancellationToken) as IPropertySymbol;
        if (propSymbol is null) return;

        var initializer = GetDefaultInitializer(propSymbol.Type);
        if (initializer is null) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: $"Initialize with '{initializer}'",
                createChangedDocument: ct => AddInitializerAsync(context.Document, propDecl, initializer, ct),
                equivalenceKey: "AddDefaultInitializer"),
            diagnostic);
    }

    private static string? GetDefaultInitializer(ITypeSymbol type)
    {
        // string -> ""
        if (type.SpecialType == SpecialType.System_String)
            return "\"\"";

        // Array -> []
        if (type is IArrayTypeSymbol)
            return "[]";

        // List<T> -> []
        if (type is INamedTypeSymbol named && named.IsGenericType &&
            named.ConstructedFrom.ToDisplayString() == "System.Collections.Generic.List<T>")
            return "[]";

        // Other reference types: only offer fix for concrete types with accessible parameterless ctor
        if (type is INamedTypeSymbol namedType && type.IsReferenceType)
        {
            // Exclude interfaces, abstract classes, and static classes
            if (namedType.TypeKind == TypeKind.Interface ||
                namedType.TypeKind == TypeKind.Delegate ||
                namedType.IsAbstract ||
                namedType.IsStatic)
                return null;

            // Check for accessible parameterless constructor
            bool hasAccessibleDefaultCtor = namedType.InstanceConstructors.Any(c =>
                c.Parameters.Length == 0 &&
                c.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal);

            if (!hasAccessibleDefaultCtor)
                return null;

            var fullName = namedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return $"new {fullName}()";
        }

        return null;
    }

    private static async Task<Document> AddInitializerAsync(
        Document document, PropertyDeclarationSyntax propDecl, string initializer, CancellationToken ct)
    {
        var initializerExpr = SyntaxFactory.ParseExpression(initializer);
        var equalsClause = SyntaxFactory.EqualsValueClause(initializerExpr)
            .WithLeadingTrivia(SyntaxFactory.Space);

        var newPropDecl = propDecl
            .WithInitializer(equalsClause)
            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        var newRoot = root!.ReplaceNode(propDecl, newPropDecl);
        return document.WithSyntaxRoot(newRoot);
    }
}
