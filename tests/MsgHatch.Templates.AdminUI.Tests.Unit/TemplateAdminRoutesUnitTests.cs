using System.Reflection;
using Microsoft.AspNetCore.Components;
using static MsgHatch.Templates.AdminUI.Tests.Unit.TestTypes.TemplateTestData;

namespace MsgHatch.Templates.AdminUI.Tests.Unit;

public sealed class TemplateAdminRoutesUnitTests
{
    [Fact]
    public void Edit_DefaultScope_GivesThePathWithNoQuery()
    {
        Assert.Equal("/msghatch/templates/Requests/en/subject/PlainText", TemplateAdminRoutes.Edit(Lookup()));
    }

    [Fact]
    public void Edit_CompanyWithCharactersOfAUri_EscapesTheCompanyAndTheLanguage()
    {
        var address = TemplateAdminRoutes.Edit(Lookup("hu-HU", "a/b c", "body", TemplatePartKind.Html));

        Assert.Equal("/msghatch/templates/Requests/hu-HU/body/Html?companyId=a%2Fb%20c", address);
    }

    [Fact]
    public void View_DefaultScope_GivesThePathOfTheViewPageWithNoQuery()
    {
        Assert.Equal("/msghatch/templates/Requests/en/subject/PlainText/view", TemplateAdminRoutes.View(Lookup()));
    }

    [Fact]
    public void View_CompanyWithCharactersOfAUri_EscapesTheCompanyAndTheLanguage()
    {
        var address = TemplateAdminRoutes.View(Lookup("hu-HU", "a/b c", "body", TemplatePartKind.Html));

        Assert.Equal("/msghatch/templates/Requests/hu-HU/body/Html/view?companyId=a%2Fb%20c", address);
    }

    [Fact]
    public void Pages_RouteTemplates_StartWithTheListAddress()
    {
        Assert.Equal(TemplateAdminRoutes.List, typeof(TemplateListPage).GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal(TemplateAdminRoutes.List + "/{TypeName}/{Language}/{Part}/{KindName}", typeof(TemplateEditPage).GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal(TemplateAdminRoutes.List + "/{TypeName}/{Language}/{Part}/{KindName}/view", typeof(TemplateViewPage).GetCustomAttribute<RouteAttribute>()!.Template);
    }
}
