using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace BitSerializer.Generator.Analysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class BitFieldMissingAnalyzer : DiagnosticAnalyzer
{
    private static readonly string[] HelperAttributeNames =
    {
        "BitSerializer.BitFieldCountAttribute",
        "BitSerializer.BitFieldRelatedAttribute",
        "BitSerializer.BitPolyAttribute"
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            DiagnosticDescriptors.MissingBitFieldWithHelperAttribute,
            DiagnosticDescriptors.UninitializedReferenceField);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeMember, SymbolKind.Property, SymbolKind.Field);
    }

    private static void AnalyzeMember(SymbolAnalysisContext context)
    {
        var symbol = context.Symbol;

        // Skip implicitly declared fields (e.g. backing fields)
        if (symbol is IFieldSymbol field && field.IsImplicitlyDeclared)
            return;

        var attributes = symbol.GetAttributes();
        if (attributes.IsEmpty)
            return;

        bool hasBitField = attributes.Any(a =>
            a.AttributeClass?.ToDisplayString() == "BitSerializer.BitFieldAttribute");

        bool hasBitIgnore = attributes.Any(a =>
            a.AttributeClass?.ToDisplayString() == "BitSerializer.BitIgnoreAttribute");
        if (hasBitIgnore)
            return;

        bool hasStringAttribute = attributes.Any(a =>
            a.AttributeClass?.ToDisplayString() == "BitSerializer.BitFixedStringAttribute" ||
            a.AttributeClass?.ToDisplayString() == "BitSerializer.BitTerminatedStringAttribute");

        // BITS009: helper attribute without [BitField]
        if (!hasBitField && !hasStringAttribute)
        {
            bool hasHelperAttribute = attributes.Any(a =>
                a.AttributeClass != null &&
                HelperAttributeNames.Contains(a.AttributeClass.ToDisplayString()));

            if (hasHelperAttribute)
            {
                var diagnostic = Diagnostic.Create(
                    DiagnosticDescriptors.MissingBitFieldWithHelperAttribute,
                    symbol.Locations.FirstOrDefault(),
                    symbol.Name);
                context.ReportDiagnostic(diagnostic);
            }
            return;
        }

        // BITS014: reference-type [BitField] member without default value
        if (!hasBitField && !hasStringAttribute)
            return;

        var memberType = symbol switch
        {
            IPropertySymbol prop => prop.Type,
            IFieldSymbol f => f.Type,
            _ => null
        };
        if (memberType == null || !memberType.IsReferenceType)
            return;

        // Skip if the member has a declarator initializer (= value)
        if (HasInitializer(symbol))
            return;

        // Skip if the member has the 'required' modifier
        if (HasRequiredModifier(symbol))
            return;

        // Skip if the member is assigned in any constructor of its containing type
        if (IsAssignedInConstructor(symbol))
            return;

        var containingTypeName = symbol.ContainingType?.Name ?? "";
        var diag = Diagnostic.Create(
            DiagnosticDescriptors.UninitializedReferenceField,
            symbol.Locations.FirstOrDefault(),
            symbol.Name, containingTypeName);
        context.ReportDiagnostic(diag);
    }

    private static bool HasInitializer(ISymbol symbol)
    {
        foreach (var syntaxRef in symbol.DeclaringSyntaxReferences)
        {
            var syntax = syntaxRef.GetSyntax();
            if (syntax is PropertyDeclarationSyntax propSyntax && propSyntax.Initializer != null)
                return true;
            if (syntax is VariableDeclaratorSyntax varSyntax && varSyntax.Initializer != null)
                return true;
        }
        return false;
    }

    private static bool HasRequiredModifier(ISymbol symbol)
    {
        foreach (var syntaxRef in symbol.DeclaringSyntaxReferences)
        {
            var syntax = syntaxRef.GetSyntax();
            // Property: required public string Name { get; set; }
            if (syntax is PropertyDeclarationSyntax propSyntax &&
                propSyntax.Modifiers.Any(SyntaxKind.RequiredKeyword))
                return true;
            // Field: required string _name;  — check the parent FieldDeclarationSyntax
            if (syntax is VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax { Parent: FieldDeclarationSyntax fieldDecl } } &&
                fieldDecl.Modifiers.Any(SyntaxKind.RequiredKeyword))
                return true;
        }
        return false;
    }

    private static bool IsAssignedInConstructor(ISymbol symbol)
    {
        var containingType = symbol.ContainingType;
        if (containingType == null)
            return false;

        var memberName = symbol.Name;

        foreach (var typeSyntaxRef in containingType.DeclaringSyntaxReferences)
        {
            var typeNode = typeSyntaxRef.GetSyntax();
            if (typeNode is not TypeDeclarationSyntax typeDecl)
                continue;

            foreach (var ctor in typeDecl.Members.OfType<ConstructorDeclarationSyntax>())
            {
                if (ctor.Body == null && ctor.ExpressionBody == null)
                    continue;

                // Look for assignments like: this.Name = ...; or Name = ...;
                var assignments = (ctor.Body?.DescendantNodes() ?? ctor.ExpressionBody!.DescendantNodes())
                    .OfType<AssignmentExpressionSyntax>();

                foreach (var assignment in assignments)
                {
                    var left = assignment.Left;
                    // Simple name: Name = ...
                    if (left is IdentifierNameSyntax id && id.Identifier.Text == memberName)
                        return true;
                    // Qualified: this.Name = ...
                    if (left is MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax, Name: IdentifierNameSyntax name }
                        && name.Identifier.Text == memberName)
                        return true;
                }
            }
        }

        return false;
    }
}
