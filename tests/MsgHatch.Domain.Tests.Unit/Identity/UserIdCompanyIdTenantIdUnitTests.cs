using System.Globalization;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Tests.Unit.Identity;

/// <summary>
/// One data-driven test for <see cref="UserId"/>, <see cref="CompanyId"/>, and <see cref="TenantId"/>,
/// with the rows of the examples I1 to I6 of docs/subscription-domain.md.
/// </summary>
public sealed class UserIdCompanyIdTenantIdUnitTests
{
    public enum IdKind
    {
        User,
        Company,
        Tenant,
    }

    private const string _guidText = "3f2504e0-4f89-11d3-9a0c-0305e82c3301";

    public static TheoryData<IdKind, int, string> IntRows => ForEachKind<int, string>(
        (42, "42"), // I1
        (-5, "-5"), // I1
        (1234567, "1234567")); // I3

    public static TheoryData<IdKind, long, string> LongRows => ForEachKind<long, string>(
        (9000000000L, "9000000000")); // I1

    public static TheoryData<IdKind, Guid, string> GuidRows => ForEachKind<Guid, string>(
        (Guid.Parse("3F2504E0-4F89-11D3-9A0C-0305E82C3301"), _guidText)); // I2

    public static TheoryData<IdKind, string, int, string> CultureRows => ForEachKind<string, int, string>(
        ("hu-HU", 1234567, "1234567"), // I3
        ("ar-SA", 1234567, "1234567"), // I3
        ("hu-HU", -5, "-5"),
        ("ar-SA", -5, "-5"));

    public static TheoryData<IdKind, string?> InvalidTexts => ForEachKind<string?>(
        null, // I5
        "", // I5
        new string('a', 257), // I5: one above the maximum length
        "a\u0001b"); // I5: a control character

    public static TheoryData<IdKind, string> ValidTexts => ForEachKind<string>(
        new string('a', 256), // I5: at the maximum length
        "a", // one character, the minimum length
        "auth0|abc", // I5
        " ", // only whitespace is not a rule of the design
        "Ősz-űr \"<&>\\"); // accented and special characters

    [Theory]
    [MemberData(nameof(IntRows))]
    public void From_Int_GivesTheInvariantText(IdKind kind, int id, string expected)
    {
        // Act
        var text = _ops[kind].FromInt(id);

        // Assert
        Assert.Equal(expected, text);
    }

    [Theory]
    [MemberData(nameof(LongRows))]
    public void From_Long_GivesTheInvariantText(IdKind kind, long id, string expected)
    {
        // Act
        var text = _ops[kind].FromLong(id);

        // Assert
        Assert.Equal(expected, text);
    }

    [Theory]
    [MemberData(nameof(GuidRows))]
    public void From_Guid_GivesTheLowercaseDFormat(IdKind kind, Guid id, string expected)
    {
        // Act
        var text = _ops[kind].FromGuid(id);

        // Assert
        Assert.Equal(expected, text);
    }

    [Theory]
    [MemberData(nameof(CultureRows))]
    public void From_CurrentCultureOfTheThread_GivesTheInvariantText(IdKind kind, string culture, int id, string expected)
    {
        // Act
        var text = WithCurrentCulture(culture, () => _ops[kind].FromInt(id));

        // Assert
        Assert.Equal(expected, text);
    }

    [Theory]
    [MemberData(nameof(ForEachKindRows))]
    public void TryAs_IntText_ReadsTheIntBack(IdKind kind)
    {
        // Act
        var (success, id) = _ops[kind].TryAsInt("42"); // I4

        // Assert
        Assert.True(success);
        Assert.Equal(42, id);
    }

    [Theory]
    [MemberData(nameof(ForEachKindRows))]
    public void TryAs_TextThatIsNotAnInt_ReturnsFalse(IdKind kind)
    {
        // Act
        var (success, _) = _ops[kind].TryAsInt("abc"); // I4

        // Assert
        Assert.False(success);
    }

    [Theory]
    [MemberData(nameof(ForEachKindRows))]
    public void TryAs_GuidText_ReadsTheGuidBack(IdKind kind)
    {
        // Act
        var (success, id) = _ops[kind].TryAsGuid(_guidText); // I4

        // Assert
        Assert.True(success);
        Assert.Equal(Guid.Parse(_guidText), id);
    }

    [Theory]
    [MemberData(nameof(InvalidTexts))]
    public void Create_InvalidText_ReturnsValidationFailed(IdKind kind, string? text)
    {
        // Act
        var result = _ops[kind].Create(text);

        // Assert
        Assert.Equal(Errors.ValidationFailed, result.Error?.Type);
    }

    [Theory]
    [MemberData(nameof(ValidTexts))]
    public void Create_ValidText_ReturnsTheIdWithTheText(IdKind kind, string text)
    {
        // Act
        var result = _ops[kind].Create(text);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(text, result.Value);
    }

    [Theory]
    [MemberData(nameof(InvalidTexts))]
    public void Constructor_InvalidText_ThrowsArgumentException(IdKind kind, string? text)
    {
        // Act
        var exception = Record.Exception(() => _ops[kind].Construct(text));

        // Assert
        Assert.IsAssignableFrom<ArgumentException>(exception);
    }

    [Theory]
    [MemberData(nameof(ForEachKindRows))]
    public void Equals_TextsThatDifferOnlyInCase_AreNotEqual(IdKind kind)
    {
        // Act
        var equal = _ops[kind].TextsGiveEqualIds("A1", "a1"); // I6

        // Assert
        Assert.False(equal);
    }

    [Theory]
    [MemberData(nameof(ForEachKindRows))]
    public void Equals_FromIntAndTheSameText_AreEqual(IdKind kind)
    {
        // Act
        var equal = _ops[kind].FromIntEqualsText(42, "42"); // I6

        // Assert
        Assert.True(equal);
    }

    public static TheoryData<IdKind> ForEachKindRows => [IdKind.User, IdKind.Company, IdKind.Tenant];

    private sealed record IdOps(
        Func<int, string> FromInt,
        Func<long, string> FromLong,
        Func<Guid, string> FromGuid,
        Func<string, (bool Success, int Id)> TryAsInt,
        Func<string, (bool Success, Guid Id)> TryAsGuid,
        Func<string?, ErrorOr<string>> Create,
        Action<string?> Construct,
        Func<string, string, bool> TextsGiveEqualIds,
        Func<int, string, bool> FromIntEqualsText);

    private static readonly Dictionary<IdKind, IdOps> _ops = new()
    {
        [IdKind.User] = new(
            static id => UserId.From(id).Value,
            static id => UserId.From(id).Value,
            static id => UserId.From(id).Value,
            static text => (new UserId(text).TryAs(out int id), id),
            static text => (new UserId(text).TryAs(out Guid id), id),
            static text => UserId.Create(text).ThenIfSuccess(static id => ErrorOr<string>.ForValue(id.Value)),
            static text => _ = new UserId(text!),
            static (a, b) => new UserId(a) == new UserId(b),
            static (id, text) => UserId.From(id) == new UserId(text)),
        [IdKind.Company] = new(
            static id => CompanyId.From(id).Value,
            static id => CompanyId.From(id).Value,
            static id => CompanyId.From(id).Value,
            static text => (new CompanyId(text).TryAs(out int id), id),
            static text => (new CompanyId(text).TryAs(out Guid id), id),
            static text => CompanyId.Create(text).ThenIfSuccess(static id => ErrorOr<string>.ForValue(id.Value)),
            static text => _ = new CompanyId(text!),
            static (a, b) => new CompanyId(a) == new CompanyId(b),
            static (id, text) => CompanyId.From(id) == new CompanyId(text)),
        [IdKind.Tenant] = new(
            static id => TenantId.From(id).Value,
            static id => TenantId.From(id).Value,
            static id => TenantId.From(id).Value,
            static text => (new TenantId(text).TryAs(out int id), id),
            static text => (new TenantId(text).TryAs(out Guid id), id),
            static text => TenantId.Create(text).ThenIfSuccess(static id => ErrorOr<string>.ForValue(id.Value)),
            static text => _ = new TenantId(text!),
            static (a, b) => new TenantId(a) == new TenantId(b),
            static (id, text) => TenantId.From(id) == new TenantId(text)),
    };

    private static T WithCurrentCulture<T>(string culture, Func<T> action)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            return action();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    private static TheoryData<IdKind, T1, T2> ForEachKind<T1, T2>(params (T1, T2)[] rows)
    {
        var data = new TheoryData<IdKind, T1, T2>();
        foreach (var kind in Enum.GetValues<IdKind>())
        {
            foreach (var (first, second) in rows)
            {
                data.Add(kind, first, second);
            }
        }

        return data;
    }

    private static TheoryData<IdKind, T1, T2, T3> ForEachKind<T1, T2, T3>(params (T1, T2, T3)[] rows)
    {
        var data = new TheoryData<IdKind, T1, T2, T3>();
        foreach (var kind in Enum.GetValues<IdKind>())
        {
            foreach (var (first, second, third) in rows)
            {
                data.Add(kind, first, second, third);
            }
        }

        return data;
    }

    private static TheoryData<IdKind, T> ForEachKind<T>(params T[] rows)
    {
        var data = new TheoryData<IdKind, T>();
        foreach (var kind in Enum.GetValues<IdKind>())
        {
            foreach (var row in rows)
            {
                data.Add(kind, row);
            }
        }

        return data;
    }
}
