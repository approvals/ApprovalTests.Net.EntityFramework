using System.Data.Common;
using System.Linq;
using ApprovalUtilities.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace ApprovalTests.Persistence.EntityFramework.Version5
{
    public class DbContextAdaptor<T> : IDatabaseToExecutableQueryAdapter
    {
        private readonly DbContext db;
        private readonly IQueryable<T> queryable;

        public DbContextAdaptor(DbContext db, IQueryable<T> queryable)
        {
            this.db = db;
            this.queryable = queryable;
        }

        public string GetQuery()
        {
            return queryable.ToQueryString();
        }

        public DbConnection GetConnection()
        {
            return db.Database.GetDbConnection();
        }
    }
}
