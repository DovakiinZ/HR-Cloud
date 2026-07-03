namespace HR.Application.Common.Models;

public class ApiResponse<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string? Message { get; set; }
    public List<string>? Errors { get; set; }

    /// <summary>Optional machine-readable error code (e.g. PAYROLL_RUN_STALE, PAYROLL_PERIOD_CLOSED)
    /// so clients branch on it instead of parsing <see cref="Message"/>.</summary>
    public string? Code { get; set; }

    public static ApiResponse<T> Ok(T data, string? message = null) => new()
    {
        Success = true,
        Data = data,
        Message = message
    };

    public static ApiResponse<T> Fail(string message, List<string>? errors = null, string? code = null) => new()
    {
        Success = false,
        Message = message,
        Errors = errors,
        Code = code
    };
}

public class ApiResponse : ApiResponse<object>
{
    public static ApiResponse Ok(string? message = null) => new()
    {
        Success = true,
        Message = message
    };

    public new static ApiResponse Fail(string message, List<string>? errors = null, string? code = null) => new()
    {
        Success = false,
        Message = message,
        Errors = errors,
        Code = code
    };
}
