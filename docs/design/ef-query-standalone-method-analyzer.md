# Design: Analyzer to flag EF queries not in a standalone method

## Problem

`EntityFrameworkApprovals.Verify` / `VerifyQueryAsSql` approval-test a query by
handing it an `IQueryable<T>`. That only works well if the query was built by
a method whose entire job is "build and return this query" — see
`CreateCompanyLoaderByName2` in
[EntityFrameworkLoaderTest.cs](../../src/Tests/EntityFrameworkLoaderTest.cs):

```csharp
private IQueryable<Company> CreateCompanyLoaderByName2(ModelContainer db, string name)
{
    return (from c in db.Companies
        where c.Name.StartsWith(name)
        select c).Take(10);
}
```

When a query is instead built inline, in the middle of a larger method that
also enumerates it, mutates state, or mixes in other logic, it can't be lifted
out and approval-tested on its own without a refactor first. The goal of this
analyzer is to catch that pattern early and nudge the query into its own
method, so it's already in approval-testable shape.

## Goal

Warn when code constructs an EF Core query (a LINQ query rooted at a
`DbSet<T>` / `DbContext`) in a method that is not "standalone" for this
purpose — i.e. the method does more than build and return that query.

Non-goal: this is a style/testability nudge, not a correctness check. It
should be low-noise and easy to suppress for legitimate exceptions.

## Detecting "this is an EF query"

A query counts as an EF query if its root data source is EF-backed:

- The expression's ultimate source (walking back through the fluent chain /
  query-expression `from` clause) is a member access whose type is
  `DbSet<T>` (or implements `IQueryable<T>` and is a property/method on a
  type deriving from `Microsoft.EntityFrameworkCore.DbContext`).
- Use symbol analysis (`SemanticModel.GetTypeInfo` / `GetSymbolInfo`), not
  name matching, so this works regardless of DbContext subclass name.

Both query-syntax (`from x in db.Foos ...`) and method-syntax
(`db.Foos.Where(...).Select(...)`) should be recognized. In Roslyn terms:
walk `InvocationExpressionSyntax` chains and `QueryExpressionSyntax` /
`FromClauseSyntax`, and check the innermost source's `ITypeSymbol`.

## Detecting "not in a standalone method"

Given an EF query expression found somewhere in a method body, the containing
method is **standalone** (no diagnostic) 

- the method's declared return type is `IQueryable<T>`

Otherwise, flag the query expression's location. Concretely, this covers:

## Analyzer implementation sketch

- `DiagnosticAnalyzer`, registered via
  `RegisterSyntaxNodeAction` on `SyntaxKind.InvocationExpression` and
  `SyntaxKind.QueryExpression` (or simpler: register once on
  `SyntaxKind.MethodDeclaration` / `SyntaxKind.LocalFunctionStatement` /
  lambda bodies, find EF query roots inside, then check the shape).
- Diagnostic ID: `ENTITYFRAMEWORKAPPROVALS001`,
  category `Design`, default severity `Warning`.
- Message: `"EF query should be built in a standalone method (or
  LoaderUtils.Load lambda) so it can be approval-tested on its own — extract
  it out of '{0}'."`
- Ship as a `netstandard2.0` analyzer project referencing
  `Microsoft.CodeAnalysis.CSharp` (matching the existing multi-project
  layout under `src/`), packaged as a Roslyn analyzer/`DiagnosticAnalyzer`
  NuGet-embedded DLL alongside `ApprovalTests.EntityFramework`.

## Suppression

Since this is a style nudge rather than a bug detector, support the normal
`#pragma warning disable ENTITYFRAMEWORKAPPROVALS001` / `[SuppressMessage]` / `.editorconfig`
severity override paths that come for free with `DiagnosticAnalyzer`, and
default the packaged severity to `Warning` (not `Error`) so adoption in an
existing codebase doesn't break builds outright.

