
namespace CoffeeLoyalty.Core.DTOs;

// Represents the result of an operation without using WPF.
public class OperationResult
{
    // Shows whether the operation was successful.
    public bool IsSuccess { get; }

    // Shows whether the operation failed.
    public bool IsFailure => !IsSuccess;

    // Contains a readable error message when something fails.
    public string? ErrorMessage { get; }

    // Contains an optional error code.
    public string? ErrorCode { get; }

    // Creates a result and checks that its state is valid.
    protected OperationResult(
        bool isSuccess,
        string? errorMessage,
        string? errorCode)
    {
        // A successful result must not contain an error.
        if (isSuccess &&
            (errorMessage != null || errorCode != null))
        {
            throw new ArgumentException(
                "A successful result cannot contain errors.");
        }

        // A failed result must have a readable error message.
        if (!isSuccess && string.IsNullOrWhiteSpace(errorMessage))
        {
            throw new ArgumentException(
                "An error message is required for a failed result.");
        }

        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
        ErrorCode = errorCode;
    }

    // Creates a successful result without returning data.
    public static OperationResult Success()
    {
        return new OperationResult(true, null, null);
    }

    // Creates a failed result with an error message.
    public static OperationResult Failure(
        string errorMessage,
        string? errorCode = null)
    {
        return new OperationResult(false, errorMessage, errorCode);
    }
}

// Represents a result that can also return data of type T.
public sealed class OperationResult<T> : OperationResult
{
    // Contains the returned data when the operation succeeds.
    public T? Value { get; }

    // Creates a typed result using the base class.
    private OperationResult(
        bool isSuccess,
        T? value,
        string? errorMessage,
        string? errorCode)
        : base(isSuccess, errorMessage, errorCode)
    {
        // A successful typed result must contain a value.
        if (isSuccess && value is null)
        {
            throw new ArgumentException(
                "A successful result must contain a value.");
        }

        Value = value;
    }

    // Creates a successful result containing data.
    public new static OperationResult<T> Success(T value)
    {
        return new OperationResult<T>(
            true, value, null, null);
    }

    // Creates a failed result without returned data.
    public new static OperationResult<T> Failure(
        string errorMessage,
        string? errorCode = null)
    {
        return new OperationResult<T>(
            false, default, errorMessage, errorCode);
    }
}
