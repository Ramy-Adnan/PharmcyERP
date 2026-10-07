namespace PharmacyERP.Application.Common.Models;

/// <summary>
/// Generic outcome wrapper returned by Application use cases instead of
/// throwing exceptions for expected failure paths (e.g. "invalid credentials"),
/// so the WPF layer can render a friendly message without try/catch everywhere.
/// </summary>
public class Result
{
    public bool Succeeded { get; }
    public IReadOnlyList<string> Errors { get; }

    protected Result(bool succeeded, IEnumerable<string>? errors = null)
    {
        Succeeded = succeeded;
        Errors = (errors ?? Enumerable.Empty<string>()).ToList();
    }

    public static Result Success() => new(true);
    public static Result Failure(params string[] errors) => new(false, errors);
    public static Result Failure(IEnumerable<string> errors) => new(false, errors);
}

/// <summary>Result variant that also carries a return value on success.</summary>
public class Result<T> : Result
{
    public T? Value { get; }

    private Result(bool succeeded, T? value, IEnumerable<string>? errors = null) : base(succeeded, errors)
    {
        Value = value;
    }

    public static Result<T> Success(T value) => new(true, value);
    public static new Result<T> Failure(params string[] errors) => new(false, default, errors);
    public static new Result<T> Failure(IEnumerable<string> errors) => new(false, default, errors);
}
