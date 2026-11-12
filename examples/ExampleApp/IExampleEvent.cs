namespace ExampleApp;

/// <summary>
/// The shape of the example events A to E: a user scope or a company scope, and a text. The interface belongs to the
/// app, so the events stay free of MsgHatch types (D16).
/// </summary>
public interface IExampleEvent
{
    /// <summary>The users of a user scope, or null for a company scope.</summary>
    IReadOnlyList<int>? UserIds { get; }

    /// <summary>The company of a company scope, or null for a user scope.</summary>
    int? CompanyId { get; }

    /// <summary>The text of the notification.</summary>
    string Text { get; }
}
