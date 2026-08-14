using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ApprovalTests.EntityFramework.Analyzers;

internal static class EfQueryAnalyzerHelpers
{
    /// <summary>
    /// Looks for a member access resolving to a DbSet&lt;T&gt; property/field, which marks the
    /// method as containing an EF query root. Nested local functions are skipped: they are
    /// analyzed (and, if warranted, flagged) on their own.
    /// </summary>
    public static SyntaxNode? FindEfQuery(SyntaxNode root, SemanticModel semanticModel)
    {
        return DescendantsExcludingNestedLocalFunctions(root)
            .Where(node => node is MemberAccessExpressionSyntax or IdentifierNameSyntax)
            .FirstOrDefault(node => IsDbSetAccess(node, semanticModel));
    }

    private static bool IsDbSetAccess(SyntaxNode node, SemanticModel semanticModel)
    {
        var memberType = semanticModel.GetSymbolInfo(node).Symbol switch
        {
            IPropertySymbol property => property.Type,
            IFieldSymbol field => field.Type,
            _ => null
        };

        return memberType is INamedTypeSymbol { IsGenericType: true } named
            && named.ConstructedFrom.MetadataName == "DbSet`1"
            && named.ConstructedFrom.ContainingNamespace.ToDisplayString() == "Microsoft.EntityFrameworkCore";
    }

    /// <summary>
    /// Roslyn's own pragma suppression only covers diagnostics located after the disable
    /// directive, but these analyzers report at the method identifier (so the message reads
    /// naturally), which usually precedes the query itself. Emulate suppression by replaying
    /// the tree's pragma directives up to the query's position instead.
    /// </summary>
    public static bool IsSuppressedAt(SyntaxNode node, string diagnosticId)
    {
        return node.SyntaxTree.GetRoot().DescendantTrivia()
            .TakeWhile(trivia => trivia.SpanStart <= node.SpanStart)
            .Select(trivia => trivia.GetStructure())
            .OfType<PragmaWarningDirectiveTriviaSyntax>()
            .Where(pragma => pragma.ErrorCodes.Count == 0
                || pragma.ErrorCodes.Any(code => code.ToString() == diagnosticId))
            .Select(pragma => pragma.DisableOrRestoreKeyword.IsKind(SyntaxKind.DisableKeyword))
            .LastOrDefault();
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
