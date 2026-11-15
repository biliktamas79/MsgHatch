using System.Reflection;
using Fluid;

namespace MsgHatch.Email.Liquid;

/// <summary>
/// The Fluid options of one payload type, built one time (D209, D210): a limit of 100,000 steps, the time zone UTC, an
/// unknown name as a <see cref="FluidException"/> (D207), and the member types of the payload. A member type is each
/// class, record, or struct that the public properties of the payload reach, also as the element type of a collection.
/// A type of the .NET runtime is not a member type, so a template cannot read its members.
/// </summary>
/// <typeparam name="TPayload">The payload type.</typeparam>
internal static class LiquidOptions<TPayload>
{
    public static TemplateOptions Options { get; } = Create();

    private static TemplateOptions Create()
    {
        var options = new TemplateOptions
        {
            MaxSteps = 100_000,
            TimeZone = TimeZoneInfo.Utc,
            Undefined = static name => throw new FluidException($"The template has the unknown name {name}."),
        };
        var memberTypes = new HashSet<Type>();
        AddMemberTypes(typeof(TPayload), memberTypes);
        foreach (var type in memberTypes)
        {
            options.MemberAccessStrategy.Register(type);
        }

        return options;
    }

    private static void AddMemberTypes(Type type, HashSet<Type> memberTypes)
    {
        if (ElementTypeOf(type) is { } element)
        {
            AddMemberTypes(element, memberTypes);
        }
        else if (IsMemberType(type) && memberTypes.Add(type))
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                AddMemberTypes(property.PropertyType, memberTypes);
            }
        }
    }

    private static Type? ElementTypeOf(Type type) =>
        Nullable.GetUnderlyingType(type)
        ?? (IsEnumerable(type) ? type : type.GetInterfaces().FirstOrDefault(IsEnumerable))?.GetGenericArguments()[0];

    private static bool IsEnumerable(Type type) => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>);

    private static bool IsMemberType(Type type) =>
        (type.IsClass || (type.IsValueType && !type.IsPrimitive && !type.IsEnum))
        && type.Namespace is not "System"
        && type.Namespace?.StartsWith("System.", StringComparison.Ordinal) != true;
}
