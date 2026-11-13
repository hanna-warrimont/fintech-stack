namespace Portal.Instruments.Domain.Abstractions;

/// <summary>Ergebnis einer Operation, die fachlich scheitern kann – statt Exceptions für erwartbare Fehler.</summary>
public class Result
{
    protected Result(bool isSuccess, Error error)
    {
        if (isSuccess != (error == Error.None))
            throw new ArgumentException("Erfolg hat keinen Fehler, Misserfolg braucht einen.", nameof(error));
        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }

    public static Result Success() => new(true, Error.None);
    public static Result Failure(Error error) => new(false, error);
    public static Result<T> Success<T>(T value) => new(value, true, Error.None);
    public static Result<T> Failure<T>(Error error) => new(default, false, error);
}

public sealed class Result<T> : Result
{
    private readonly T? _value;

    internal Result(T? value, bool isSuccess, Error error) : base(isSuccess, error) => _value = value;

    public T Value => IsSuccess ? _value! : throw new InvalidOperationException($"Kein Wert bei Fehler {Error.Code}.");

    public static implicit operator Result<T>(T value) => Success(value);

    public static implicit operator Result<T>(Error error) => Failure<T>(error);
}
