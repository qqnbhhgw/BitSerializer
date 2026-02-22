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

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(BitFieldMissingCodeFixProvider)), Shared]
public class BitFieldMissingCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create("BITS009");

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics[0];
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var node = root.FindToken(diagnosticSpan.Start).Parent;
        if (node is null) return;

        var memberDecl = node.AncestorsAndSelf()
            .OfType<MemberDeclarationSyntax>()
            .FirstOrDefault(n => n is PropertyDeclarationSyntax or FieldDeclarationSyntax);
        if (memberDecl is null) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Add [BitField] attribute",
                createChangedDocument: ct => AddBitFieldAttributeAsync(context.Document, memberDecl, ct),
                equivalenceKey: "AddBitFieldAttribute"),
            diagnostic);
    }

    private static async Task<Document> AddBitFieldAttributeAsync(
        Document document, MemberDeclarationSyntax memberDecl, CancellationToken ct)
    {
        var attribute = SyntaxFactory.Attribute(SyntaxFactory.IdentifierName("BitField"));
        var attributeList = SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(attribute));

        // Find the first attribute list to match its formatting
        var existingAttrList = memberDecl.AttributeLists.FirstOrDefault();
        if (existingAttrList != null)
        {
            // Insert before existing attribute lists, matching the leading trivia
            attributeList = attributeList
                .WithLeadingTrivia(existingAttrList.GetLeadingTrivia())
                .WithTrailingTrivia(SyntaxFactory.Space);

            var newAttributeLists = memberDecl.AttributeLists.Insert(0, attributeList);
            var newMemberDecl = memberDecl.WithAttributeLists(newAttributeLists);

            var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
            var newRoot = root!.ReplaceNode(memberDecl, newMemberDecl);
            return document.WithSyntaxRoot(newRoot);
        }
        else
        {
            // No existing attributes - add with proper formatting
            attributeList = attributeList.WithTrailingTrivia(SyntaxFactory.Space);
            var newAttributeLists = SyntaxFactory.SingletonList(attributeList);
            var newMemberDecl = memberDecl.WithAttributeLists(newAttributeLists);

            var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
            var newRoot = root!.ReplaceNode(memberDecl, newMemberDecl);
            return document.WithSyntaxRoot(newRoot);
        }
    }
}
