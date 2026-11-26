using Microsoft.Data.SqlClient;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Data.EFCore.SqlServer.Tests.Unit.TestTypes;

#region readme:classifier-type
public sealed class SqlServerReadOnlyClassifier : IExceptionClassifier<MsgHatchDbContext>
{
    private const int _readOnlyDatabase = 3906;

    public ErrorType? Classify(Exception exception) =>
        exception.GetBaseException() is SqlException { Number: _readOnlyDatabase } ? Errors.Rejected : null;
}
#endregion
