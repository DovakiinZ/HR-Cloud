using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HR.Application.Common.Interfaces;
using HR.Domain.Engines.Files;
using HR.Domain.Engines.Notifications;
using HR.Domain.Engines.Reports;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Platform.Services.Reports;

/// <summary>Pure scheduling helpers — unit-tested without a DB.</summary>
public static class ScheduleMath
{
    public static DateTime ComputeNextRun(ReportScheduleFrequency freq, DateTime fromUtc) => freq switch
    {
        ReportScheduleFrequency.Daily => fromUtc.AddDays(1),
        ReportScheduleFrequency.Weekly => fromUtc.AddDays(7),
        ReportScheduleFrequency.Monthly => fromUtc.AddMonths(1),
        ReportScheduleFrequency.Quarterly => fromUtc.AddMonths(3),
        _ => fromUtc.AddDays(1),
    };

    public static IReadOnlyList<string> ParseEmails(string recipientsJson)
    {
        if (string.IsNullOrWhiteSpace(recipientsJson)) return Array.Empty<string>();
        try
        {
            var arr = JsonSerializer.Deserialize<List<string>>(recipientsJson) ?? new();
            return arr.Where(s => !string.IsNullOrWhiteSpace(s) && s.Contains('@')).ToList();
        }
        catch (JsonException) { return Array.Empty<string>(); }
    }
}

/// <summary>Runs due report schedules: export → store file → enqueue email(s) with a download link,
/// then stamp LastRunAt and roll NextRunAt forward. One schedule failing does not abort the batch.</summary>
public sealed class ReportScheduleRunner : IReportScheduleRunner
{
    private readonly ApplicationDbContext _db;
    private readonly IReportExportService _export;
    private readonly IBackgroundExecutionContext _background;
    private readonly ILogger<ReportScheduleRunner> _logger;

    public ReportScheduleRunner(ApplicationDbContext db, IReportExportService export,
        IBackgroundExecutionContext background, ILogger<ReportScheduleRunner> logger)
    { _db = db; _export = export; _background = background; _logger = logger; }

    // Map the schedule's Domain enum -> the export service's Finance enum. Png has no report-file form -> Csv.
    private static HR.Application.Engines.Finance.Export.ExportFormat MapFormat(HR.Domain.Enums.ExportFormat f) => f switch
    {
        HR.Domain.Enums.ExportFormat.Xlsx => HR.Application.Engines.Finance.Export.ExportFormat.Excel,
        HR.Domain.Enums.ExportFormat.Csv  => HR.Application.Engines.Finance.Export.ExportFormat.Csv,
        HR.Domain.Enums.ExportFormat.Pdf  => HR.Application.Engines.Finance.Export.ExportFormat.Pdf,
        _ => HR.Application.Engines.Finance.Export.ExportFormat.Csv,
    };

    public async Task<int> RunDueAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        // ReportSchedule has no tenant query filter; IgnoreQueryFilters is harmless + explicit.
        var due = await _db.Set<ReportSchedule>().IgnoreQueryFilters()
            .Where(s => s.IsActive && (s.NextRunAt == null || s.NextRunAt <= now))
            .ToListAsync(ct);

        var processed = 0;
        foreach (var schedule in due)
        {
            try
            {
                // ReportDefinition IS tenant-filtered -> read across tenants with IgnoreQueryFilters.
                var report = await _db.Set<ReportDefinition>().IgnoreQueryFilters()
                    .Where(r => r.Id == schedule.ReportDefinitionId)
                    .Select(r => new { r.NameEn, r.Code, r.TenantId, r.OwnerId })
                    .FirstOrDefaultAsync(ct);
                if (report is null) { schedule.IsActive = false; await _db.SaveChangesAsync(ct); continue; }

                // Re-establish tenant + owner so ExportAsync's access checks + query filters work.
                using (_background.Begin(report.TenantId, report.OwnerId))
                {
                    var file = await _export.ExportAsync(schedule.ReportDefinitionId, MapFormat(schedule.ExportFormat), null, ct);

                    var stored = new StoredFile
                    {
                        TenantId = report.TenantId,
                        FileName = file.FileName,
                        ContentType = file.ContentType,
                        Data = file.Content,
                        SizeBytes = file.Content.LongLength,
                        Category = "ReportSchedule",
                    };
                    _db.Set<StoredFile>().Add(stored);
                    await _db.SaveChangesAsync(ct);

                    var link = $"/api/files/{stored.Id}";
                    foreach (var email in ScheduleMath.ParseEmails(schedule.Recipients))
                    {
                        _db.EmailQueue.Add(new EmailNotificationQueue
                        {
                            TenantId = report.TenantId,
                            ToEmail = email,
                            Subject = $"تقرير مجدول: {report.NameEn}",
                            Body = $"تم إنشاء التقرير \"{report.NameEn}\" في {now:yyyy-MM-dd HH:mm} UTC.\nرابط التنزيل: {link}",
                            Category = "ReportSchedule",
                            EntityId = schedule.ReportDefinitionId,
                            Link = link,
                            Status = EmailQueueStatus.Pending,
                        });
                    }

                    schedule.LastRunAt = now;
                    schedule.NextRunAt = ScheduleMath.ComputeNextRun(schedule.Frequency, now);
                    await _db.SaveChangesAsync(ct);
                }
                processed++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Report schedule {ScheduleId} failed.", schedule.Id);
            }
        }
        return processed;
    }
}
