using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
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

        var queryNode = FindEfQuery(body, context.SemanticModel);
        if (queryNode is not null && !IsSuppressedAt(queryNode, DiagnosticId))
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

        var queryNode = FindEfQuery(body, context.SemanticModel);
        if (queryNode is not null && !IsSuppressedAt(queryNode, DiagnosticId))
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

    /// <summary>
    /// Looks for a member access resolving to a DbSet&lt;T&gt; property/field, which marks the
    /// method as containing an EF query root. Nested local functions are skipped: they are
    /// analyzed (and, if warranted, flagged) on their own via <see cref="AnalyzeLocalFunction"/>.
    /// </summary>
    private static SyntaxNode? FindEfQuery(SyntaxNode root, SemanticModel semanticModel)
    {
        foreach (var node in DescendantsExcludingNestedLocalFunctions(root))
        {
            if (node is not (MemberAccessExpressionSyntax or IdentifierNameSyntax))
            {
                continue;
            }

            var memberType = semanticModel.GetSymbolInfo(node).Symbol switch
            {
                IPropertySymbol property => property.Type,
                IFieldSymbol field => field.Type,
                _ => null
            };

            if (memberType is INamedTypeSymbol { IsGenericType: true } named
                && named.ConstructedFrom.MetadataName == "DbSet`1"
                && named.ConstructedFrom.ContainingNamespace.ToDisplayString() == "Microsoft.EntityFrameworkCore")
            {
                return node;
            }
        }

        return null;
    }

    /// <summary>
    /// Roslyn's own pragma suppression only covers diagnostics located after the disable
    /// directive, but this analyzer reports at the method identifier (so the message reads
    /// naturally), which usually precedes the query itself. Emulate suppression by replaying
    /// the tree's pragma directives up to the query's position instead.
    /// </summary>
    private static bool IsSuppressedAt(SyntaxNode node, string diagnosticId)
    {
        var disabled = false;
        foreach (var trivia in node.SyntaxTree.GetRoot().DescendantTrivia())
        {
            if (trivia.SpanStart > node.SpanStart)
            {
                break;
            }

            if (trivia.GetStructure() is not PragmaWarningDirectiveTriviaSyntax pragma)
            {
                continue;
            }

            var appliesToAll = pragma.ErrorCodes.Count == 0;
            var appliesToThis = appliesToAll || pragma.ErrorCodes.Any(code => code.ToString() == diagnosticId);
            if (appliesToThis)
            {
                disabled = pragma.DisableOrRestoreKeyword.IsKind(SyntaxKind.DisableKeyword);
            }
        }

        return disabled;
    }

    private static IEnumerable<SyntaxNode> DescendantsExcludingNestedLocalFunctions(SyntaxNode root)
    {
        foreach (var child in root.ChildNodes())
        {
            if (child is LocalFunctionStatementSyntax)
            {
                continue;
            }

            yield return child;

            foreach (var descendant in DescendantsExcludingNestedLocalFunctions(child))
            {
                yield return descendant;
            }
        }
    }
}
