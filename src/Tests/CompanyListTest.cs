using ApprovalTests;
using Xunit;

public class CompanyListTest
{
    [Fact]
    public void TestLoader()
    {
        Approvals.Verify(CompanyList.GetCompanyByName("m"));
    }

    [Fact]
    public void TestLoader2()
    {
        Approvals.Verify(CompanyList.GetCompanyByName("mi"));
    }
}