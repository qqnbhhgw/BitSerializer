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

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(BitSerializePartialCodeFixProvider)), Shared]
public class BitSerializePartialCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create("BITS005");

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics[0];
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var declaration = root.FindToken(diagnosticSpan.Start).Parent?.AncestorsAndSelf()
            .OfType<TypeDeclarationSyntax>().FirstOrDefault();
        if (declaration is null) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Add 'partial' modifier",
                createChangedDocument: ct => AddPartialModifierAsync(context.Document, declaration, ct),
                equivalenceKey: "AddPartialModifier"),
            diagnostic);
    }

    private static async Task<Document> AddPartialModifierAsync(
        Document document, TypeDeclarationSyntax typeDecl, CancellationToken ct)
    {
        var partialToken = SyntaxFactory.Token(SyntaxKind.PartialKeyword)
            .WithTrailingTrivia(SyntaxFactory.Space);

        var newModifiers = typeDecl.Modifiers.Add(partialToken);
        var newTypeDecl = typeDecl.WithModifiers(newModifiers);

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        var newRoot = root!.ReplaceNode(typeDecl, newTypeDecl);
        return document.WithSyntaxRoot(newRoot);
    }
}
