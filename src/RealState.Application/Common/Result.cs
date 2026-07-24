namespace RealState.Application.Common;

/// <summary>Lightweight operation result used by application services instead of throwing for expected failures.</summary>
public class Result
{
    public bool Succeeded { get; protected set; }
    public IReadOnlyList<string> Errors { get; protected set; } = Array.Empty<string>();

    public static Result Success() => new() { Succeeded = true };
    public static Result Failure(params string[] errors) => new() { Succeeded = false, Errors = errors };
}

public sealed class Result<T> : Result
{
    public T? Value { get; private set; }

    public static Result<T> Success(T value) => new() { Succeeded = true, Value = value };
    public new static Result<T> Failure(params string[] errors) => new() { Succeeded = false, Errors = errors };
}
