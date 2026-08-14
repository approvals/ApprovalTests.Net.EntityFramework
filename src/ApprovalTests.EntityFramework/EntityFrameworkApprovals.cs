using System.Linq;
using ApprovalTests.EntityFrameworkUtilities;
using ApprovalTests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ApprovalTests.Persistence.EntityFramework
{
    public class EntityFrameworkApprovals
    {
        public static void Verify<T>(DbContext db, IQueryable<T> queryable)
        {
            Approvals.Verify(new ExecutableSqlQuery(new ObjectContextAdaptor<T>(db, queryable)));
        }

        public static void VerifyQueryAsSql(IQueryable query)
        {
            Approvals.VerifyWithExtension(query.ToQueryString() + "\n", ".sql");
        }
    }
}
