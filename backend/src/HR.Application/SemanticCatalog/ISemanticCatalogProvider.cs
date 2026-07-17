using HR.Application.SemanticCatalog.Contracts;

namespace HR.Application.SemanticCatalog;

public sealed record CatalogQueryContext(IReadOnlyCollection<string> Permissions);

public sealed record SemanticSearchHit(string Kind, string Code, string NameAr, string NameEn, double Score);

public interface ISemanticCatalogProvider
{
    IReadOnlyList<SemanticDomain> GetDomains(CatalogQueryContext ctx);
    IReadOnlyList<SemanticObject> GetObjects(CatalogQueryContext ctx, string? domainCode = null);
    SemanticObject? GetObject(CatalogQueryContext ctx, string objectCode);
    IReadOnlyList<SemanticMetric> GetMetrics(CatalogQueryContext ctx, string? domainCode = null);
    SemanticMetric? GetMetric(CatalogQueryContext ctx, string metricCode);
    IReadOnlyList<SemanticSearchHit> Search(CatalogQueryContext ctx, string query);
    CatalogHealth GetHealth();
}
