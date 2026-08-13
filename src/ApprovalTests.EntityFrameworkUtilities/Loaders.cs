using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace ApprovalTests.EntityFrameworkUtilities
{
    public class Loaders
    {
        public static LambdaEnumerableLoader<T, C> Create<T, C>(C modelContainer, Func<C, IQueryable<T>> func)
            where C : DbContext
        {
            return new LambdaEnumerableLoader<T, C>(modelContainer, func);
        }

        public static LambdaEnumerableLoader<T, C> Create<T, C>(Func<C> modelContainer, Func<C, IQueryable<T>> func)
            where C : DbContext
        {
            return new LambdaEnumerableLoader<T, C>(modelContainer, func);
        }

        public static LambdaSingleLoader<T, C> CreateSingle<T, C>(Func<C> modelContainer, Func<C, IQueryable<T>> func)
            where C : DbContext
        {
            return new LambdaSingleLoader<T, C>(Create(modelContainer, func));
        }
    }
}
