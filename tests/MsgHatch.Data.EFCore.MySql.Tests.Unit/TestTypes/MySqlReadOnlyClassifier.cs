using MsgHatch.Domain.Failures;
using MySqlConnector;

namespace MsgHatch.Data.EFCore.MySql.Tests.Unit.TestTypes;

#region readme:classifier-type
public sealed class MySqlReadOnlyClassifier : IExceptionClassifier<MsgHatchDbContext>
{
    public ErrorType? Classify(Exception exception) =>
        exception.GetBaseException() is MySqlException { ErrorCode: MySqlErrorCode.OptionPreventsStatement } ? Errors.Rejected : null;
}
#endregion
