using Microsoft.Data.Sqlite;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Data.EFCore.Tests.Unit.TestTypes;

#region readme:classifier-type
public sealed class SqliteReadOnlyClassifier : IExceptionClassifier<MsgHatchDbContext>
{
    private const int _readOnly = 8;

    public ErrorType? Classify(Exception exception) =>
        exception.GetBaseException() is SqliteException { SqliteErrorCode: _readOnly } ? Errors.Rejected : null;
}
#endregion
