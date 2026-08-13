using System.Linq;
using ApprovalTests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ApprovalTests.Persistence.EntityFramework.Version5
{
    public class EntityFrameworkDbContextApprovals
    {
        public static void Verify<T>(DbContext db, IQueryable<T> queryable)
        {
            Approvals.Verify(new ExecutableSqlQuery(new DbContextAdaptor<T>(db, queryable)));
        }
    }
}