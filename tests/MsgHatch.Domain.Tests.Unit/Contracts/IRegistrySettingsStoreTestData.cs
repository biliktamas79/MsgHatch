namespace MsgHatch.Domain.Tests.Unit.Contracts;

/// <summary>What the registry settings store interface cannot do: reset the storage.</summary>
public interface IRegistrySettingsStoreTestData
{
    ValueTask ResetAsync(CancellationToken ct);
}
