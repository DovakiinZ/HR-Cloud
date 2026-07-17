using HR.Domain.Common;
using HR.Domain.Enums;

namespace HR.Domain.Engines.Reports;

public class ReportSchedule : BaseEntity
{
    public Guid ReportDefinitionId { get; set; }
    public ReportScheduleFrequency Frequency { get; set; }
    public string? CronExpression { get; set; }
    /// <summary>Minutes past midnight (Asia/Riyadh) to send. Null = 0 (00:00).</summary>
    public int? TimeOfDayMinutes { get; set; }
    /// <summary>0=Sunday..6=Saturday for Weekly. Null = anchor to creation weekday at compute time.</summary>
    public int? DayOfWeek { get; set; }
    /// <summary>1..28 (clamped) for Monthly/Quarterly. Null = day 1.</summary>
    public int? DayOfMonth { get; set; }
    public ExportFormat ExportFormat { get; set; }
    public string Recipients { get; set; } = null!; // JSONB - list of email/userId
    public bool IsActive { get; set; } = true;
    public DateTime? LastRunAt { get; set; }
    public DateTime? NextRunAt { get; set; }

    public ReportDefinition ReportDefinition { get; set; } = null!;
}
