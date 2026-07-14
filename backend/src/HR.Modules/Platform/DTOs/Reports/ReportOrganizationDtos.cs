using System;

namespace HR.Modules.Platform.DTOs.Reports;

public class ReportFolderDto
{
    public Guid Id { get; set; }
    public string NameEn { get; set; } = null!;
    public string NameAr { get; set; } = null!;
    public Guid? ParentFolderId { get; set; }
}

public class ReportTagDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Color { get; set; }
}
