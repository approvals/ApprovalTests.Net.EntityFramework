using System.Linq;
using ApprovalTests.EntityFrameworkUtilities;
using Microsoft.EntityFrameworkCore;

namespace ApprovalTests.Persistence.EntityFramework
{
    public class EntityFrameworkApprovals
    {
        public static void Verify<T>(DbContext db, IQueryable<T> queryable)
        {
            DatabaseApprovals.Verify(new ObjectContextAdaptor<T>(db, queryable));
        }
    }
}
