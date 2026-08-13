using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace ApprovalTests.EntityFrameworkUtilities
{
    public static class EntityFrameworkLoadersExtensions
    {
        public static LambdaSingleLoader<T, C> Singleton<T, C>(this EntityFrameworkLoader<T, IEnumerable<T>, C> otherLoader)
            where C : DbContext
        {
            return new LambdaSingleLoader<T, C>(otherLoader);
        }
    }
}