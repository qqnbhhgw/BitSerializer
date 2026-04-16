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

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (semanticModel is null) return;

        // Case 1: property { get; set; }
        var propDecl = node.AncestorsAndSelf().OfType<PropertyDeclarationSyntax>().FirstOrDefault();
        if (propDecl is not null)
        {
            if (semanticModel.GetDeclaredSymbol(propDecl, context.CancellationToken) is not IPropertySymbol propSymbol)
                return;

            var initializer = GetDefaultInitializer(propSymbol.Type);
            if (initializer is null) return;

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: $"Initialize with '{initializer}'",
                    createChangedDocument: ct => AddPropertyInitializerAsync(context.Document, propDecl, initializer, ct),
                    equivalenceKey: "AddDefaultInitializer"),
                diagnostic);
            return;
        }

        // Case 2: field declaration (public string Name;)
        var declarator = node.AncestorsAndSelf().OfType<VariableDeclaratorSyntax>().FirstOrDefault();
        if (declarator is not null && declarator.Parent is VariableDeclarationSyntax { Parent: FieldDeclarationSyntax })
        {
            if (semanticModel.GetDeclaredSymbol(declarator, context.CancellationToken) is not IFieldSymbol fieldSymbol)
                return;

            var initializer = GetDefaultInitializer(fieldSymbol.Type);
            if (initializer is null) return;

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: $"Initialize with '{initializer}'",
                    createChangedDocument: ct => AddFieldInitializerAsync(context.Document, declarator, initializer, ct),
                    equivalenceKey: "AddDefaultInitializer"),
                diagnostic);
        }
    }

    private static string? GetDefaultInitializer(ITypeSymbol type)
    {
        // string -> ""
        if (type.SpecialType == SpecialType.System_String)
            return "\"\"";

        // T[] -> global::System.Array.Empty<T>()  (LangVersion-agnostic; avoids collection expression [])
        if (type is IArrayTypeSymbol arrayType)
        {
            var elem = arrayType.ElementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return $"global::System.Array.Empty<{elem}>()";
        }

        // List<T> -> new global::System.Collections.Generic.List<T>()
        if (type is INamedTypeSymbol named && named.IsGenericType &&
            named.ConstructedFrom.ToDisplayString() == "System.Collections.Generic.List<T>")
        {
            var elem = named.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return $"new global::System.Collections.Generic.List<{elem}>()";
        }

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

    private static async Task<Document> AddPropertyInitializerAsync(
        Document document, PropertyDeclarationSyntax propDecl, string initializer, CancellationToken ct)
    {
        var initializerExpr = SyntaxFactory.ParseExpression(initializer)
            .WithoutLeadingTrivia()
            .WithoutTrailingTrivia();

        var equalsClause = SyntaxFactory.EqualsValueClause(initializerExpr)
            .WithLeadingTrivia(SyntaxFactory.Space);

        // Try to keep "{ get; set; } = xxx;" on one line, but ONLY collapse whitespace/newline —
        // never drop comments or preprocessor directives that trail the close brace.
        var newPropDecl = propDecl;
        if (propDecl.AccessorList != null)
        {
            var closeBrace = propDecl.AccessorList.CloseBraceToken;
            var trailing = closeBrace.TrailingTrivia;
            bool hasSignificantTrivia = trailing.Any(t =>
                !t.IsKind(SyntaxKind.WhitespaceTrivia) &&
                !t.IsKind(SyntaxKind.EndOfLineTrivia));

            if (!hasSignificantTrivia)
            {
                var newCloseBrace = closeBrace.WithTrailingTrivia(SyntaxTriviaList.Empty);
                newPropDecl = newPropDecl.WithAccessorList(
                    propDecl.AccessorList.WithCloseBraceToken(newCloseBrace));
            }
            // Else: leave trivia (comments, directives) intact — initializer may land on next line,
            // which is ugly but preserves the user's source.
        }

        newPropDecl = newPropDecl
            .WithInitializer(equalsClause)
            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        var newRoot = root!.ReplaceNode(propDecl, newPropDecl);
        return document.WithSyntaxRoot(newRoot);
    }

    private static async Task<Document> AddFieldInitializerAsync(
        Document document, VariableDeclaratorSyntax declarator, string initializer, CancellationToken ct)
    {
        var initializerExpr = SyntaxFactory.ParseExpression(initializer)
            .WithoutLeadingTrivia()
            .WithoutTrailingTrivia();

        var equalsClause = SyntaxFactory.EqualsValueClause(initializerExpr)
            .WithLeadingTrivia(SyntaxFactory.Space)
            .WithEqualsToken(SyntaxFactory.Token(SyntaxFactory.TriviaList(), SyntaxKind.EqualsToken, SyntaxFactory.TriviaList(SyntaxFactory.Space)));

        var newDeclarator = declarator.WithInitializer(equalsClause);

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        var newRoot = root!.ReplaceNode(declarator, newDeclarator);
        return document.WithSyntaxRoot(newRoot);
    }
}
