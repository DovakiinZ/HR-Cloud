using FluentValidation.Results;

namespace HR.Application.Common.Exceptions;

public class ValidationException : Exception
{
    public IDictionary<string, string[]> Errors { get; }

    public ValidationException() : base("One or more validation errors occurred.")
    {
        Errors = new Dictionary<string, string[]>();
    }

    public ValidationException(IEnumerable<ValidationFailure> failures) : base("One or more validation errors occurred.")
    {
        Errors = failures
            .GroupBy(e => e.PropertyName, e => e.ErrorMessage)
            .ToDictionary(g => g.Key, g => g.ToArray());
    }

    /// <summary>Returns a message that includes the field names and their error messages so callers
    /// (and tests) can inspect which fields were invalid without reading <see cref="Errors"/>.</summary>
    public override string Message =>
        Errors.Count == 0
            ? base.Message
            : $"{base.Message} {string.Join("; ", Errors.Select(kv => $"{kv.Key}: {string.Join(", ", kv.Value)}"))}";
}
