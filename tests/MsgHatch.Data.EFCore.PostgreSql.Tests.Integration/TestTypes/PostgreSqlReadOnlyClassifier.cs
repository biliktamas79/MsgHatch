using MsgHatch.Domain.Failures;
using Npgsql;

namespace MsgHatch.Data.EFCore.PostgreSql.Tests.Integration.TestTypes;

#region readme:classifier-type
public sealed class PostgreSqlReadOnlyClassifier : IExceptionClassifier<MsgHatchDbContext>
{
    public ErrorType? Classify(Exception exception) =>
        exception.GetBaseException() is PostgresException { SqlState: PostgresErrorCodes.ReadOnlySqlTransaction } ? Errors.Rejected : null;
}
#endregion
