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
    /// directive, but these analyzers report at the method identifier (so the message reads
    /// naturally), which usually precedes the query itself. Emulate suppression by replaying
    /// the tree's pragma directives up to the query's position instead.
    /// </summary>
    public static bool IsSuppressedAt(SyntaxNode node, string diagnosticId)
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
