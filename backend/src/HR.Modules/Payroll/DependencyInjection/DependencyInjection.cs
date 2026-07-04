using HR.Application.Engines.Finance.Export;
using HR.Application.Engines.Finance.Export.Bank;
using HR.Modules.Payroll.Export;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.Payroll;

public static class DependencyInjection
{
    public static IServiceCollection AddPayrollModule(this IServiceCollection services)
    {
        // SP5 export framework — everything DI-discovered so new writers/reports/bank profiles need no
        // change to the engine or the service.

        // Format writers (by ExportFormat) — stateless.
        services.AddSingleton<IExportWriter, CsvExportWriter>();
        services.AddSingleton<IExportWriter, TxtExportWriter>();
        services.AddSingleton<IExportWriter, XmlExportWriter>();
        services.AddSingleton<IExportWriter, ExcelExportWriter>();

        // Report providers (by Kind).
        services.AddScoped<IPayrollReportProvider, RunSummaryReportProvider>();
        services.AddScoped<IPayrollReportProvider, EmployeeDetailReportProvider>();

        // Bank pipeline — profiles + validators (stateless) + payment source.
        services.AddSingleton<IBankProfile, SaudiWpsSifProfile>();
        services.AddSingleton<IBankExportValidator, SaudiWpsSifValidator>();
        services.AddScoped<IBankPaymentSource, BankPaymentSource>();

        // Orchestrator.
        services.AddScoped<IPayrollExportService, PayrollExportService>();

        return services;
    }
}
