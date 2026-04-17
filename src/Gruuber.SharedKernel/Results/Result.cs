namespace Gruuber.SharedKernel.Results;

public class Result<T>
{
    public bool IsSuccess { get; private set; }
    public T? Value { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }

    private Result() { }

    public static Result<T> Success(T value) =>
        new() { IsSuccess = true, Value = value };

    public static Result<T> Failure(string errorCode, string errorMessage) =>
        new() { IsSuccess = false, ErrorCode = errorCode, ErrorMessage = errorMessage };
}

public class ApplicationResult<T>
{
    public bool IsSuccess { get; private set; }
    public T? Data { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public int StatusCode { get; private set; }

    private ApplicationResult() { }

    public static ApplicationResult<T> Success(T data, int statusCode = 200) =>
        new() { IsSuccess = true, Data = data, StatusCode = statusCode };

    public static ApplicationResult<T> Accepted(T data) =>
        new() { IsSuccess = true, Data = data, StatusCode = 202 };

    public static ApplicationResult<T> Failure(string errorCode, string errorMessage, int statusCode = 400) =>
        new() { IsSuccess = false, ErrorCode = errorCode, ErrorMessage = errorMessage, StatusCode = statusCode };

    public static ApplicationResult<T> Conflict(Guid entityId, long currentVersion) =>
        new()
        {
            IsSuccess = false,
            ErrorCode = "RESOURCE_CONFLICTED",
            ErrorMessage = $"The resource was modified by another request. Current version: {currentVersion}.",
            StatusCode = 409
        };
}

public record ConflictDetail(Guid EntityId, long CurrentVersion);
