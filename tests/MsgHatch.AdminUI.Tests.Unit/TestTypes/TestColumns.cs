using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.AdminUI.Tests.Unit.TestTypes;

/// <summary>The results of the test columns, by the header of each column.</summary>
public sealed class TestColumnCells
{
    public Dictionary<string, ErrorOr<IReadOnlyDictionary<MessageType, IReadOnlyList<MessageTypeCellItem>>>> ByHeader { get; } = [];
}

/// <summary>A message type column that gives the result of its header from <see cref="TestColumnCells"/>.</summary>
public abstract class TestColumn(TestColumnCells cells, string header) : IMessageTypeColumn
{
    public string Header => header;

    public ValueTask<ErrorOr<IReadOnlyDictionary<MessageType, IReadOnlyList<MessageTypeCellItem>>>> GetCellsAsync(CancellationToken ct) =>
        ValueTask.FromResult(cells.ByHeader[header]);
}

public sealed class FirstTestColumn(TestColumnCells cells) : TestColumn(cells, "First");

public sealed class SecondTestColumn(TestColumnCells cells) : TestColumn(cells, "Second");
