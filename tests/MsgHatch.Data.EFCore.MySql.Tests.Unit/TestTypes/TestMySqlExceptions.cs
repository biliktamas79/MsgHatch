using System.Reflection;
using MySqlConnector;

namespace MsgHatch.Data.EFCore.MySql.Tests.Unit.TestTypes;

/// <summary>
/// The <see cref="MySqlException"/> of a server error, with no server. MySqlConnector has no public constructor of
/// <see cref="MySqlException"/>, so the factory calls its non-public constructor.
/// </summary>
public static class TestMySqlExceptions
{
    /// <summary>A <see cref="MySqlException"/> with the error code.</summary>
    public static MySqlException AMySqlException(MySqlErrorCode code) =>
        (MySqlException)Activator.CreateInstance(
            typeof(MySqlException), BindingFlags.Instance | BindingFlags.NonPublic, binder: null, [code, "The message of the server."], culture: null)!;
}
