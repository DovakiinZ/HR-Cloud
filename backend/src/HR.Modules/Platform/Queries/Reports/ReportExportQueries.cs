using System;
using System.Threading;
using System.Threading.Tasks;
using HR.Application.Common.Exceptions;
using HR.Application.Engines.Finance.Export;
using HR.Modules.Platform.Services.Reports;
using MediatR;

namespace HR.Modules.Platform.Queries.Reports;

public record ExportReportQuery(Guid Id, string Format) : IRequest<ReportExportFile>;

public class ExportReportQueryHandler : IRequestHandler<ExportReportQuery, ReportExportFile>
{
    private readonly IReportExportService _export;
    public ExportReportQueryHandler(IReportExportService export) => _export = export;

    public Task<ReportExportFile> Handle(ExportReportQuery request, CancellationToken ct)
    {
        if (!Enum.TryParse<ExportFormat>(request.Format, ignoreCase: true, out var fmt))
            throw new ValidationException(new[] { new FluentValidation.Results.ValidationFailure("format", $"Unknown export format '{request.Format}'. Use excel, csv, or pdf.") });
        return _export.ExportAsync(request.Id, fmt, ct);
    }
}
