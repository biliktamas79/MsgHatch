using Microsoft.AspNetCore.Components;

namespace MsgHatch.Email.Razor;

/// <summary>
/// The base class of a Razor email template (D198): the file <c>{root}/{MessageType}.{Language}.body.razor</c>, or
/// <c>{root}/{CompanyId}/{MessageType}.{Language}.body.razor</c> for a company, with
/// <c>@inherits RazorEmailComponent&lt;TPayload&gt;</c>. It renders the html body of the email. The subject and the text
/// part come from the template store (D193).
/// </summary>
/// <typeparam name="TPayload">The payload type of the email.</typeparam>
public abstract class RazorEmailComponent<TPayload> : ComponentBase
{
    /// <summary>The payload of the route.</summary>
    [Parameter, EditorRequired]
    public TPayload Payload { get; set; } = default!;
}
