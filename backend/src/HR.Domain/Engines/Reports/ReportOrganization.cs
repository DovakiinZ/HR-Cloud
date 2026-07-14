using System;
using HR.Domain.Common;

namespace HR.Domain.Engines.Reports;

public class ReportFolder : TenantEntity
{
    public string NameEn { get; set; } = null!;
    public string NameAr { get; set; } = null!;
    public Guid? ParentFolderId { get; set; }
}

public class ReportTag : TenantEntity
{
    public string Name { get; set; } = null!;
    public string? Color { get; set; }
}

public class ReportDefinitionTag : BaseEntity
{
    public Guid ReportDefinitionId { get; set; }
    public Guid ReportTagId { get; set; }
}

public class ReportUserState : TenantEntity
{
    public Guid UserId { get; set; }
    public Guid ReportDefinitionId { get; set; }
    public bool IsFavorite { get; set; }
    public bool IsPinned { get; set; }
    public DateTime? LastViewedAt { get; set; }
}
