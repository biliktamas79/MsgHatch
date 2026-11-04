using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.DependencyInjection;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Subscriptions.Tests.Unit;

/// <summary>The standard logging wrapper tests for <see cref="IRecipientDirectory"/>.</summary>
public sealed class RecipientDirectoryLoggingWrapperUnitTests
{
    private const string _userEmail = "client1@acme.example";
    private const string _companyEmail = "admin@acme.example";
    private static readonly TenantKey _t1 = TenantKey.For(new TenantId("t1"));
    private static readonly IReadOnlyList<UserProfile> _users =
        [TestSubscriptions.AUser("1", email: _userEmail), TestSubscriptions.AUser("2", uiLanguage: "hu")];

    private static readonly CompanyProfile _company = TestSubscriptions.ACompany(email: _companyEmail);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string, object?> SuccessRows => new()
    {
        { "GetUsers", _users },
        { "GetCompanyUsers", _users },
        { "GetCompany", _company },
    };

    public static TheoryData<string, int> SuccessEventRows => new()
    {
        { "GetUsers", 1112 },
        { "GetCompanyUsers", 1114 },
        { "GetCompany", 1116 },
    };

    public static TheoryData<string, int, LogLevel, int> ErrorRows
    {
        get
        {
            var rows = new TheoryData<string, int, LogLevel, int>();
            foreach (var (operation, eventId) in new[] { ("GetUsers", 1113), ("GetCompanyUsers", 1115), ("GetCompany", 1117) })
            {
                rows.Add(operation, Errors.Unavailable.Code, LogLevel.Warning, eventId);
                rows.Add(operation, Errors.NotFound.Code, LogLevel.Error, eventId);
                rows.Add(operation, Errors.OperationCancelled.Code, LogLevel.Information, eventId);
            }

            return rows;
        }
    }

    public static TheoryData<string> Operations => ["GetUsers", "GetCompanyUsers", "GetCompany"];

    [Theory]
    [MemberData(nameof(SuccessRows))]
    public async Task Operation_InnerSuccess_ReturnsTheResultUnchanged(string operation, object? expected)
    {
        // Arrange
        await using var provider = BuildWith(ADirectory(error: null));
        await using var scope = provider.CreateAsyncScope();

        // Act
        var result = await CallAsync(scope.ServiceProvider.GetRequiredKeyedService<IRecipientDirectory>(_t1), operation);

        // Assert
        Assert.Equal((operation, true, (Error?)null), (operation, ReferenceEquals(expected, result.Value), result.Error));
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task Operation_InnerError_ReturnsTheSameError(string operation)
    {
        // Arrange
        var error = new Error(Errors.Unavailable);
        await using var provider = BuildWith(ADirectory(error));
        await using var scope = provider.CreateAsyncScope();

        // Act
        var result = await CallAsync(scope.ServiceProvider.GetRequiredKeyedService<IRecipientDirectory>(_t1), operation);

        // Assert
        Assert.Equal((operation, true), (operation, ReferenceEquals(error, result.Error)));
    }

    [Theory]
    [MemberData(nameof(SuccessEventRows))]
    public async Task Operation_InnerSuccess_LogsADebugRecordWithItsEventIdAndTheTenantOfTheKey(string operation, int eventId)
    {
        // Arrange
        await using var provider = BuildWith(ADirectory(error: null));
        await using var scope = provider.CreateAsyncScope();

        // Act
        await CallAsync(scope.ServiceProvider.GetRequiredKeyedService<IRecipientDirectory>(_t1), operation);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((operation, LogLevel.Debug, eventId, "t1"), (operation, record.Level, record.Id.Id, record.GetStructuredStateValue("Tenant")));
    }

    [Theory]
    [MemberData(nameof(ErrorRows))]
    public async Task Operation_InnerError_LogsTheLevelOfTheErrorWithItsEventId(string operation, int errorCode, LogLevel level, int eventId)
    {
        // Arrange
        await using var provider = BuildWith(ADirectory(new Error(ErrorTypeOf(errorCode))));
        await using var scope = provider.CreateAsyncScope();

        // Act
        await CallAsync(scope.ServiceProvider.GetRequiredKeyedService<IRecipientDirectory>(_t1), operation);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((operation, errorCode, level, eventId), (operation, errorCode, record.Level, record.Id.Id));
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task Operation_ProfilesWithEmailAddresses_NoLogRecordContainsAnAddress(string operation)
    {
        // Arrange
        await using var provider = BuildWith(ADirectory(error: null));
        await using var scope = provider.CreateAsyncScope();

        // Act
        await CallAsync(scope.ServiceProvider.GetRequiredKeyedService<IRecipientDirectory>(_t1), operation);

        // Assert
        Assert.Equal(
            (operation, 1, false),
            (operation, provider.LogRecords().Count, provider.LogRecords().Any(static record => record.ContainsText(_userEmail) || record.ContainsText(_companyEmail))));
    }

    [Fact]
    public async Task GetUsersAsync_DirectoryOfNoTenant_LogsNoTenant()
    {
        // Arrange
        await using var provider = BuildWith(ADirectory(error: null));
        await using var scope = provider.CreateAsyncScope();

        // Act
        await CallAsync(scope.ServiceProvider.GetRequiredService<IRecipientDirectory>(), "GetUsers");

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal(((string?)null, 1112), (record.GetStructuredStateValue("Tenant"), record.Id.Id));
    }

    [Fact]
    public async Task UseRecipientDirectory_EnableLoggingFalse_RegistersNoLoggingWrapper()
    {
        // Arrange
        var inner = ADirectory(error: null);
        await using var provider = BuildWith(inner, enableLogging: false);
        await using var scope = provider.CreateAsyncScope();

        // Act
        var directory = scope.ServiceProvider.GetRequiredKeyedService<IRecipientDirectory>(_t1);

        // Assert
        Assert.Same(inner, directory);
    }

    private static ErrorType ErrorTypeOf(int code) =>
        new[] { Errors.Unavailable, Errors.NotFound, Errors.OperationCancelled }.Single(type => type.Code == code);

    private static IRecipientDirectory ADirectory(Error? error)
    {
        var directory = Substitute.For<IRecipientDirectory>();
        directory.GetUsersAsync(default!, default)
            .ReturnsForAnyArgs(error is null ? ErrorOr<IReadOnlyList<UserProfile>>.ForValue(_users) : error);
        directory.GetCompanyUsersAsync(default, default)
            .ReturnsForAnyArgs(error is null ? ErrorOr<IReadOnlyList<UserProfile>>.ForValue(_users) : error);
        directory.GetCompanyAsync(default, default).ReturnsForAnyArgs(error is null ? ErrorOr<CompanyProfile?>.ForValue(_company) : error);
        return directory;
    }

    private static async Task<(object? Value, Error? Error)> CallAsync(IRecipientDirectory directory, string operation) =>
        operation switch
        {
            "GetUsers" => Of(await directory.GetUsersAsync([new UserId("1"), new UserId("2")], Ct)),
            "GetCompanyUsers" => Of(await directory.GetCompanyUsersAsync(new CompanyId("100"), Ct)),
            _ => Of(await directory.GetCompanyAsync(new CompanyId("100"), Ct)),
        };

    private static (object? Value, Error? Error) Of<T>(ErrorOr<T> result) => result.IsError ? (null, result.Error) : (result.Value, null);

    private static ServiceProvider BuildWith(IRecipientDirectory inner, bool enableLogging = true)
    {
        var services = TestHost.CreateServices();
        services.AddMsgHatch(o => o.EnableLogging = enableLogging).UseRecipientDirectory((_, _) => inner);
        return services.Build();
    }
}
