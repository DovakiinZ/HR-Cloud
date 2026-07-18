namespace HR.Application.Reports.Registry;

public interface IReportFieldRegistry
{
    IReadOnlyList<ReportSubjectDescriptor> GetSubjects(ReportRegistryContext ctx);
    IReadOnlyList<ReportFieldDescriptor> GetFields(ReportRegistryContext ctx, string subject);
    ReportFieldDescriptor? GetField(ReportRegistryContext ctx, string key);
    ReportResolveResult Resolve(ReportRegistryContext ctx, IReadOnlyCollection<string> keys);
    ReportRegistryHealth GetHealth();
}
