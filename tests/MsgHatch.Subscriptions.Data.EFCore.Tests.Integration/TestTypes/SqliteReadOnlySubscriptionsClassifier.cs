using Microsoft.Data.Sqlite;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Subscriptions.Data.EFCore.Tests.Integration.TestTypes;

#region readme:classifier-type
public sealed class SqliteReadOnlySubscriptionsClassifier : IExceptionClassifier<MsgHatchSubscriptionDbContext>
{
    private const int _readOnly = 8;

    public ErrorType? Classify(Exception exception) =>
        exception.GetBaseException() is SqliteException { SqliteErrorCode: _readOnly } ? Errors.Rejected : null;
}
#endregion
