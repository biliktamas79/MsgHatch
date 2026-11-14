using System.Net.Http;
using Microsoft.Extensions.Logging;

namespace WolverineExample.UserSimulator;

/// <summary>The log events of the user simulator, 9300 to 9399, as source-generated extension methods of <see cref="ILogger"/>.</summary>
public static partial class SimulatorLoggerExtensions
{
    /// <summary>The event id of <see cref="NotificationReceived"/>.</summary>
    public const int NotificationReceivedId = 9300;

    /// <summary>The event id of <see cref="UserConnected"/>.</summary>
    public const int UserConnectedId = 9301;

    /// <summary>The event id of <see cref="ConnectFailed"/>.</summary>
    public const int ConnectFailedId = 9302;

    /// <summary>The event id of <see cref="UserDisconnected"/>.</summary>
    public const int UserDisconnectedId = 9303;

    /// <summary>The event id of <see cref="UserReconnected"/>.</summary>
    public const int UserReconnectedId = 9304;

    /// <summary>The event id of <see cref="NotificationCaughtUp"/>.</summary>
    public const int NotificationCaughtUpId = 9305;

    /// <summary>The event id of <see cref="CatchUpFailed"/>.</summary>
    public const int CatchUpFailedId = 9306;

    /// <summary>A simulated user got one notification from the catch-up after a connect, which it did not get live.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="tenant">The tenant of the user.</param>
    /// <param name="userId">The user.</param>
    /// <param name="messageType">The message type.</param>
    /// <param name="text">The text of the notification.</param>
    [LoggerMessage(NotificationCaughtUpId, LogLevel.Information, "User {Tenant}:{UserId} caught up {MessageType}: {Text}")]
    public static partial void NotificationCaughtUp(this ILogger logger, string tenant, int userId, string messageType, string text);

    /// <summary>The hub gave an error for the catch-up of a simulated user.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="tenant">The tenant of the user.</param>
    /// <param name="userId">The user.</param>
    /// <param name="exception">The error of the hub.</param>
    [LoggerMessage(CatchUpFailedId, LogLevel.Warning, "User {Tenant}:{UserId} could not catch up")]
    public static partial void CatchUpFailed(this ILogger logger, string tenant, int userId, Exception exception);

    /// <summary>A simulated user connected again, for example after the hub closed the connection of an expired token.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="tenant">The tenant of the user.</param>
    /// <param name="userId">The user.</param>
    [LoggerMessage(UserReconnectedId, LogLevel.Information, "User {Tenant}:{UserId} reconnected")]
    public static partial void UserReconnected(this ILogger logger, string tenant, int userId);

    /// <summary>A simulated user got one notification.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="tenant">The tenant of the user.</param>
    /// <param name="userId">The user.</param>
    /// <param name="messageType">The message type, which is the SignalR method.</param>
    /// <param name="text">The text of the notification.</param>
    [LoggerMessage(NotificationReceivedId, LogLevel.Information, "User {Tenant}:{UserId} got {MessageType}: {Text}")]
    public static partial void NotificationReceived(this ILogger logger, string tenant, int userId, string messageType, string text);

    /// <summary>A simulated user connected to the hub.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="tenant">The tenant of the user.</param>
    /// <param name="userId">The user.</param>
    [LoggerMessage(UserConnectedId, LogLevel.Information, "User {Tenant}:{UserId} connected")]
    public static partial void UserConnected(this ILogger logger, string tenant, int userId);

    /// <summary>A simulated user could not connect, and tries again after a delay.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="tenant">The tenant of the user.</param>
    /// <param name="userId">The user.</param>
    /// <param name="statusCode">The HTTP status code, or null when the hub did not answer.</param>
    /// <param name="error">The kind of the HTTP error.</param>
    /// <param name="delay">The delay before the next try.</param>
    [LoggerMessage(ConnectFailedId, LogLevel.Warning, "User {Tenant}:{UserId} could not connect: {StatusCode} {Error}. Next try in {Delay}")]
    public static partial void ConnectFailed(
        this ILogger logger, string tenant, int userId, int? statusCode, HttpRequestError error, TimeSpan delay);

    /// <summary>The connection of a simulated user closed, after its reconnects failed.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="tenant">The tenant of the user.</param>
    /// <param name="userId">The user.</param>
    [LoggerMessage(UserDisconnectedId, LogLevel.Warning, "User {Tenant}:{UserId} disconnected")]
    public static partial void UserDisconnected(this ILogger logger, string tenant, int userId);
}
