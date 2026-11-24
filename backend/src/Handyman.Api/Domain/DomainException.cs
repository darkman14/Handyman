namespace Handyman.Api.Domain;

/// <summary>
/// A business rule was broken. The API maps it to 409 with <see cref="Code"/> as the stable problem-details code.
/// The message is written by us for the user-facing <c>detail</c>: never put internals in it.
/// </summary>
public class DomainException(string code, string message) : Exception(message)
{
    public const string DefaultCode = "business_rule_violated";

    public string Code { get; } = code;

    public DomainException(string message) : this(DefaultCode, message) { }
}

/// <summary>What the use case was asked for does not exist. The API maps it to 404 with code <c>&lt;thing&gt;_not_found</c>.</summary>
public class NotFoundException(string thing) : Exception($"The {thing.Replace('_', ' ')} was not found.")
{
    /// <summary>Snake-case name of what is missing, e.g. <c>job</c>, <c>bid</c>, <c>area</c>.</summary>
    public string Thing { get; } = thing;

    public string Code => $"{Thing}_not_found";
}
