using System.Reflection;
using Microsoft.Data.SqlClient;

namespace MsgHatch.Data.EFCore.SqlServer.Tests.Unit.TestTypes;

/// <summary>
/// The <see cref="SqlException"/> of a server error, with no server. Microsoft.Data.SqlClient has no public constructor of
/// <see cref="SqlException"/>, so the factory calls its non-public members: the constructor of <see cref="SqlError"/>, the
/// constructor and <c>Add</c> of <see cref="SqlErrorCollection"/>, and <c>SqlException.CreateException</c>.
/// </summary>
public static class TestSqlExceptions
{
    private const BindingFlags _nonPublicInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    /// <summary>A <see cref="SqlException"/> with one error of the number.</summary>
    public static SqlException ASqlException(int number)
    {
        var error = (SqlError)typeof(SqlError).GetConstructor(
                _nonPublicInstance,
                [typeof(int), typeof(byte), typeof(byte), typeof(string), typeof(string), typeof(string), typeof(int), typeof(Exception)])!
            .Invoke([number, (byte)1, (byte)16, "server", "The message of the server.", "procedure", 1, null]);
        var errors = (SqlErrorCollection)typeof(SqlErrorCollection).GetConstructor(_nonPublicInstance, Type.EmptyTypes)!.Invoke(null);
        typeof(SqlErrorCollection).GetMethod("Add", _nonPublicInstance)!.Invoke(errors, [error]);
        return (SqlException)typeof(SqlException)
            .GetMethod("CreateException", BindingFlags.Static | BindingFlags.NonPublic, [typeof(SqlErrorCollection), typeof(string)])!
            .Invoke(null, [errors, "17.0"])!;
    }
}
