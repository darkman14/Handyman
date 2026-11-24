using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;

namespace Handyman.Api.Infrastructure;

/// <summary>
/// Endpoint-level validation with data annotations, so handlers only ever see valid input.
/// Checks the properties of body/complex arguments and the attributes on the handler's own parameters
/// (e.g. <c>[Range(1, 50)] int page</c>). Failure: 400 problem details, code <c>validation_failed</c>,
/// <c>errors</c> = { field: [messages] }. Nested objects are not walked; keep request records flat or annotate them at the top.
/// Added once to the <c>/api</c> group in Program.cs. Reflection (not the .NET 10 source-generated AddValidation) on purpose:
/// it does not depend on which assembly the request type lives in.
/// </summary>
public sealed class ValidationFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var errors = new Dictionary<string, List<string>>();
        var parameters = context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<MethodInfo>()?.GetParameters() ?? [];

        for (var i = 0; i < context.Arguments.Count; i++)
        {
            var argument = context.Arguments[i];
            if (argument is null) continue;
            var parameter = i < parameters.Length ? parameters[i] : null;

            if (parameter is not null)
                ValidateParameter(parameter, argument, errors);
            if (IsRequestObject(argument))
                ValidateObject(argument, errors);
        }

        if (errors.Count == 0)
            return await next(context);

        var problem = ApiExceptionHandler.ProblemFor(StatusCodes.Status400BadRequest, ApiExceptionHandler.ValidationFailed,
            "Validation failed", "One or more fields are invalid.");
        problem.Extensions["errors"] = errors.ToDictionary(e => e.Key, e => e.Value.ToArray());
        return Results.Json(problem, statusCode: StatusCodes.Status400BadRequest, contentType: "application/problem+json");
    }

    private static bool IsRequestObject(object argument)
    {
        var type = argument.GetType();
        return type.IsClass && type != typeof(string)
            && !typeof(HttpContext).IsAssignableFrom(type)
            && !type.Namespace!.StartsWith("System", StringComparison.Ordinal)
            && !type.Namespace.StartsWith("Microsoft", StringComparison.Ordinal)
            && !type.Namespace.StartsWith("Handyman.Api.Data", StringComparison.Ordinal);
    }

    private static void ValidateParameter(ParameterInfo parameter, object value, Dictionary<string, List<string>> errors)
    {
        var attributes = parameter.GetCustomAttributes<ValidationAttribute>().ToList();
        if (attributes.Count == 0) return;
        var name = parameter.Name ?? "value";
        var results = new List<ValidationResult>();
        Validator.TryValidateValue(value, new ValidationContext(value) { MemberName = name, DisplayName = name }, results, attributes);
        foreach (var result in results)
            Add(errors, name, result.ErrorMessage);
    }

    private static void ValidateObject(object value, Dictionary<string, List<string>> errors)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(value, new ValidationContext(value), results, validateAllProperties: true);
        foreach (var result in results)
        {
            var members = result.MemberNames.Any() ? result.MemberNames : [""];
            foreach (var member in members)
                Add(errors, JsonNamingPolicy.CamelCase.ConvertName(member), result.ErrorMessage);
        }
    }

    private static void Add(Dictionary<string, List<string>> errors, string field, string? message)
    {
        if (!errors.TryGetValue(field, out var list)) errors[field] = list = [];
        list.Add(message ?? "Invalid value.");
    }
}
