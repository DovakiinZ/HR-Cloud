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
        services.AddScoped<ITokenResolver, TokenResolver>();
        services.AddScoped<IEndOfServiceEngine, EndOfServiceEngine>();
        services.AddScoped<ILeaveAccrualEngine, LeaveAccrualEngine>();
        services.AddScoped<HR.Application.Engines.Settlement.ITerminationWorkflow, HR.Modules.Platform.Services.Settlement.TerminationWorkflow>();
        services.AddScoped<HR.Application.Engines.Settlement.IRestoreWorkflow, HR.Modules.Platform.Services.Settlement.RestoreWorkflow>();

        // Master Data Engine services
        services.AddScoped<ILookupService, LookupService>();
        services.AddScoped<IUsageTrackingService, UsageTrackingService>();

        // Dashboard Platform — object-driven discovery + aggregation + seeding
        services.AddScoped<HR.Modules.Platform.Services.Catalog.IObjectCatalogService,
            HR.Modules.Platform.Services.Catalog.ObjectCatalogService>();
        services.AddScoped<HR.Modules.Platform.Services.WidgetData.IWidgetDataService,
            HR.Modules.Platform.Services.WidgetData.WidgetDataService>();
        services.AddScoped<HR.Modules.Platform.Services.WidgetData.IWidgetSuggestionService,
            HR.Modules.Platform.Services.WidgetData.WidgetSuggestionService>();
        services.AddScoped<HR.Modules.Platform.Services.Dashboards.IDashboardSeeder,
            HR.Modules.Platform.Services.Dashboards.DashboardSeeder>();

        // Notification engine (bell + email queue) + document-expiry rule scanner
        services.AddScoped<HR.Modules.Platform.Services.Notifications.INotificationService,
            HR.Modules.Platform.Services.Notifications.NotificationService>();
        services.AddScoped<HR.Modules.Platform.Services.Notifications.IDocumentExpiryScanner,
            HR.Modules.Platform.Services.Notifications.DocumentExpiryScanner>();

        // Completion Effects Engine — generic orchestrator + flags→intents factory + executor
        // registry. Executors are auto-discovered from this assembly (Leave/Expense/Loan executors
        // also live here today); other modules register their own.
        services.AddScoped<IEffectExecutorRegistry, EffectExecutorRegistry>();
        services.AddScoped<ICompletionEngine, HR.Modules.Platform.Services.Completion.CompletionEngine>();
        services.AddScoped<ICompletionEffectFactory, HR.Modules.Platform.Services.Completion.CompletionEffectFactory>();
        services.AddEffectExecutorsFromAssembly(Assembly.GetExecutingAssembly());

        // Request Center engine + system-request seeder
        services.AddScoped<HR.Modules.Platform.Services.Requests.ILeaveService,
            HR.Modules.Platform.Services.Requests.LeaveService>();
        services.AddScoped<HR.Modules.Platform.Services.Requests.IRequestEngine,
            HR.Modules.Platform.Services.Requests.RequestEngine>();
        services.AddScoped<HR.Modules.Platform.Services.Requests.IRequestSeeder,
            HR.Modules.Platform.Services.Requests.RequestSeeder>();

        // HR-managed leave records engine
        services.AddScoped<HR.Modules.Platform.Services.Leaves.ILeaveRecordService,
            HR.Modules.Platform.Services.Leaves.LeaveRecordService>();

        // Official document rendering (QuestPDF) + token resolution + mapping-driven generation
        services.AddScoped<HR.Modules.Platform.Services.Documents.IDocumentTokenResolver,
            HR.Modules.Platform.Services.Documents.DocumentTokenResolver>();
        services.AddScoped<HR.Modules.Platform.Services.Documents.IDocumentRenderer,
            HR.Modules.Platform.Services.Documents.DocumentRenderer>();
        services.AddScoped<HR.Modules.Platform.Services.Documents.IDocumentGenerationService,
            HR.Modules.Platform.Services.Documents.DocumentGenerationService>();
        // Payslip rendering + immutable byte-archiving (SP4). Interface lives in HR.Application so the
        // Payroll module depends on the abstraction, not on Platform.
        services.AddScoped<HR.Application.Engines.Finance.IPayslipDocumentService,
            HR.Modules.Platform.Services.Documents.PayslipDocumentService>();
        services.AddScoped<HR.Modules.Platform.Services.Documents.IPageTemplateSeeder,
            HR.Modules.Platform.Services.Documents.PageTemplateSeeder>();
        services.AddScoped<HR.Modules.Platform.Services.Documents.IDocumentLibrarySeeder,
            HR.Modules.Platform.Services.Documents.DocumentLibrarySeeder>();

        return services;
    }
}
