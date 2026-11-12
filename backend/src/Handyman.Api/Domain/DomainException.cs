namespace Handyman.Api.Domain;

/// <summary>A business rule was broken. The API maps it to 409.</summary>
public class DomainException(string message) : Exception(message);
