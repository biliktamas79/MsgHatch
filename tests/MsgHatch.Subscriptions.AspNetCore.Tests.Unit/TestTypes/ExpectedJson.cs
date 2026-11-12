using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.AspNetCore.Tests.Unit.TestTypes;

/// <summary>The response JSON of the scenario subscriptions #4, #6, and #11 of <c>docs/subscription-domain.md</c>.</summary>
public static class ExpectedJson
{
    public static readonly string S4 =
        $$"""{"id":"{{Scenario.S4.Id}}","revision":1,"level":"Company","companyId":"100","userId":null,"messageType":"B","kind":"OptOut","channel":null,"settings":null,"recipients":{"kind":"ScopeUsers","users":[]},"mandatory":false,"createdAtUtc":"2030-01-01T00:00:00Z","lastModifiedAtUtc":"2030-01-01T00:00:00Z"}""";

    public static readonly string S6 =
        $$"""{"id":"{{Scenario.S6.Id}}","revision":1,"level":"User","companyId":null,"userId":"1","messageType":"B","kind":"Channel","channel":"Email","settings":{"language":"en","format":"PlainText"},"recipients":{"kind":"ScopeUsers","users":[]},"mandatory":false,"createdAtUtc":"2030-01-01T00:00:00Z","lastModifiedAtUtc":"2030-01-01T00:00:00Z"}""";

    public static readonly string S11 =
        $$"""{"id":"{{Scenario.S11.Id}}","revision":1,"level":"Company","companyId":"100","userId":null,"messageType":"D","kind":"Channel","channel":"Email","settings":{"language":null,"format":"Html"},"recipients":{"kind":"NamedUsers","users":["2"]},"mandatory":false,"createdAtUtc":"2030-01-01T00:00:00Z","lastModifiedAtUtc":"2030-01-01T00:00:00Z"}""";
}
