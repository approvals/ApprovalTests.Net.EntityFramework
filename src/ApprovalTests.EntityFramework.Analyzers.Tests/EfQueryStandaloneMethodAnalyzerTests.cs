using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Testing.Verifiers;
using Xunit;

// The analyzer under test (docs/design/ef-query-standalone-method-analyzer.md) does not
// exist yet. These tests describe the intended behavior and will not compile/run until
// an ApprovalTests.EntityFramework.Analyzers project exposing EfQueryStandaloneMethodAnalyzer
// is added and referenced from this project.
using ApprovalTests.EntityFramework.Analyzers;

namespace ApprovalTests.EntityFramework.Analyzers.Tests;

using Verifier = CSharpAnalyzerVerifier<EfQueryStandaloneMethodAnalyzer, XUnitVerifier>;

public class EfQueryStandaloneMethodAnalyzerTests
{
    private const string DiagnosticId = "ENTITYFRAMEWORKAPPROVALS001";

    // Shared DbContext/entity scaffolding that every test source is wrapped around.
    private static string WithScaffolding(string methodBody) => $$"""
        using System.Collections.Generic;
        using System.Linq;
        using Microsoft.EntityFrameworkCore;

        public class Company
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
        }

        public class MyDbContext : DbContext
        {
            public DbSet<Company> Companies { get; set; } = null!;
        }

        public class Loaders
        {
            {{methodBody}}
        }
        """;

    [Fact]
    public async Task MethodSyntaxQuery_ReturnedDirectly_AsIQueryable_NoDiagnostic()
    {
        var source = WithScaffolding("""
            public IQueryable<Company> CreateCompanyLoaderByName(MyDbContext db, string name)
            {
                return db.Companies.Where(c => c.Name.StartsWith(name)).Take(10);
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task QuerySyntaxQuery_ReturnedDirectly_AsIQueryable_NoDiagnostic()
    {
        var source = WithScaffolding("""
            public IQueryable<Company> CreateCompanyLoaderByName2(MyDbContext db, string name)
            {
                return (from c in db.Companies
                    where c.Name.StartsWith(name)
                    select c).Take(10);
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task QueryBuiltThenEnumeratedInline_ReturnsList_Diagnostic()
    {
        var source = WithScaffolding("""
            public List<Company> {|#0:LoadCompanies|}(MyDbContext db, string name)
            {
                var query = db.Companies.Where(c => c.Name.StartsWith(name));
                return query.ToList();
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticId)
            .WithLocation(0)
            .WithArguments("LoadCompanies");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task QueryBuiltThenMixedWithOtherLogic_VoidMethod_Diagnostic()
    {
        var source = WithScaffolding("""
            private int _lastCount;

            public void {|#0:RefreshCompanies|}(MyDbContext db, string name)
            {
                var query = db.Companies.Where(c => c.Name.StartsWith(name));
                _lastCount = query.Count();
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticId)
            .WithLocation(0)
            .WithArguments("RefreshCompanies");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task QueryBuiltInline_ThenReturnedAsList_MethodSyntax_Diagnostic()
    {
        var source = WithScaffolding("""
            public List<Company> {|#0:GetActiveCompanies|}(MyDbContext db)
            {
                return db.Companies.Where(c => c.Id > 0).ToList();
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticId)
            .WithLocation(0)
            .WithArguments("GetActiveCompanies");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task NonEfQuery_OverInMemoryList_NoDiagnostic()
    {
        // The LINQ source is a List<T>, not a DbSet<T>/DbContext member, so this
        // is not an EF query at all and should never be flagged.
        var source = WithScaffolding("""
            public List<Company> FilterInMemory(List<Company> companies, string name)
            {
                var query = companies.Where(c => c.Name.StartsWith(name));
                return query.ToList();
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task MethodReturningIQueryableOfWrongElementType_StillStandalone_NoDiagnostic()
    {
        // Return type is IQueryable<T> for some T, matching the documented rule
        // regardless of extra projection.
        var source = WithScaffolding("""
            public IQueryable<string> CompanyNamesStartingWith(MyDbContext db, string name)
            {
                return db.Companies.Where(c => c.Name.StartsWith(name)).Select(c => c.Name);
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task LocalFunctionBuildingQuery_ThenEnumerated_Diagnostic()
    {
        var source = WithScaffolding("""
            public int CountCompanies(MyDbContext db, string name)
            {
                return Local();

                int {|#0:Local|}()
                {
                    var query = db.Companies.Where(c => c.Name.StartsWith(name));
                    return query.Count();
                }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticId)
            .WithLocation(0)
            .WithArguments("Local");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task PragmaSuppressed_NoDiagnostic()
    {
        var source = WithScaffolding("""
            public List<Company> GetActiveCompanies(MyDbContext db)
            {
            #pragma warning disable ENTITYFRAMEWORKAPPROVALS001
                return db.Companies.Where(c => c.Id > 0).ToList();
            #pragma warning restore ENTITYFRAMEWORKAPPROVALS001
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }
}
