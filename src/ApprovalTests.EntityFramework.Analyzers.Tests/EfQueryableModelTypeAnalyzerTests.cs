using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Testing.Verifiers;
using Xunit;

// The analyzer under test does not exist yet. These tests describe the intended behavior
// and will not compile/run until EfQueryableModelTypeAnalyzer is added to
// ApprovalTests.EntityFramework.Analyzers, alongside EfQueryStandaloneMethodAnalyzer.
//
// Goal: a method whose declared return type is IQueryable<T> is only "standalone-safe" per
// EfQueryStandaloneMethodAnalyzer if T is itself an EF model type — i.e. an entity type
// mapped as the element type of some DbSet<T> on a DbContext in the compilation. Returning
// IQueryable<T> for a scalar (string, int, ...), an anonymous type, or an unmapped DTO/POCO
// defeats the purpose: EntityFrameworkApprovals.Verify / VerifyQueryAsSql exist to approval-test
// the shape of an entity query, not an arbitrary projection.
using ApprovalTests.EntityFramework.Analyzers;

namespace ApprovalTests.EntityFramework.Analyzers.Tests;

// Same reasoning as EfQueryStandaloneMethodAnalyzerTests.Verifier: pin references to the
// assemblies this test host already runs against, plus EF Core itself, instead of letting
// CSharpAnalyzerVerifier resolve an incompatible netstandard2.0 ref pack.
internal static class ModelTypeVerifier
{
    private static readonly ImmutableArray<MetadataReference> RuntimeReferences =
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => !string.IsNullOrWhiteSpace(path) && 
                           path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) &&
                           File.Exists(path))
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(Microsoft.EntityFrameworkCore.DbContext).Assembly.Location))
            .ToImmutableArray();

    public static DiagnosticResult Diagnostic(string diagnosticId) =>
        CSharpAnalyzerVerifier<EfQueryableModelTypeAnalyzer, XUnitVerifier>.Diagnostic(diagnosticId);

    public static async Task VerifyAnalyzerAsync(string source, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<EfQueryableModelTypeAnalyzer, XUnitVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = new ReferenceAssemblies("net10.0"),
        };
        test.TestState.AdditionalReferences.AddRange(RuntimeReferences);
        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }
}

public class EfQueryableModelTypeAnalyzerTests
{
    private const string DiagnosticId = "ENTITYFRAMEWORKAPPROVALS002";

    // Shared DbContext/entity scaffolding that every test source is wrapped around. Includes
    // one mapped entity (Company) and one deliberately-unmapped POCO (CompanyDto) shaped just
    // like it, so tests can distinguish "is an entity type" from "merely looks like one".
    private static string WithScaffolding(string methodBody) => $$"""
        using System.Collections.Generic;
        using System.Linq;
        using Microsoft.EntityFrameworkCore;

        public class Company
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
        }

        public class CompanyDto
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
    public async Task IQueryableOfMappedEntityType_NoDiagnostic()
    {
        var source = WithScaffolding("""
            public IQueryable<Company> CreateCompanyLoaderByName(MyDbContext db, string name)
            {
                return db.Companies.Where(c => c.Name.StartsWith(name));
            }
            """);

        await ModelTypeVerifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task IQueryableOfScalarProjection_Diagnostic()
    {
        var source = WithScaffolding("""
            public IQueryable<string> {|#0:CompanyNamesStartingWith|}(MyDbContext db, string name)
            {
                return db.Companies.Where(c => c.Name.StartsWith(name)).Select(c => c.Name);
            }
            """);

        var expected = ModelTypeVerifier.Diagnostic(DiagnosticId)
            .WithLocation(0)
            .WithArguments("CompanyNamesStartingWith", "string");

        await ModelTypeVerifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task IQueryableOfUnmappedDto_LooksLikeEntity_Diagnostic()
    {
        // CompanyDto is shaped identically to the mapped Company entity but is never exposed
        // via a DbSet<T>, so it isn't an EF model type: name/shape matching isn't enough.
        var source = WithScaffolding("""
            public IQueryable<CompanyDto> {|#0:CompanyDtos|}(MyDbContext db)
            {
                return db.Companies.Select(c => new CompanyDto { Id = c.Id, Name = c.Name });
            }
            """);

        var expected = ModelTypeVerifier.Diagnostic(DiagnosticId)
            .WithLocation(0)
            .WithArguments("CompanyDtos", "CompanyDto");

        await ModelTypeVerifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task IQueryableOfAnonymousType_Diagnostic()
    {
        var source = WithScaffolding("""
            public IQueryable<object> {|#0:CompanyProjection|}(MyDbContext db)
            {
                return db.Companies.Select(c => new { c.Id, c.Name }).Cast<object>();
            }
            """);

        var expected = ModelTypeVerifier.Diagnostic(DiagnosticId)
            .WithLocation(0)
            .WithArguments("CompanyProjection", "object");

        await ModelTypeVerifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task NonIQueryableReturnType_NotAnalyzed_NoDiagnostic()
    {
        // Out of scope for this analyzer: EfQueryStandaloneMethodAnalyzer already flags
        // non-IQueryable<T> returns that build an EF query.
        var source = WithScaffolding("""
            public List<CompanyDto> GetCompanyDtos(MyDbContext db)
            {
                return db.Companies.Select(c => new CompanyDto { Id = c.Id, Name = c.Name }).ToList();
            }
            """);

        await ModelTypeVerifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task NonEfQueryable_OverInMemoryList_NoDiagnostic()
    {
        // The source is List<T>.AsQueryable(), not an EF-backed DbSet<T>, so there's no EF
        // model to check the element type against: never flag it.
        var source = WithScaffolding("""
            public IQueryable<CompanyDto> InMemoryDtos(List<CompanyDto> dtos)
            {
                return dtos.AsQueryable();
            }
            """);

        await ModelTypeVerifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task LocalFunctionReturningIQueryableOfUnmappedType_Diagnostic()
    {
        var source = WithScaffolding("""
            public IQueryable<string> CompanyNames(MyDbContext db)
            {
                return Local();

                IQueryable<string> {|#0:Local|}()
                {
                    return db.Companies.Select(c => c.Name);
                }
            }
            """);

        var expected = ModelTypeVerifier.Diagnostic(DiagnosticId)
            .WithLocation(0)
            .WithArguments("Local", "string");

        await ModelTypeVerifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task PragmaSuppressed_NoDiagnostic()
    {
        var source = WithScaffolding("""
            public IQueryable<string> CompanyNamesStartingWith(MyDbContext db, string name)
            {
            #pragma warning disable ENTITYFRAMEWORKAPPROVALS002
                return db.Companies.Where(c => c.Name.StartsWith(name)).Select(c => c.Name);
            #pragma warning restore ENTITYFRAMEWORKAPPROVALS002
            }
            """);

        await ModelTypeVerifier.VerifyAnalyzerAsync(source);
    }
}
