using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
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
        ImmutableArray.Create(DiagnosticDescriptors.MissingBitFieldWithHelperAttribute);

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
        if (hasBitField)
            return;

        bool hasBitIgnore = attributes.Any(a =>
            a.AttributeClass?.ToDisplayString() == "BitSerializer.BitIgnoreAttribute");
        if (hasBitIgnore)
            return;

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
    }
}
