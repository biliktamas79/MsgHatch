using Microsoft.CodeAnalysis;

namespace MsgHatch.SourceGeneration;

/// <summary>The types of a route value or a query value, and the C# expression that writes a value into a URI (D236, D293).</summary>
internal static class HttpValues
{
    /// <summary>
    /// True for a string, a <c>Guid</c>, a number, a <c>bool</c>, an enum, or a <c>DateTime</c>. A query value can also be a
    /// nullable value type of these, which a request leaves out when it is null, or an <c>IReadOnlyList&lt;T&gt;</c> of these,
    /// which gives one pair for each item.
    /// </summary>
    public static bool CanWrite(ITypeSymbol type, bool isQueryValue) =>
        type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } wrapper
            ? isQueryValue && IsSingleValue(wrapper.TypeArguments[0])
            : IsSingleValue(type) || (isQueryValue && ItemTypeOf(type) is { } item && IsSingleValue(item));

    /// <summary>True when a value of the type can be null, so that a query leaves it out.</summary>
    public static bool CanBeNull(ITypeSymbol type) =>
        type.IsReferenceType || type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;

    /// <summary>Gives the type of the value of a nullable value type, or the type itself.</summary>
    public static ITypeSymbol ValueTypeOf(ITypeSymbol type) =>
        type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } wrapper ? wrapper.TypeArguments[0] : type;

    /// <summary>Gives the item type of an <c>IReadOnlyList&lt;T&gt;</c>, or null for another type.</summary>
    public static ITypeSymbol? ItemTypeOf(ITypeSymbol type) =>
        type is INamedTypeSymbol { IsGenericType: true } named
        && named.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IReadOnlyList<T>"
            ? named.TypeArguments[0]
            : null;

    /// <summary>
    /// Gives the expression of the escaped text of a value: an enum with its name, a number in the invariant culture, and a
    /// <c>DateTime</c> in the ISO 8601 format <c>O</c> of D57.
    /// </summary>
    public static string TextOf(ITypeSymbol type, string value) =>
        type.SpecialType == SpecialType.System_String ? $"global::System.Uri.EscapeDataString({value})"
        : type.SpecialType == SpecialType.System_Boolean ? $"({value} ? \"true\" : \"false\")"
        : type.SpecialType == SpecialType.System_DateTime
            ? $"global::System.Uri.EscapeDataString({value}.ToString(\"O\", global::System.Globalization.CultureInfo.InvariantCulture))"
        : type.TypeKind == TypeKind.Enum ? $"global::System.Uri.EscapeDataString({value}.ToString())"
        : IsGuid(type) ? $"{value}.ToString()"
        : $"{value}.ToString(global::System.Globalization.CultureInfo.InvariantCulture)";

    private static bool IsSingleValue(ITypeSymbol type) =>
        type.SpecialType is SpecialType.System_String or SpecialType.System_Boolean or SpecialType.System_DateTime
        || IsNumber(type) || IsGuid(type) || type.TypeKind == TypeKind.Enum;

    private static bool IsGuid(ITypeSymbol type) => type.ToDisplayString() == "System.Guid";

    private static bool IsNumber(ITypeSymbol type) => type.SpecialType is
        SpecialType.System_SByte or SpecialType.System_Byte or SpecialType.System_Int16 or SpecialType.System_UInt16
        or SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64
        or SpecialType.System_Decimal or SpecialType.System_Single or SpecialType.System_Double;
}
