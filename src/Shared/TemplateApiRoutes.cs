namespace MsgHatch;

/// <summary>
/// The route paths of the template management API under its prefix (section 4.22, D228). The <c>.AspNetCore</c> package
/// and the client package compile this file, so a change of a route changes both sides in one commit.
/// </summary>
internal static class TemplateApiRoutes
{
    public const string Templates = "";
    public const string Parts = "/parts";
    public const string Template = "/{messageType}/{language}/{part}/{kind}";
    public const string Check = Template + "/check";
    public const string Preview = Template + "/preview";
}
