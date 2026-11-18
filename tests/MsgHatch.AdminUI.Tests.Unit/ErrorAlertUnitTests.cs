using Bunit;
using MsgHatch.Domain.Failures;

namespace MsgHatch.AdminUI.Tests.Unit;

public sealed class ErrorAlertUnitTests : BunitContext
{
    [Fact]
    public void Render_NoError_ShowsNothing()
    {
        var cut = Render<ErrorAlert>();

        Assert.Empty(cut.FindAll(".alert"));
    }

    [Fact]
    public void Render_Unavailable_ShowsTheTextOfUnavailableAndTheCode()
    {
        var cut = Render<ErrorAlert>(p => p.Add(x => x.Error, new Error(Errors.Unavailable, "Connection refused.")));

        var alert = cut.Find(".alert-danger");
        Assert.Equal(AdminErrors.UnavailableText, alert.TextContent);
        Assert.Equal("503", alert.GetAttribute("data-error-code"));
    }
}
