namespace Rva.SharedKernel.Results;

/// <summary>
/// Outcome of an operation that can fail in an expected way (.ai/rules/csharp-monolith.md section 6). Expected failures
/// are returned, never thrown; exceptions are for unexpected faults only.
/// </summary>
public class Result
{
    private readonly Error? error;

    protected Result(Error? error) => this.error = error;

    public bool IsSuccess => error is null;
    public bool IsFailure => !IsSuccess;

    /// <summary>The failure. Throws <see cref="InvalidOperationException"/> on a success.</summary>
    public Error Error => error ?? throw new InvalidOperationException("A successful result has no error.");

    public static Result Success() => new(null);

    public static Result Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(error);
    }

    public static Result<T> Success<T>(T value) => Result<T>.Success(value);
    public static Result<T> Failure<T>(Error error) => Result<T>.Failure(error);

    public static implicit operator Result(Error error) => Failure(error);
}

/// <summary>A <see cref="Result"/> that carries a value on success.</summary>
public sealed class Result<T> : Result
{
    private readonly T? value;

    private Result(T? value, Error? error) : base(error) => this.value = value;

    /// <summary>The value. Throws <see cref="InvalidOperationException"/> on a failure.</summary>
    public T Value => IsSuccess ? value! : throw new InvalidOperationException($"A failed result has no value ({Error.Code}).");

    public static Result<T> Success(T value) => new(value, null);

    public static new Result<T> Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(default, error);
    }

    public static implicit operator Result<T>(T value) => Success(value);
    public static implicit operator Result<T>(Error error) => Failure(error);
}
