using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ExampleApp;

/// <summary>
/// The SignalR hub of the UI notifications. The SignalR transport of MsgHatch sends through its hub context, so the hub
/// needs no method.
/// </summary>
[Authorize]
public sealed class NotificationHub : Hub;
