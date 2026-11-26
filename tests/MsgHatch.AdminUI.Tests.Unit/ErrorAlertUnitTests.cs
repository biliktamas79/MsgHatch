using Bunit;
using MsgHatch.Domain.Failures;

namespace MsgHatch.AdminUI.Tests.Unit;

public sealed class ErrorAlertUnitTests : BunitContext
{
    [Fact]
    public void Render_NoError_ShowsNothing()
    {
        // Act
        var cut = Render<ErrorAlert>();

        // Assert
        Assert.Empty(cut.FindAll(".alert"));
    }

    [Fact]
    public void Render_Unavailable_ShowsTheTextOfUnavailableAndTheCode()
    {
        // Act
        var cut = Render<ErrorAlert>(p => p.Add(x => x.Error, new Error(Errors.Unavailable, "Connection refused.")));

        // Assert
        var alert = cut.Find(".alert-danger");
        Assert.Equal(AdminErrors.UnavailableText, alert.TextContent);
        Assert.Equal("503", alert.GetAttribute("data-error-code"));
    }

    [Fact]
    public void Render_Class_AddsTheClassesToTheAlert()
    {
        // Act
        var cut = Render<ErrorAlert>(p => p.Add(x => x.Error, new Error(Errors.Unavailable)).Add(x => x.Class, "mb-0 py-1"));

        // Assert
        Assert.Equal("alert alert-danger mb-0 py-1", cut.Find("[role=alert]").ClassName);
    }

    [Fact]
    public void Render_ConflictWithAText_ShowsTheTextInPlaceOfTheTextOfConflict()
    {
        // Act
        var cut = Render<ErrorAlert>(p => p.Add(x => x.Error, new Error(Errors.Conflict, "A duplicate.")).Add(x => x.Text, "A duplicate."));

        // Assert
        var alert = cut.Find(".alert-danger");
        Assert.Equal(("A duplicate.", "409"), (alert.TextContent, alert.GetAttribute("data-error-code")));
    }
}
