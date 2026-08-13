using System.Data.Common;
using System.Linq;
using ApprovalUtilities.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace ApprovalTests.EntityFrameworkUtilities
{
    public class ObjectContextAdaptor<T> : IDatabaseToExecutableQueryAdapter
    {
        private readonly DbContext db;
        private readonly IQueryable<T> queryable;

        public ObjectContextAdaptor(DbContext db, IQueryable<T> queryable)
        {
            this.db = db;
            this.queryable = queryable;
        }

        public string GetQuery()
        {
            return EntityFrameworkUtils.GetQueryFromLinq(queryable);
        }

        public DbConnection GetConnection()
        {
            return EntityFrameworkUtils.GetConnectionFrom(db);
        }
    }

    public class EntityFrameworkUtils
    {
        public static DbConnection GetConnectionFrom(DbContext context)
        {
            return context.Database.GetDbConnection();
        }

        public static string GetQueryFromLinq(IQueryable linq)
        {
            return linq.ToQueryString();
        }
    }
}
