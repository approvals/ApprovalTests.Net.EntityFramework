using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace ApprovalTests.Persistence.EntityFramework.Version5
{
    public class EntityFrameworkDbContextApprovals
    {
        public static void Verify<T>(DbContext db, IQueryable<T> queryable)
        {
            DatabaseApprovals.Verify(new DbContextAdaptor<T>(db, queryable));
        }
    }
}