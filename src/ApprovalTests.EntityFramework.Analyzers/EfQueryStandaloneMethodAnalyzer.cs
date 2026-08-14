using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ApprovalTests.EntityFramework.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class EfQueryStandaloneMethodAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "ENTITYFRAMEWORKAPPROVALS001";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "EF query should be built in a standalone method",
        messageFormat:
            "EF query should be built in a standalone method so it can be approval-tested on its own — extract it out of '{0}'.",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeLocalFunction, SyntaxKind.LocalFunctionStatement);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var method = (MethodDeclarationSyntax)context.Node;
        SyntaxNode? body = (SyntaxNode?)method.Body ?? method.ExpressionBody;
        if (body is null || IsStandalone(method.ReturnType, context.SemanticModel))
        {
            return;
        }

        var queryNode = EfQueryAnalyzerHelpers.FindEfQuery(body, context.SemanticModel);
        if (queryNode is not null && !EfQueryAnalyzerHelpers.IsSuppressedAt(queryNode, DiagnosticId))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, method.Identifier.GetLocation(), method.Identifier.Text));
        }
    }

    private static void AnalyzeLocalFunction(SyntaxNodeAnalysisContext context)
    {
        var function = (LocalFunctionStatementSyntax)context.Node;
        SyntaxNode? body = (SyntaxNode?)function.Body ?? function.ExpressionBody;
        if (body is null || IsStandalone(function.ReturnType, context.SemanticModel))
        {
            return;
        }

        var queryNode = EfQueryAnalyzerHelpers.FindEfQuery(body, context.SemanticModel);
        if (queryNode is not null && !EfQueryAnalyzerHelpers.IsSuppressedAt(queryNode, DiagnosticId))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, function.Identifier.GetLocation(), function.Identifier.Text));
        }
    }

    private static bool IsStandalone(TypeSyntax returnType, SemanticModel semanticModel)
    {
        var type = semanticModel.GetTypeInfo(returnType).Type;
        return type is INamedTypeSymbol { IsGenericType: true } named
            && named.ConstructedFrom.MetadataName == "IQueryable`1"
            && named.ConstructedFrom.ContainingNamespace.ToDisplayString() == "System.Linq";
    }

}
