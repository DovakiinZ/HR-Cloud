namespace HR.Modules.Platform.Services.Documents;

/// <summary>Thin seam over DocumentTokenResolver so the dispatcher can be unit-tested and depend on
/// an abstraction. One method, one implementation (the existing resolver).</summary>
public interface IRequestTokenResolver
{
    Task<IReadOnlyDictionary<string, string>> ResolveForRequestAsync(Guid requestInstanceId, CancellationToken ct);
}
