using Amazon.S3;
using HR.Application.Common.Interfaces;
using HR.Infrastructure.Persistence;
using HR.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace HR.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // PostgreSQL + EF Core
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection"),
                b => b.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName)));

        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());

        // Dapper
        services.AddSingleton(new DapperContext(configuration.GetConnectionString("DefaultConnection")!));

        // Redis
        var redisConnection = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrEmpty(redisConnection))
        {
            // Connect lazily on first use — avoids failing host startup (and EF design-time tooling)
            // when Redis isn't running.
            services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnection));
            services.AddScoped<ICacheService, RedisCacheService>();
        }

        // Cloudflare R2 (S3-compatible)
        var r2Config = configuration.GetSection("R2");
        if (r2Config.Exists())
        {
            services.AddSingleton<IAmazonS3>(sp =>
            {
                var config = new AmazonS3Config
                {
                    ServiceURL = r2Config["ServiceUrl"],
                    ForcePathStyle = true
                };
                return new AmazonS3Client(r2Config["AccessKey"], r2Config["SecretKey"], config);
            });
            services.AddScoped<IFileStorageService>(sp =>
                new R2FileStorageService(sp.GetRequiredService<IAmazonS3>(), r2Config["BucketName"]!));
        }

        // Audit
        services.AddScoped<IAuditLogService, AuditLogService>();

        // Unified permission resolver (roles ∪ templates ∪ allow − deny; deny wins) → feeds the JWT.
        services.AddScoped<HR.Application.Engines.Permissions.IPermissionResolver, HR.Infrastructure.Engines.Permissions.PermissionResolver>();
        services.AddScoped<HR.Application.Engines.Permissions.IAccessTemplateSeeder, HR.Infrastructure.Engines.Permissions.AccessTemplateSeeder>();

        // Financial Calculation Engine
        // Scope engine (pluggable dimension providers). Payroll depends only on IScopeEngine.
        services.AddScoped<HR.Application.Engines.Scope.IScopeEngine, HR.Infrastructure.Engines.Scope.ScopeEngine>();
        services.AddScoped<HR.Application.Engines.Finance.IPayrollTypeService, HR.Infrastructure.Engines.Finance.PayrollTypeService>();
        services.AddScoped<HR.Application.Engines.Finance.IPayrollTransactionService, HR.Infrastructure.Engines.Finance.PayrollTransactionService>();
        services.AddScoped<HR.Application.Engines.Finance.IPayrollTransactionConsumer, HR.Infrastructure.Engines.Finance.PayrollTransactionConsumer>();
        services.AddScoped<HR.Application.Engines.Finance.IPayrollTransactionReversalService, HR.Infrastructure.Engines.Finance.PayrollTransactionReversalService>();
        services.AddScoped<HR.Application.Engines.Finance.IFinancialLedger, HR.Infrastructure.Engines.Finance.FinancialLedger>();
        services.AddScoped<HR.Application.Engines.Finance.IRuleEngine, HR.Infrastructure.Engines.Finance.RuleEngine>();
        services.AddScoped<HR.Application.Engines.Finance.IPayrollFactProvider, HR.Infrastructure.Engines.Finance.PayrollFactProvider>();
        services.AddScoped<HR.Application.Engines.Finance.IStandardPayrollSeeder, HR.Infrastructure.Engines.Finance.StandardPayrollSeeder>();
        services.AddScoped<HR.Infrastructure.Engines.Finance.AttendanceWageCalculator>();
        services.AddScoped<HR.Application.Engines.Finance.IAttendancePayrollSyncService, HR.Infrastructure.Engines.Finance.AttendancePayrollSyncService>();
        services.AddScoped<HR.Infrastructure.Engines.Finance.PayrollComputation>();
        services.AddScoped<HR.Application.Engines.Finance.IPayrollPreviewEngine, HR.Infrastructure.Engines.Finance.PayrollPreviewEngine>();
        services.AddScoped<HR.Application.Engines.Finance.IPayrollRunEngine, HR.Infrastructure.Engines.Finance.PayrollRunEngine>();
        services.AddScoped<HR.Application.Engines.Finance.IPayrollRunAmendmentService, HR.Infrastructure.Engines.Finance.PayrollRunAmendmentService>();
        services.AddScoped<HR.Application.Engines.Finance.IPayrollAuditReadService, HR.Infrastructure.Engines.Finance.PayrollAuditReadService>();
        services.AddScoped<HR.Application.Engines.Finance.IPayrollPeriodGuard, HR.Infrastructure.Engines.Finance.PayrollPeriodGuard>();
        services.AddScoped<HR.Application.Engines.Finance.IPayrollRunStalenessEvaluator, HR.Infrastructure.Engines.Finance.PayrollRunStalenessEvaluator>();
        services.AddScoped<HR.Application.Engines.Finance.IPayrollRunReadService, HR.Infrastructure.Engines.Finance.PayrollRunReadService>();
        services.AddScoped<HR.Application.Engines.Finance.ICreateFromRunService, HR.Infrastructure.Engines.Finance.CreateFromRunService>();

        // Domain events (in-process MediatR transport) + ambient background tenant context
        services.AddSingleton<HR.Application.Common.Interfaces.IBackgroundExecutionContext, HR.Infrastructure.Services.BackgroundExecutionContext>();
        services.AddScoped<HR.Application.Common.Interfaces.IDomainEventPublisher, HR.Infrastructure.Services.DomainEventPublisher>();
        services.AddScoped<MediatR.INotificationHandler<HR.Application.Engines.Finance.Events.PayrollApprovedEvent>, HR.Infrastructure.Engines.Finance.PayrollEventLogHandler>();
        services.AddScoped<MediatR.INotificationHandler<HR.Application.Engines.Finance.Events.PayrollExecutionStartedEvent>, HR.Infrastructure.Engines.Finance.PayrollEventLogHandler>();
        services.AddScoped<MediatR.INotificationHandler<HR.Application.Engines.Finance.Events.PayrollCompletedEvent>, HR.Infrastructure.Engines.Finance.PayrollEventLogHandler>();
        services.AddScoped<MediatR.INotificationHandler<HR.Application.Engines.Finance.Events.PayrollExecutionFailedEvent>, HR.Infrastructure.Engines.Finance.PayrollEventLogHandler>();

        // Batch execution orchestrator (concurrent, resumable, idempotent) + in-process scheduler default.
        // Set "Hangfire:Enabled"=true (and wire AddHangfire in the host) to offload to durable workers.
        services.AddScoped<HR.Infrastructure.Engines.Finance.PayrollItemExecutor>();
        services.AddScoped<HR.Application.Engines.Finance.IPayrollExecutionEngine, HR.Infrastructure.Engines.Finance.PayrollExecutionEngine>();
        if (configuration.GetValue<bool>("Hangfire:Enabled"))
            services.AddScoped<HR.Application.Engines.Finance.IPayrollExecutionScheduler, HR.Infrastructure.Engines.Finance.Scheduling.HangfirePayrollExecutionScheduler>();
        else
            services.AddScoped<HR.Application.Engines.Finance.IPayrollExecutionScheduler, HR.Infrastructure.Engines.Finance.Scheduling.InProcessPayrollExecutionScheduler>();

        // Payroll validation engine + validators (specification pattern; add a class to add a check)
        services.AddScoped<HR.Application.Engines.Finance.IPayrollValidationEngine, HR.Infrastructure.Engines.Finance.PayrollValidationEngine>();
        services.AddScoped<HR.Application.Engines.Finance.IPayrollValidator, HR.Infrastructure.Engines.Finance.Validators.NegativeSalaryValidator>();
        services.AddScoped<HR.Application.Engines.Finance.IPayrollValidator, HR.Infrastructure.Engines.Finance.Validators.InvalidGosiValidator>();
        services.AddScoped<HR.Application.Engines.Finance.IPayrollValidator, HR.Infrastructure.Engines.Finance.Validators.DuplicateEmployeeValidator>();
        services.AddScoped<HR.Application.Engines.Finance.IPayrollValidator, HR.Infrastructure.Engines.Finance.Validators.OverlappingPayrollValidator>();
        services.AddScoped<HR.Application.Engines.Finance.IPayrollValidator, HR.Infrastructure.Engines.Finance.Validators.CurrencyValidator>();
        services.AddScoped<HR.Application.Engines.Finance.IPayrollValidator, HR.Infrastructure.Engines.Finance.Validators.MissingAttendanceValidator>();
        services.AddScoped<HR.Application.Engines.Finance.IPayrollValidator, HR.Infrastructure.Engines.Finance.Validators.RuleConflictValidator>();
        // SP9 — smart validation (duplicate / conflict / consistency)
        services.AddScoped<HR.Application.Engines.Finance.IPayrollValidator, HR.Infrastructure.Engines.Finance.Validators.DuplicateTransactionValidator>();
        services.AddScoped<HR.Application.Engines.Finance.IPayrollValidator, HR.Infrastructure.Engines.Finance.Validators.ExcessiveDeductionValidator>();
        services.AddScoped<HR.Application.Engines.Finance.IPayrollValidator, HR.Infrastructure.Engines.Finance.Validators.ZeroGrossValidator>();

        // Email transport: ACS when configured, else a no-op that leaves rows Pending.
        services.Configure<HR.Application.Engines.Notifications.EmailOptions>(
            configuration.GetSection(HR.Application.Engines.Notifications.EmailOptions.SectionName));
        var acsConn = configuration.GetConnectionString("AcsEmail");
        var senderAddr = configuration[$"{HR.Application.Engines.Notifications.EmailOptions.SectionName}:SenderAddress"];
        if (!string.IsNullOrWhiteSpace(acsConn) && !string.IsNullOrWhiteSpace(senderAddr))
            services.AddSingleton<HR.Application.Engines.Notifications.IEmailSender>(sp =>
                new HR.Infrastructure.Engines.Notifications.AcsEmailSender(
                    acsConn,
                    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<HR.Application.Engines.Notifications.EmailOptions>>(),
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<HR.Infrastructure.Engines.Notifications.AcsEmailSender>>()));
        else
            services.AddSingleton<HR.Application.Engines.Notifications.IEmailSender,
                HR.Infrastructure.Engines.Notifications.NullEmailSender>();

        return services;
    }
}
