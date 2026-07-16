using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HR.Application.Common.Exceptions;
using HR.Application.Engines.Finance.Export;
using HR.Domain.Engines.Reports;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Platform.Services.Reports;

/// <summary>Ties the report execution pipeline to the export framework: access-gate, run to completion,
/// flatten to a <see cref="TabularDataset"/>, and serialize via the writer matching the requested format.</summary>
public sealed class ReportExportService : IReportExportService
{
    private readonly ApplicationDbContext _db;
    private readonly IReportExecutionService _exec;
    private readonly IReportAccessService _access;
    private readonly IEnumerable<IExportWriter> _writers;

    public ReportExportService(ApplicationDbContext db, IReportExecutionService exec, IReportAccessService access, IEnumerable<IExportWriter> writers)
    { _db = db; _exec = exec; _access = access; _writers = writers; }

    public async Task<ReportExportFile> ExportAsync(Guid reportId, ExportFormat format, IReadOnlyDictionary<string, string?>? parameters, CancellationToken ct)
    {
        await _access.EnsureCanReadAsync(reportId, ct);

        var writer = _writers.FirstOrDefault(w => w.Format == format)
            ?? throw new ValidationException(new[] { new FluentValidation.Results.ValidationFailure("format", $"Unsupported export format '{format}'.") });

        var meta = await _db.Set<ReportDefinition>().Where(r => r.Id == reportId)
            .Select(r => new { r.NameEn, r.Code }).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("ReportDefinition", reportId);

        var result = await _exec.RunForExportAsync(reportId, parameters, ct);
        var dataset = ReportResultFlattener.Flatten(result, meta.NameEn);
        var bytes = writer.Write(dataset);

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd");
        var safe = string.IsNullOrWhiteSpace(meta.Code) ? "report" : meta.Code;
        var fileName = $"{safe}-{stamp}.{writer.Extension}";
        return new ReportExportFile(bytes, writer.ContentType, fileName);
    }
}
