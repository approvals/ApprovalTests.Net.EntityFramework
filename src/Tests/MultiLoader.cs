using System.Collections.Generic;
using System.Linq;
using ApprovalTests.EntityFrameworkUtilities;
using ApprovalTests.Tests.EntityFramework;

public abstract class MultiLoader<T> : EntityFrameworkLoader<T, IEnumerable<T>, ModelContainer>
{
    public MultiLoader() : base(() => new ModelContainer())
    {
    }

    public override IEnumerable<T> Load()
    {
        return GetLinqStatement().ToArray();
    }
}