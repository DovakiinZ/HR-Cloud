using HR.Domain.Common;

namespace HR.Domain.Engines.Finance.Entities;

/// <summary>The lifecycle of one export request.</summary>
public enum PayrollExportStatus { Pending = 1, Completed = 2, Failed = 3 }

/// <summary>A record of one payroll export: what was exported (a report type or a bank profile code), in
/// which format, and the immutable artifact it produced (a StoredFile). Kept as an entity so exports are
/// auditable and re-downloadable, and so generation can move to a background job without an API change.</summary>
public class PayrollExportJob : TenantEntity
{
    public Guid PayrollRunId { get; set; }

    /// <summary>Report type name (e.g. "RunSummary") or bank profile code (e.g. "SA_WPS_SIF").</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Output format name (Excel/Csv/Txt/Xml). Stored as a string to keep the entity independent
    /// of the Application-layer ExportFormat enum.</summary>
    public string Format { get; set; } = string.Empty;

    /// <summary>True when this export produced a bank file (IBAN/salary data) — drives the finer permission.</summary>
    public bool IsBankExport { get; set; }

    public PayrollExportStatus Status { get; set; } = PayrollExportStatus.Pending;

    /// <summary>The produced artifact (StoredFile) served via /api/files/{id}.</summary>
    public Guid? ArtifactStoredFileId { get; set; }
    public string? FileName { get; set; }
    public int RowCount { get; set; }

    public Guid RequestedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? Error { get; set; }
}
