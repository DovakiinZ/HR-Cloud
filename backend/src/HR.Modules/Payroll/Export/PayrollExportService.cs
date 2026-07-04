using HR.Application.Common.Exceptions;
using HR.Application.Common.Interfaces;
using HR.Application.Engines.Finance.Export;
using HR.Application.Engines.Finance.Export.Bank;
using HR.Domain.Engines.Files;
using HR.Domain.Engines.Finance.Entities;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Payroll.Export;

/// <summary>Orchestrates payroll exports: resolve a report provider or a bank profile → build a dataset →
/// validate (bank) → serialize with the matching format writer → persist an immutable StoredFile artifact
/// and a PayrollExportJob record. Bank validation is enforced here, separate from generation.</summary>
public sealed class PayrollExportService : IPayrollExportService
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly IEnumerable<IPayrollReportProvider> _providers;
    private readonly IEnumerable<IExportWriter> _writers;
    private readonly IEnumerable<IBankProfile> _profiles;
    private readonly IEnumerable<IBankExportValidator> _validators;
    private readonly IBankPaymentSource _bankSource;

    public PayrollExportService(
        ApplicationDbContext db, ICurrentUserService user,
        IEnumerable<IPayrollReportProvider> providers, IEnumerable<IExportWriter> writers,
        IEnumerable<IBankProfile> profiles, IEnumerable<IBankExportValidator> validators,
        IBankPaymentSource bankSource)
    {
        _db = db; _user = user; _providers = providers; _writers = writers;
        _profiles = profiles; _validators = validators; _bankSource = bankSource;
    }

    public IReadOnlyList<ExportOptionDto> AvailableExports()
    {
        var reports = _providers.Select(p => new ExportOptionDto(p.Kind, p.Title, false, "Excel"));
        var banks = _profiles.Select(pr => new ExportOptionDto(pr.Code, pr.DisplayName, true, pr.Format.ToString()));
        return reports.Concat(banks).ToList();
    }

    public bool IsBankKind(string kind) => _profiles.Any(p => string.Equals(p.Code, kind, StringComparison.OrdinalIgnoreCase));

    public async Task<ExportResult> CreateAsync(Guid runId, ExportRequest request, CancellationToken ct)
    {
        TabularDataset dataset;
        bool isBank;
        ExportFormat format;

        var profile = _profiles.FirstOrDefault(p => string.Equals(p.Code, request.Kind, StringComparison.OrdinalIgnoreCase));
        if (profile is not null)
        {
            isBank = true;
            format = profile.Format;
            var rows = await _bankSource.BuildAsync(runId, ct);
            var validator = _validators.FirstOrDefault(v => string.Equals(v.ProfileCode, profile.Code, StringComparison.OrdinalIgnoreCase));
            var errors = validator?.Validate(rows) ?? Array.Empty<BankValidationError>();
            if (errors.Count > 0)
                throw new DomainException(
                    $"تعذّر إنشاء ملف البنك: {errors.Count} خطأ في بيانات الموظفين (أول خطأ: {errors[0].EmployeeNumber} — {errors[0].Message}).",
                    "BANK_EXPORT_INVALID");
            dataset = BankFieldMapper.Map(rows, profile);
        }
        else
        {
            var provider = _providers.FirstOrDefault(p => string.Equals(p.Kind, request.Kind, StringComparison.OrdinalIgnoreCase))
                ?? throw new DomainException($"نوع تصدير غير معروف: {request.Kind}", "EXPORT_KIND_UNKNOWN");
            isBank = false;
            if (!Enum.TryParse(request.Format, ignoreCase: true, out format)) format = ExportFormat.Excel;
            dataset = await provider.BuildAsync(runId, ct);
        }

        var writer = _writers.FirstOrDefault(w => w.Format == format)
            ?? throw new DomainException($"صيغة غير مدعومة: {format}", "EXPORT_FORMAT_UNSUPPORTED");

        var bytes = writer.Write(dataset);
        var fileName = $"{request.Kind}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.{writer.Extension}";

        var stored = new StoredFile
        {
            TenantId = _user.TenantId,
            FileName = fileName,
            ContentType = writer.ContentType,
            Data = bytes,
            SizeBytes = bytes.LongLength,
            Category = "payroll-export",
        };
        _db.Files.Add(stored);

        var job = new PayrollExportJob
        {
            PayrollRunId = runId,
            Kind = request.Kind,
            Format = format.ToString(),
            IsBankExport = isBank,
            Status = PayrollExportStatus.Completed,
            ArtifactStoredFileId = stored.Id,
            FileName = fileName,
            RowCount = dataset.Rows.Count,
            RequestedByUserId = _user.UserId,
            CreatedAt = DateTime.UtcNow,
        };
        _db.PayrollExportJobs.Add(job);
        await _db.SaveChangesAsync(ct);

        return ToResult(job);
    }

    public async Task<IReadOnlyList<ExportResult>> ListAsync(Guid runId, CancellationToken ct)
    {
        var jobs = await _db.PayrollExportJobs.AsNoTracking()
            .Where(j => j.PayrollRunId == runId)
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync(ct);
        return jobs.Select(ToResult).ToList();
    }

    public async Task<(byte[] bytes, string contentType, string fileName)?> DownloadAsync(Guid jobId, CancellationToken ct)
    {
        var job = await _db.PayrollExportJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job?.ArtifactStoredFileId is not { } fileId) return null;
        var file = await _db.Files.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fileId, ct);
        if (file is null) return null;
        return (file.Data, file.ContentType, job.FileName ?? file.FileName);
    }

    private static ExportResult ToResult(PayrollExportJob j) => new(
        j.Id, j.Kind, j.Format, j.IsBankExport, j.Status.ToString(), j.RowCount,
        j.ArtifactStoredFileId, j.FileName, j.Error, j.CreatedAt);
}
