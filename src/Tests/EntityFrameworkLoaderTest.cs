using System.Linq;
using ApprovalTests;
using ApprovalTests.EntityFrameworkUtilities;
using ApprovalTests.Persistence.EntityFramework;
using ApprovalTests.Tests.EntityFramework;
using ApprovalUtilities.Persistence;
using Xunit;

public class EntityFrameworkLoaderTest
{
    [Fact]
    public void FromInheritance()
    {
        Approvals.Verify(new CompanyLoaderByName2("M1"));
    }

    [Fact]
    public void FromLambda()
    {
        Approvals.Verify(CreateCompanyLoaderByName("Mic"));
    }


    private LambdaEnumerableLoader<Company, ModelContainer> CreateCompanyLoaderByName(string name)
    {
        return LoaderUtils.Load(db => (from c in db.Companies
            where c.Name.StartsWith(name)
            select c).Take(10));
    }

    [Fact]
    public void FromIQueryable()
    {
        using (var db = new ModelContainer())
        {
            EntityFrameworkApprovals.Verify(db, CreateCompanyLoaderByName2(db, "Mic"));
        }
    }

    private IQueryable<Company> CreateCompanyLoaderByName2(ModelContainer db, string name)
    {
        return (from c in db.Companies
            where c.Name.StartsWith(name)
            select c).Take(10);
    }

    [Fact]
    public void FromSingleLambda()
    {
        var loader1 = CreateCompanyLoaderByName("Mic");
        IExecutableLoader<Company> loader = loader1.Singleton();
        Approvals.Verify(loader);
    }
}