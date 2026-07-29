using System.Reflection;
using HR.Application.Engines.Audit;
using HR.Application.Engines.Completion;
using HR.Application.Engines.Automation;
using HR.Application.Engines.Permissions;
using HR.Application.Engines.Settlement;
using HR.Application.Engines.Timeline;
using HR.Application.Engines.Tokens;
using HR.Application.Engines.Workflows;
using HR.Application.Engines.Leave;
using HR.Infrastructure.Engines.Audit;
using HR.Infrastructure.Engines.Automation;
using HR.Infrastructure.Engines.Permissions;
using HR.Infrastructure.Engines.Settlement;
using HR.Infrastructure.Engines.Timeline;
using HR.Infrastructure.Engines.Tokens;
using HR.Infrastructure.Engines.Workflows;
using HR.Infrastructure.Engines.Leave;
using HR.Modules.Platform.Services;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.Platform;

public static class DependencyInjection
{
    public static IServiceCollection AddPlatformModule(this IServiceCollection services)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly()));
        services.AddAutoMapper(Assembly.GetExecutingAssembly());

        // Engine services
        services.AddScoped<IPermissionEvaluator, PermissionEvaluator>();
        services.AddScoped<IWorkflowEngine, WorkflowEngine>();
        services.AddScoped<IAutomationEngine, AutomationEngine>();
        services.AddScoped<IAuditEngine, AuditEngine>();
        services.AddScoped<ITimelineEngine, TimelineEngine>();
        services.AddScoped<ITimelineProjectionService, TimelineProjectionService>();
        services.AddScoped<ITokenResolver, TokenResolver>();
        services.AddScoped<IEndOfServiceEngine, EndOfServiceEngine>();
        services.AddScoped<ILeaveAccrualEngine, LeaveAccrualEngine>();
        services.AddScoped<HR.Application.Engines.Settlement.ITerminationWorkflow, HR.Modules.Platform.Services.Settlement.TerminationWorkflow>();
        services.AddScoped<HR.Application.Engines.Settlement.IRestoreWorkflow, HR.Modules.Platform.Services.Settlement.RestoreWorkflow>();

        // Master Data Engine services
        services.AddScoped<ILookupService, LookupService>();
        services.AddScoped<IUsageTrackingService, UsageTrackingService>();

        // Reports Engine — object resolver + execution pipeline
        services.AddScoped<HR.Modules.Platform.Services.Reports.IReportObjectResolver,
            HR.Modules.Platform.Services.Reports.ReportObjectResolver>();
        services.AddScoped<HR.Modules.Platform.Services.Reports.IReportExecutionService,
            HR.Modules.Platform.Services.Reports.ReportExecutionService>();
        services.AddScoped<HR.Modules.Platform.Services.Reports.IReportAccessService,
            HR.Modules.Platform.Services.Reports.ReportAccessService>();
        services.AddSingleton<HR.Application.Engines.Finance.Export.IExportWriter,
            HR.Modules.Platform.Services.Reports.PdfExportWriter>();
        services.AddScoped<HR.Modules.Platform.Services.Reports.IReportExportService,
            HR.Modules.Platform.Services.Reports.ReportExportService>();
        services.AddScoped<HR.Modules.Platform.Services.Reports.IReportSeeder,
            HR.Modules.Platform.Services.Reports.ReportSeeder>();
        services.AddScoped<HR.Modules.Platform.Services.Reports.IReportOwnerBackfill,
            HR.Modules.Platform.Services.Reports.ReportOwnerBackfill>();

        // Dashboard Platform — object-driven discovery + aggregation + seeding
        services.AddScoped<HR.Modules.Platform.Services.Catalog.IObjectCatalogService,
            HR.Modules.Platform.Services.Catalog.ObjectCatalogService>();
        services.AddScoped<HR.Modules.Platform.Services.WidgetData.IWidgetDataService,
            HR.Modules.Platform.Services.WidgetData.WidgetDataService>();
        services.AddScoped<HR.Modules.Platform.Services.WidgetData.IWidgetSuggestionService,
            HR.Modules.Platform.Services.WidgetData.WidgetSuggestionService>();
        services.AddScoped<HR.Modules.Platform.Services.WidgetData.IWidgetExportService,
            HR.Modules.Platform.Services.WidgetData.WidgetExportService>();
        services.AddScoped<HR.Modules.Platform.Services.WidgetData.IMetricWidgetService,
            HR.Modules.Platform.Services.WidgetData.MetricWidgetService>();
        services.AddScoped<HR.Modules.Platform.Services.Dashboards.IDashboardSeeder,
            HR.Modules.Platform.Services.Dashboards.DashboardSeeder>();

        // Report schedule runner (background-safe export → stored file → email link)
        services.AddScoped<HR.Modules.Platform.Services.Reports.IReportScheduleRunner,
            HR.Modules.Platform.Services.Reports.ReportScheduleRunner>();

        // Report Field Registry — permission-aware, code-defined field/subject catalog
        services.AddScoped<HR.Application.Reports.Registry.IReportObjectIdResolver,
            HR.Modules.Platform.Services.Reports.ReportObjectIdResolver>();
        services.AddScoped<HR.Application.Reports.Registry.IReportFieldRegistry,
            HR.Modules.Platform.Services.Reports.ReportFieldRegistryAdapter>();

        // Semantic Catalog — permission-aware, code-defined object/metric catalog
        services.AddScoped<HR.Application.SemanticCatalog.ISemanticCatalogProvider,
            HR.Modules.Platform.Services.SemanticCatalog.CodeDefinedSemanticCatalog>();

        // Notification engine (bell + email queue) + document-expiry rule scanner
        services.AddScoped<HR.Modules.Platform.Services.Notifications.INotificationService,
            HR.Modules.Platform.Services.Notifications.NotificationService>();
        services.AddScoped<HR.Modules.Platform.Services.Notifications.INotificationRecipientResolver,
            HR.Modules.Platform.Services.Notifications.NotificationRecipientResolver>();
        services.AddScoped<HR.Modules.Platform.Services.Notifications.IDocumentExpiryScanner,
            HR.Modules.Platform.Services.Notifications.DocumentExpiryScanner>();
        services.AddScoped<HR.Modules.Platform.Services.Notifications.IEmailQueueDrainer,
            HR.Modules.Platform.Services.Notifications.EmailQueueDrainer>();
        services.AddScoped<HR.Modules.Platform.Services.Notifications.IWorkflowNotificationDispatcher,
            HR.Modules.Platform.Services.Notifications.WorkflowNotificationDispatcher>();

        // Completion Effects Engine — generic orchestrator + flags→intents factory + executor
        // registry. Executors are auto-discovered from this assembly (Leave/Expense/Loan executors
        // also live here today); other modules register their own.
        services.AddScoped<IEffectExecutorRegistry, EffectExecutorRegistry>();
        // The trusted vocabulary the request builder may configure, plus the validator that keeps a
        // request type from being activated into a state that would fail at approval time.
        services.AddSingleton<HR.Application.Engines.Completion.IEffectActionCatalog,
            HR.Modules.Platform.Services.Completion.EffectActionCatalog>();
        services.AddScoped<HR.Modules.Platform.Services.Completion.IEffectConfigurationValidator,
            HR.Modules.Platform.Services.Completion.EffectConfigurationValidator>();
        services.AddScoped<ICompletionEngine, HR.Modules.Platform.Services.Completion.CompletionEngine>();
        services.AddScoped<ICompletionEffectFactory, HR.Modules.Platform.Services.Completion.CompletionEffectFactory>();
        services.AddScoped<HR.Application.Engines.Completion.IScheduledEffectDrainer,
            HR.Modules.Platform.Services.Completion.ScheduledEffectDrainer>();
        services.AddScoped<HR.Modules.Platform.Services.Completion.IScheduledEffectRecoveryService,
            HR.Modules.Platform.Services.Completion.ScheduledEffectRecoveryService>();
        services.AddEffectExecutorsFromAssembly(Assembly.GetExecutingAssembly());

        // Request Center engine + system-request seeder
        services.AddScoped<HR.Modules.Platform.Services.Requests.ILeaveService,
            HR.Modules.Platform.Services.Requests.LeaveService>();
        services.AddScoped<HR.Modules.Platform.Services.Requests.IRequestEngine,
            HR.Modules.Platform.Services.Requests.RequestEngine>();
        services.AddScoped<HR.Modules.Platform.Services.Requests.IRequestSeeder,
            HR.Modules.Platform.Services.Requests.RequestSeeder>();
        services.AddScoped<HR.Modules.Platform.Services.Requests.IRequestTypeAdminService,
            HR.Modules.Platform.Services.Requests.RequestTypeAdminService>();
        services.AddScoped<HR.Modules.Platform.Services.Requests.IRequestEffectDefinitionService,
            HR.Modules.Platform.Services.Requests.RequestEffectDefinitionService>();
        services.AddScoped<HR.Modules.Platform.Services.Assets.IAssetLookupService,
            HR.Modules.Platform.Services.Assets.AssetLookupService>();
        services.AddScoped<HR.Modules.Platform.Services.Requests.IRequestProvisioningService,
            HR.Modules.Platform.Services.Requests.RequestProvisioningService>();
        // Registered as a hook so Identity can provision a new tenant without referencing Platform.
        services.AddScoped<HR.Application.Common.Interfaces.ITenantOnboardingHook,
            HR.Modules.Platform.Services.Requests.RequestProvisioningOnboardingHook>();

        // HR-managed leave records engine
        services.AddScoped<HR.Modules.Platform.Services.Leaves.ILeaveRecordService,
            HR.Modules.Platform.Services.Leaves.LeaveRecordService>();

        // Official document rendering (QuestPDF) + token resolution + mapping-driven generation
        services.AddScoped<HR.Modules.Platform.Services.Documents.DocumentTokenResolver>();
        services.AddScoped<HR.Modules.Platform.Services.Documents.IDocumentTokenResolver>(
            sp => sp.GetRequiredService<HR.Modules.Platform.Services.Documents.DocumentTokenResolver>());
        services.AddScoped<HR.Modules.Platform.Services.Documents.IRequestTokenResolver>(
            sp => sp.GetRequiredService<HR.Modules.Platform.Services.Documents.DocumentTokenResolver>());
        services.AddScoped<HR.Modules.Platform.Services.Documents.IDocumentRenderer,
            HR.Modules.Platform.Services.Documents.DocumentRenderer>();
        services.AddScoped<HR.Modules.Platform.Services.Documents.IDocumentGenerationService,
            HR.Modules.Platform.Services.Documents.DocumentGenerationService>();
        // Payslip rendering + immutable byte-archiving (SP4). Interface lives in HR.Application so the
        // Payroll module depends on the abstraction, not on Platform.
        services.AddScoped<HR.Application.Engines.Finance.IPayslipDocumentService,
            HR.Modules.Platform.Services.Documents.PayslipDocumentService>();
        // Printable loan/expense documents. Interfaces live in HR.Application so the Loans/Expenses
        // modules depend on the abstraction, not on Platform (same pattern as the payslip service).
        services.AddScoped<HR.Application.Engines.Documents.ILoanDocumentService,
            HR.Modules.Platform.Services.Documents.LoanDocumentService>();
        services.AddScoped<HR.Application.Engines.Documents.IExpenseDocumentService,
            HR.Modules.Platform.Services.Documents.ExpenseDocumentService>();
        services.AddScoped<HR.Modules.Platform.Services.Documents.IPageTemplateSeeder,
            HR.Modules.Platform.Services.Documents.PageTemplateSeeder>();
        services.AddScoped<HR.Modules.Platform.Services.Documents.IDocumentLibrarySeeder,
            HR.Modules.Platform.Services.Documents.DocumentLibrarySeeder>();

        return services;
    }
}
