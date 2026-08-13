using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace ApprovalTests.EntityFrameworkUtilities
{
    public class LambdaEnumerableLoader<T, C> : EntityFrameworkLoader<T, IEnumerable<T>, C>
        where C : DbContext
    {
        private readonly Func<C, IQueryable<T>> func;

        public LambdaEnumerableLoader(C context, Func<C, IQueryable<T>> func) : base(context)
        {
            this.func = func;
        }

        public LambdaEnumerableLoader(Func<C> context, Func<C, IQueryable<T>> func) : base(context)
        {
            this.func = func;
        }

        public override IQueryable<T> GetLinqStatement()
        {
            return func(GetDatabaseContext());
        }

        public override IEnumerable<T> Load()
        {
            return GetLinqStatement().ToArray();
        }
    }
}
