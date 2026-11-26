using Microsoft.EntityFrameworkCore;
using MsgHatch.Tests.Shared;

namespace MsgHatch.Data.EFCore.Tests.Unit.TestTypes;

/// <summary>A context of a host with its own table <c>Orders</c>, whose model has no inbox of MsgHatch.</summary>
/// <param name="options">The options of the context.</param>
internal sealed class TestHostContextWithNoInbox(DbContextOptions<TestHostContextWithNoInbox> options) : DbContext(options)
{
    public DbSet<TestOrder> Orders => Set<TestOrder>();
}
