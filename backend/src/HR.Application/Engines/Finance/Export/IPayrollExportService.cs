namespace HR.Application.Engines.Finance.Export;

/// <summary>Builds one payroll report as a format-agnostic dataset. DI-discovered by <see cref="Kind"/>;
/// a new report is a new provider, engine unchanged.</summary>
public interface IPayrollReportProvider
{
    string Kind { get; }
    string Title { get; }
    Task<TabularDataset> BuildAsync(Guid runId, CancellationToken ct);
}

/// <summary>Builds the canonical bank payment rows for a run (employee identity + IBAN/bank/net), which
/// every bank profile then maps and validates. Lives in the Payroll module (needs run + employee data).</summary>
public interface IBankPaymentSource
{
    Task<IReadOnlyList<Bank.BankPaymentRow>> BuildAsync(Guid runId, CancellationToken ct);
}

public sealed record ExportRequest(string Kind, string Format);

public sealed record ExportResult(
    Guid JobId, string Kind, string Format, bool IsBank, string Status, int RowCount,
    Guid? ArtifactFileId, string? FileName, string? Error, DateTime CreatedAt);

/// <summary>One selectable export in the UI: a report type or a bank profile.</summary>
public sealed record ExportOptionDto(string Kind, string Title, bool IsBank, string Format);

/// <summary>Orchestrates payroll exports: resolve a report provider or a bank profile → dataset → format
/// writer → StoredFile artifact → PayrollExportJob record. Implemented in the Payroll module.</summary>
public interface IPayrollExportService
{
    IReadOnlyList<ExportOptionDto> AvailableExports();
    bool IsBankKind(string kind);
    Task<ExportResult> CreateAsync(Guid runId, ExportRequest request, CancellationToken ct);
    Task<IReadOnlyList<ExportResult>> ListAsync(Guid runId, CancellationToken ct);
    Task<(byte[] bytes, string contentType, string fileName)?> DownloadAsync(Guid jobId, CancellationToken ct);
}
