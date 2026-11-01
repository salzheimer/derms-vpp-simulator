namespace Rva.SharedKernel.Results;

/// <summary>
/// Kind of expected failure. Each kind maps to one HTTP status at a module's endpoints:
/// Validation 400, NotFound 404, Conflict 409, Rule 422.
/// </summary>
public enum ErrorKind
{
    /// <summary>The request is malformed: shape, ranges, missing fields, times in the future.</summary>
    Validation,

    /// <summary>The resource in the route, or its version as of a time, does not exist.</summary>
    NotFound,

    /// <summary>The change conflicts with current state: duplicate code, illegal transition, concurrent change.</summary>
    Conflict,

    /// <summary>A domain invariant is broken by a reference in the request, ex. an unknown connection node.</summary>
    Rule,
}

/// <summary>
/// An expected failure. <see cref="Code"/> is stable and namespaced by module (ex. <c>GridTopology.AssetNotFound</c>),
/// so callers match on it; <see cref="Message"/> is for people and may change.
/// </summary>
public sealed record Error
{
    public Error(string code, string message, ErrorKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown error kind.");
        Code = code;
        Message = message;
        Kind = kind;
    }

    public string Code { get; }
    public string Message { get; }
    public ErrorKind Kind { get; }

    public static Error Validation(string code, string message) => new(code, message, ErrorKind.Validation);
    public static Error NotFound(string code, string message) => new(code, message, ErrorKind.NotFound);
    public static Error Conflict(string code, string message) => new(code, message, ErrorKind.Conflict);
    public static Error Rule(string code, string message) => new(code, message, ErrorKind.Rule);
}
