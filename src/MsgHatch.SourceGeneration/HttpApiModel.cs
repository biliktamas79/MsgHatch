using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace MsgHatch.SourceGeneration;

/// <summary>Reads one interface with <c>[HttpApi]</c>, and gives its client source or its diagnostics (D236).</summary>
internal static class HttpApiModel
{
    private const string _category = "MsgHatch";
    private const string _failures = "MsgHatch.Domain.Failures";
    private static readonly string[] _httpMethods = ["GET", "POST", "PUT", "DELETE", "PATCH"];

    public static readonly DiagnosticDescriptor GenericOrNested = Error(
        "MSGHATCH002", "An HTTP API interface is generic or nested",
        "The HTTP API interface '{0}' is generic or nested, so the generator cannot write its client");

    public static readonly DiagnosticDescriptor NoJsonContext = Error(
        "MSGHATCH003", "The JSON context of an HTTP API is not a JsonSerializerContext",
        "The JSON context '{0}' of the HTTP API '{1}' is not a JsonSerializerContext");

    public static readonly DiagnosticDescriptor NoHttpCall = Error(
        "MSGHATCH004", "A member of an HTTP API is not a method with HttpCall",
        "The member '{0}' of the HTTP API '{1}' is not a method with the attribute HttpCall");

    public static readonly DiagnosticDescriptor UnknownHttpMethod = Error(
        "MSGHATCH005", "An HTTP method is not known",
        "The HTTP method '{0}' of '{1}' is not GET, POST, PUT, DELETE, or PATCH");

    public static readonly DiagnosticDescriptor RouteValueWithNoParameter = Error(
        "MSGHATCH006", "A route value has no parameter",
        "The route value '{0}' of '{1}' has no parameter with its name");

    public static readonly DiagnosticDescriptor SecondBody = Error(
        "MSGHATCH007", "A method has a second body",
        "The parameter '{0}' of '{1}' is a second parameter with the attribute HttpBody");

    public static readonly DiagnosticDescriptor UnsupportedValue = Error(
        "MSGHATCH008", "A route or query value has a type that the generator cannot write",
        "The parameter '{0}' of '{1}' has the type '{2}', which is not a string, a Guid, a number, a bool, or an enum, or a route value is nullable");

    public static readonly DiagnosticDescriptor UnsupportedReturn = Error(
        "MSGHATCH009", "A method does not return Task<ErrorOr<TResponse>> or Task<Error?>",
        "The method '{0}' does not return Task<ErrorOr<TResponse>> or Task<Error?>");

    public static readonly DiagnosticDescriptor NotInTheJsonContext = Error(
        "MSGHATCH010", "A body type is not in the JSON context",
        "The type '{0}' of '{1}' has no JsonTypeInfo property in the JSON context '{2}'");

    public static HttpApiOutput OutputOf(GeneratorAttributeSyntaxContext context)
    {
        var api = (INamedTypeSymbol)context.TargetSymbol;
        var hintName = api.ToDisplayString() + ".HttpClient.g.cs";
        var diagnostics = new List<Diagnostic>();
        if (api.IsGenericType || api.ContainingType is not null)
        {
            return new HttpApiOutput(hintName, null, [Diagnostic.Create(GenericOrNested, api.Locations[0], api.Name)]);
        }

        if (context.Attributes[0].ConstructorArguments[0].Value is not INamedTypeSymbol json || !IsJsonContext(json))
        {
            var name = (context.Attributes[0].ConstructorArguments[0].Value as ITypeSymbol)?.ToDisplayString() ?? "null";
            return new HttpApiOutput(hintName, null, [Diagnostic.Create(NoJsonContext, api.Locations[0], name, api.Name)]);
        }

        var calls = new List<string>();
        foreach (var member in api.GetMembers())
        {
            if (CallOf(member, json, diagnostics) is { } call)
            {
                calls.Add(call);
            }
        }

        return diagnostics.Count > 0
            ? new HttpApiOutput(hintName, null, [.. diagnostics])
            : new HttpApiOutput(hintName, HttpApiSource.ClientOf(api, calls), ImmutableArray<Diagnostic>.Empty);
    }

    private static string? CallOf(ISymbol member, INamedTypeSymbol json, List<Diagnostic> diagnostics)
    {
        if (member is IMethodSymbol { MethodKind: not MethodKind.Ordinary })
        {
            return null;
        }

        var attribute = member.GetAttributes().FirstOrDefault(static a => a.AttributeClass?.ToDisplayString() == HttpApiSource.CallAttribute);
        if (member is not IMethodSymbol { IsStatic: false } method || attribute is null)
        {
            diagnostics.Add(Diagnostic.Create(NoHttpCall, member.Locations[0], member.Name, member.ContainingType.Name));
            return null;
        }

        var count = diagnostics.Count;
        var httpMethod = attribute.ConstructorArguments[0].Value as string ?? string.Empty;
        if (!_httpMethods.Contains(httpMethod))
        {
            diagnostics.Add(Diagnostic.Create(UnknownHttpMethod, method.Locations[0], httpMethod, method.Name));
        }

        var response = ResponseOf(method, json, diagnostics);
        var call = new HttpCall(method, httpMethod, RoutePartsOf(attribute.ConstructorArguments[1].Value as string ?? string.Empty), response);
        CheckParameters(call, json, diagnostics);
        return diagnostics.Count == count ? HttpApiSource.MethodOf(call, json) : null;
    }

    private static string? ResponseOf(IMethodSymbol method, INamedTypeSymbol json, List<Diagnostic> diagnostics)
    {
        var result = method.ReturnType is INamedTypeSymbol { Name: "Task", Arity: 1 } task
            && task.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks"
                ? task.TypeArguments[0] as INamedTypeSymbol
                : null;
        if (result is { Name: "ErrorOr", Arity: 1 } && result.ContainingNamespace.ToDisplayString() == _failures)
        {
            return JsonPropertyOf(json, result.TypeArguments[0], method.Locations[0], method.Name, diagnostics);
        }

        if (result is not { Name: "Error", Arity: 0, NullableAnnotation: not NullableAnnotation.NotAnnotated }
            || result.ContainingNamespace.ToDisplayString() != _failures)
        {
            diagnostics.Add(Diagnostic.Create(UnsupportedReturn, method.Locations[0], method.Name));
        }

        return null;
    }

    private static void CheckParameters(HttpCall call, INamedTypeSymbol json, List<Diagnostic> diagnostics)
    {
        foreach (var name in call.Route.Where(static part => part.IsValue).Select(static part => part.Text).Distinct())
        {
            if (!call.Method.Parameters.Any(parameter => parameter.Name == name))
            {
                diagnostics.Add(Diagnostic.Create(RouteValueWithNoParameter, call.Method.Locations[0], name, call.Method.Name));
            }
        }

        var bodies = 0;
        foreach (var parameter in call.Method.Parameters)
        {
            if (HttpCall.IsBody(parameter))
            {
                bodies++;
                if (bodies > 1)
                {
                    diagnostics.Add(Diagnostic.Create(SecondBody, parameter.Locations[0], parameter.Name, call.Method.Name));
                }

                JsonPropertyOf(json, parameter.Type, parameter.Locations[0], call.Method.Name, diagnostics);
            }
            else if (!call.IsCancellation(parameter) && !HttpValues.CanWrite(parameter.Type, nullable: !call.IsRouteValue(parameter)))
            {
                diagnostics.Add(Diagnostic.Create(UnsupportedValue, parameter.Locations[0], parameter.Name, call.Method.Name, parameter.Type.ToDisplayString()));
            }
        }
    }

    private static string? JsonPropertyOf(INamedTypeSymbol json, ITypeSymbol type, Location location, string method, List<Diagnostic> diagnostics)
    {
        var property = json.GetMembers().OfType<IPropertySymbol>().FirstOrDefault(property =>
            property is { IsStatic: false, Type: INamedTypeSymbol { Name: "JsonTypeInfo", Arity: 1 } info }
            && info.ContainingNamespace.ToDisplayString() == "System.Text.Json.Serialization.Metadata"
            && SymbolEqualityComparer.Default.Equals(info.TypeArguments[0], type));
        if (property is null)
        {
            diagnostics.Add(Diagnostic.Create(NotInTheJsonContext, location, type.ToDisplayString(), method, json.Name));
        }

        return property?.Name;
    }

    private static bool IsJsonContext(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString() == "System.Text.Json.Serialization.JsonSerializerContext")
            {
                return true;
            }
        }

        return false;
    }

    private static List<RoutePart> RoutePartsOf(string route)
    {
        var parts = new List<RoutePart>();
        var index = 0;
        while (index < route.Length)
        {
            var open = route.IndexOf('{', index);
            var close = open < 0 ? -1 : route.IndexOf('}', open);
            if (close < 0)
            {
                parts.Add(new RoutePart(route.Substring(index), IsValue: false));
                break;
            }

            if (open > index)
            {
                parts.Add(new RoutePart(route.Substring(index, open - index), IsValue: false));
            }

            parts.Add(new RoutePart(NameOfRouteValue(route.Substring(open + 1, close - open - 1)), IsValue: true));
            index = close + 1;
        }

        return parts;
    }

    private static string NameOfRouteValue(string routeValue)
    {
        var end = routeValue.IndexOfAny([':', '=', '?']);
        return end < 0 ? routeValue : routeValue.Substring(0, end);
    }

    private static DiagnosticDescriptor Error(string id, string title, string message) =>
        new(id, title, message, _category, DiagnosticSeverity.Error, isEnabledByDefault: true);
}

/// <summary>One literal part or one route value of a route.</summary>
internal sealed record RoutePart(string Text, bool IsValue);

/// <summary>One method of an HTTP API with its HTTP method, its route, and the JSON property of its response, or null for <c>Error?</c>.</summary>
internal sealed record HttpCall(IMethodSymbol Method, string HttpMethod, List<RoutePart> Route, string? ResponseProperty)
{
    public static bool IsBody(IParameterSymbol parameter) =>
        parameter.GetAttributes().Any(static a => a.AttributeClass?.ToDisplayString() == HttpApiSource.BodyAttribute);

    public bool IsCancellation(IParameterSymbol parameter) =>
        SymbolEqualityComparer.Default.Equals(parameter, Method.Parameters.LastOrDefault())
        && parameter.Type.ToDisplayString() == "System.Threading.CancellationToken";

    public bool IsRouteValue(IParameterSymbol parameter) => Route.Any(part => part.IsValue && part.Text == parameter.Name);

    public IParameterSymbol? Cancellation => Method.Parameters.LastOrDefault() is { } last && IsCancellation(last) ? last : null;
}
