using Microsoft.CodeAnalysis;

namespace MsgHatch.SourceGeneration;

/// <summary>The types of a route value or a query value, and the C# expression that writes a value into a URI (D236).</summary>
internal static class HttpValues
{
    /// <summary>
    /// True for a string, a <c>Guid</c>, a number, a <c>bool</c>, or an enum. A query value can also be a nullable value
    /// type of these, and a request leaves it out when it is null.
    /// </summary>
    public static bool CanWrite(ITypeSymbol type, bool nullable) =>
        type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } wrapper
            ? nullable && CanWrite(wrapper.TypeArguments[0], nullable: false)
            : type.SpecialType is SpecialType.System_String or SpecialType.System_Boolean || IsNumber(type) || IsGuid(type)
                || type.TypeKind == TypeKind.Enum;

    /// <summary>True when a value of the type can be null, so that a query leaves it out.</summary>
    public static bool CanBeNull(ITypeSymbol type) =>
        type.IsReferenceType || type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;

    /// <summary>Gives the type of the value of a nullable value type, or the type itself.</summary>
    public static ITypeSymbol ValueTypeOf(ITypeSymbol type) =>
        type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } wrapper ? wrapper.TypeArguments[0] : type;

    /// <summary>Gives the expression of the escaped text of a value: an enum with its name, and a number in the invariant culture.</summary>
    public static string TextOf(ITypeSymbol type, string value) =>
        type.SpecialType == SpecialType.System_String ? $"global::System.Uri.EscapeDataString({value})"
        : type.SpecialType == SpecialType.System_Boolean ? $"({value} ? \"true\" : \"false\")"
        : type.TypeKind == TypeKind.Enum ? $"global::System.Uri.EscapeDataString({value}.ToString())"
        : IsGuid(type) ? $"{value}.ToString()"
        : $"{value}.ToString(global::System.Globalization.CultureInfo.InvariantCulture)";

    private static bool IsGuid(ITypeSymbol type) => type.ToDisplayString() == "System.Guid";

    private static bool IsNumber(ITypeSymbol type) => type.SpecialType is
        SpecialType.System_SByte or SpecialType.System_Byte or SpecialType.System_Int16 or SpecialType.System_UInt16
        or SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64
        or SpecialType.System_Decimal or SpecialType.System_Single or SpecialType.System_Double;
}
