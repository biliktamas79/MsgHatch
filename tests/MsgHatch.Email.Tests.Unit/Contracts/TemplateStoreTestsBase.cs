using MsgHatch.Domain.Identity;
using MsgHatch.Email.Templates;

namespace MsgHatch.Email.Tests.Unit.Contracts;

/// <summary>
/// The use cases of <see cref="ITemplateStore"/> (section 5.5). Each implementation gets them with one subclass that
/// builds the store and its test data. The scenario is the folder <c>TemplateFiles</c> of this project, which the test
/// data puts into the store one time.
/// </summary>
/// <typeparam name="TStore">The store type.</typeparam>
public abstract partial class TemplateStoreTestsBase<TStore>(TStore store, ITemplateStoreTestData testData) : IAsyncLifetime
    where TStore : class, ITemplateStore
{
    protected static readonly MessageType TestEvent = new("TestEvent");

    protected TStore Store { get; } = store;

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => testData.SeedAsync(Ct);

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    protected static (string Name, TemplatePartKind Kind, string Text)[] PartsOf(TemplateSource? source) =>
        [.. (source?.Parts ?? []).Select(static part => (part.Name, part.Kind, part.Text))];
}
