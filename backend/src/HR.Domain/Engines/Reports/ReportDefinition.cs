using HR.Domain.Common;
using HR.Domain.Enums;

namespace HR.Domain.Engines.Reports;

public class ReportDefinition : TenantEntity
{
    public string Code { get; set; } = null!;
    public string NameEn { get; set; } = null!;
    public string NameAr { get; set; } = null!;
    public string? Description { get; set; }
    public ReportType ReportType { get; set; }
    public ReportScope Scope { get; set; } = ReportScope.Personal;
    public Guid? OwnerId { get; set; }
    public Guid? TemplateId { get; set; }
    public Guid? FolderId { get; set; }
    public Guid PrimaryObjectId { get; set; } // Main object for report
    public bool IsPublished { get; set; }
    public bool IsActive { get; set; } = true;
    public int Version { get; set; } = 1;

    public ReportTemplate? Template { get; set; }
    public ICollection<ReportField> Fields { get; set; } = new List<ReportField>();
    public ICollection<ReportRelationship> Relationships { get; set; } = new List<ReportRelationship>();
    public ICollection<ReportFilter> Filters { get; set; } = new List<ReportFilter>();
    public ICollection<ReportGrouping> Groupings { get; set; } = new List<ReportGrouping>();
    public ICollection<ReportSorting> Sortings { get; set; } = new List<ReportSorting>();
    public ICollection<ReportSchedule> Schedules { get; set; } = new List<ReportSchedule>();
    public ICollection<ReportShare> Shares { get; set; } = new List<ReportShare>();
}
