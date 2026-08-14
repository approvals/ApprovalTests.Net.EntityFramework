using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ApprovalTests.EntityFramework.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class EfQueryableModelTypeAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "ENTITYFRAMEWORKAPPROVALS002";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "EF query should return a mapped entity type",
        messageFormat:
            "'{0}' returns IQueryable<{1}>, but '{1}' is not an EF model type — approval-test the entity query directly instead of projecting.",
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
        Analyze(context, body, method.ReturnType, method.Identifier);
    }

    private static void AnalyzeLocalFunction(SyntaxNodeAnalysisContext context)
    {
        var function = (LocalFunctionStatementSyntax)context.Node;
        SyntaxNode? body = (SyntaxNode?)function.Body ?? function.ExpressionBody;
        Analyze(context, body, function.ReturnType, function.Identifier);
    }

    private static void Analyze(
        SyntaxNodeAnalysisContext context,
        SyntaxNode? body,
        TypeSyntax returnType,
        SyntaxToken identifier)
    {
        if (body is null)
        {
            return;
        }

        var elementType = GetQueryableElementType(returnType, context.SemanticModel);
        if (elementType is null)
        {
            return;
        }

        var queryNode = FindEfQuery(body, context.SemanticModel);
        if (queryNode is null || IsSuppressedAt(queryNode, DiagnosticId))
        {
            return;
        }

        var mappedEntityTypes = GetMappedEntityTypes(context.SemanticModel.Compilation);
        if (mappedEntityTypes.Contains(elementType))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            identifier.GetLocation(),
            identifier.Text,
            elementType.ToDisplayString()));
    }

    private static ITypeSymbol? GetQueryableElementType(TypeSyntax returnType, SemanticModel semanticModel)
    {
        var type = semanticModel.GetTypeInfo(returnType).Type;
        if (type is INamedTypeSymbol { IsGenericType: true } named
            && named.ConstructedFrom.MetadataName == "IQueryable`1"
            && named.ConstructedFrom.ContainingNamespace.ToDisplayString() == "System.Linq")
        {
            return named.TypeArguments[0];
        }

        return null;
    }

    private static ImmutableHashSet<ITypeSymbol> GetMappedEntityTypes(Compilation compilation)
    {
        var dbContextType = compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbContext");
        var builder = ImmutableHashSet.CreateBuilder<ITypeSymbol>(SymbolEqualityComparer.Default);
        if (dbContextType is null)
        {
            return builder.ToImmutable();
        }

        foreach (var type in GetAllTypes(compilation.GlobalNamespace))
        {
            if (!InheritsFrom(type, dbContextType))
            {
                continue;
            }

            foreach (var member in type.GetMembers())
            {
                var memberType = member switch
                {
                    IPropertySymbol property => property.Type,
                    IFieldSymbol field => field.Type,
                    _ => null
                };

                if (memberType is INamedTypeSymbol { IsGenericType: true } named
                    && named.ConstructedFrom.MetadataName == "DbSet`1"
                    && named.ConstructedFrom.ContainingNamespace.ToDisplayString() == "Microsoft.EntityFrameworkCore")
                {
                    builder.Add(named.TypeArguments[0]);
                }
            }
        }

        return builder.ToImmutable();
    }

    private static bool InheritsFrom(ITypeSymbol type, INamedTypeSymbol baseType)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<INamedTypeSymbol> GetAllTypes(INamespaceSymbol root)
    {
        foreach (var member in root.GetMembers())
        {
            switch (member)
            {
                case INamespaceSymbol ns:
                    foreach (var nested in GetAllTypes(ns))
                    {
                        yield return nested;
                    }

                    break;
                case INamedTypeSymbol type:
                    yield return type;
                    foreach (var nested in type.GetTypeMembers())
                    {
                        yield return nested;
                    }

                    break;
            }
        }
    }

    /// <summary>
    /// Looks for a member access resolving to a DbSet&lt;T&gt; property/field, which marks the
    /// method as containing an EF query root. Nested local functions are skipped: they are
    /// analyzed (and, if warranted, flagged) on their own.
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
