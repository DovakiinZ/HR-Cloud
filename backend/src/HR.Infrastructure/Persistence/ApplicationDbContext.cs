using HR.Application.Common.Interfaces;
using HR.Domain.Common;
using HR.Domain.Engines.Audit;
using HR.Domain.Engines.Automation;
using HR.Domain.Engines.CompanyConfig;
using HR.Domain.Engines.Dashboards;
using HR.Domain.Engines.Documents;
using HR.Domain.Engines.Files;
using HR.Domain.Engines.Forms;
using HR.Domain.Engines.MasterData;
using HR.Domain.Engines.Metadata;
using HR.Domain.Engines.ObjectRegistry;
using HR.Domain.Engines.OrgGraph;
using HR.Domain.Engines.Permissions;
using HR.Domain.Engines.Reports;
using HR.Domain.Engines.Timeline;
using HR.Domain.Engines.Tokens;
using HR.Domain.Engines.Workflows;
using HR.Modules.Core.Entities;
using HR.Modules.Employees.Entities;
using HR.Modules.Identity.Entities;
using HR.Modules.Settings.Entities;
using HR.Modules.Tasks.Entities;
using HR.Modules.Tenancy.Entities;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace HR.Infrastructure.Persistence;

public class ApplicationDbContext : DbContext, IApplicationDbContext
{
    private readonly ICurrentUserService _currentUser;

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, ICurrentUserService currentUser)
        : base(options)
    {
        _currentUser = currentUser;
    }

    // Tenancy
    public DbSet<Tenant> Tenants => Set<Tenant>();

    // Identity
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserPermission> UserPermissions => Set<UserPermission>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    // Core
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Branch> Branch => Set<Branch>();

    // Employees
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<EmployeeAllowance> EmployeeAllowances => Set<EmployeeAllowance>();
    public DbSet<EmployeeAddition> EmployeeAdditions => Set<EmployeeAddition>();
    public DbSet<EmployeeDeduction> EmployeeDeductions => Set<EmployeeDeduction>();
    public DbSet<TerminationSettlement> TerminationSettlements => Set<TerminationSettlement>();
    public DbSet<TerminationSettlementItem> TerminationSettlementItems => Set<TerminationSettlementItem>();
    public DbSet<TerminationApprovalStep> TerminationApprovalSteps => Set<TerminationApprovalStep>();
    public DbSet<EmployeeRestoreRequest> EmployeeRestoreRequests => Set<EmployeeRestoreRequest>();
    public DbSet<RestoreApprovalStep> RestoreApprovalSteps => Set<RestoreApprovalStep>();

    // Tasks
    public DbSet<HrTask> HrTasks => Set<HrTask>();
    public DbSet<HrTaskChecklist> HrTaskChecklists => Set<HrTaskChecklist>();
    public DbSet<HrTaskComment> HrTaskComments => Set<HrTaskComment>();
    public DbSet<HrTaskActivity> HrTaskActivities => Set<HrTaskActivity>();

    // Settings
    public DbSet<CompanySettings> CompanySettings => Set<CompanySettings>();

    // Audit
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    // Files (DB-backed binary store: employee photos, attachments)
    public DbSet<StoredFile> Files => Set<StoredFile>();

    // ===== Engine DbSets =====

    // Master Data Engine (generic tenant-scoped reusable objects)
    public DbSet<MasterDataItem> MasterDataItems => Set<MasterDataItem>();

    // Metadata Engine
    public DbSet<MetadataDefinition> MetadataDefinitions => Set<MetadataDefinition>();
    public DbSet<MetadataField> MetadataFields => Set<MetadataField>();
    public DbSet<MetadataOption> MetadataOptions => Set<MetadataOption>();
    public DbSet<MetadataValue> MetadataValues => Set<MetadataValue>();

    // Object Registry Engine
    public DbSet<ObjectDefinition> ObjectDefinitions => Set<ObjectDefinition>();
    public DbSet<ObjectField> ObjectFields => Set<ObjectField>();
    public DbSet<ObjectRelationship> ObjectRelationships => Set<ObjectRelationship>();
    public DbSet<ObjectPermission> ObjectPermissions => Set<ObjectPermission>();

    // Permission Engine
    public DbSet<PermissionTemplate> PermissionTemplates => Set<PermissionTemplate>();
    public DbSet<PermissionTemplateItem> PermissionTemplateItems => Set<PermissionTemplateItem>();
    public DbSet<UserPermissionOverride> UserPermissionOverrides => Set<UserPermissionOverride>();
    public DbSet<UserPermissionScope> UserPermissionScopes => Set<UserPermissionScope>();
    public DbSet<UserPermissionTemplate> UserPermissionTemplates => Set<UserPermissionTemplate>();

    // Forms Engine
    public DbSet<FormDefinition> FormDefinitions => Set<FormDefinition>();
    public DbSet<FormField> FormFields => Set<FormField>();
    public DbSet<FormSubmission> FormSubmissions => Set<FormSubmission>();
    public DbSet<FormSubmissionValue> FormSubmissionValues => Set<FormSubmissionValue>();

    // Workflow Engine
    public DbSet<WorkflowDefinition> WorkflowDefinitions => Set<WorkflowDefinition>();
    public DbSet<WorkflowVersion> WorkflowVersions => Set<WorkflowVersion>();
    public DbSet<WorkflowNode> WorkflowNodes => Set<WorkflowNode>();
    public DbSet<WorkflowEdge> WorkflowEdges => Set<WorkflowEdge>();
    public DbSet<WorkflowCondition> WorkflowConditions => Set<WorkflowCondition>();
    public DbSet<WorkflowApproverRule> WorkflowApproverRules => Set<WorkflowApproverRule>();
    public DbSet<WorkflowInstance> WorkflowInstances => Set<WorkflowInstance>();
    public DbSet<WorkflowInstanceStep> WorkflowInstanceSteps => Set<WorkflowInstanceStep>();

    // Automation Engine
    public DbSet<AutomationRule> AutomationRules => Set<AutomationRule>();
    public DbSet<AutomationTrigger> AutomationTriggers => Set<AutomationTrigger>();
    public DbSet<AutomationCondition> AutomationConditions => Set<AutomationCondition>();
    public DbSet<AutomationAction> AutomationActions => Set<AutomationAction>();
    public DbSet<AutomationExecutionLog> AutomationExecutionLogs => Set<AutomationExecutionLog>();

    // Audit Engine
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<AuditConfiguration> AuditConfigurations => Set<AuditConfiguration>();

    // Timeline Engine
    public DbSet<TimelineEvent> TimelineEvents => Set<TimelineEvent>();
    public DbSet<TimelineSubscription> TimelineSubscriptions => Set<TimelineSubscription>();

    // Completion Effects Engine
    public DbSet<HR.Domain.Engines.Completion.CompletionRun> CompletionRuns => Set<HR.Domain.Engines.Completion.CompletionRun>();
    public DbSet<HR.Domain.Engines.Completion.CompletionEffect> CompletionEffects => Set<HR.Domain.Engines.Completion.CompletionEffect>();
    public DbSet<HR.Domain.Engines.Completion.EffectAttempt> EffectAttempts => Set<HR.Domain.Engines.Completion.EffectAttempt>();

    // Token Engine
    public DbSet<TokenDefinition> TokenDefinitions => Set<TokenDefinition>();
    public DbSet<TokenCategory> TokenCategories => Set<TokenCategory>();

    // Dashboard Engine
    public DbSet<DashboardDefinition> DashboardDefinitions => Set<DashboardDefinition>();
    public DbSet<DashboardCategory> DashboardCategories => Set<DashboardCategory>();
    public DbSet<DashboardTemplate> DashboardTemplates => Set<DashboardTemplate>();
    public DbSet<DashboardShare> DashboardShares => Set<DashboardShare>();
    public DbSet<DashboardWidget> DashboardWidgets => Set<DashboardWidget>();
    public DbSet<WidgetDefinition> WidgetDefinitions => Set<WidgetDefinition>();
    public DbSet<WidgetDataSource> WidgetDataSources => Set<WidgetDataSource>();
    public DbSet<WidgetDrilldown> WidgetDrilldowns => Set<WidgetDrilldown>();
    public DbSet<WidgetPermission> WidgetPermissions => Set<WidgetPermission>();
    public DbSet<WidgetFilter> WidgetFilters => Set<WidgetFilter>();
    public DbSet<WidgetLayout> WidgetLayouts => Set<WidgetLayout>();

    // Report Engine
    public DbSet<ReportDefinition> ReportDefinitions => Set<ReportDefinition>();
    public DbSet<ReportTemplate> ReportTemplates => Set<ReportTemplate>();
    public DbSet<ReportField> ReportFields => Set<ReportField>();
    public DbSet<ReportRelationship> ReportRelationships => Set<ReportRelationship>();
    public DbSet<ReportFilter> ReportFilters => Set<ReportFilter>();
    public DbSet<ReportGrouping> ReportGroupings => Set<ReportGrouping>();
    public DbSet<ReportSorting> ReportSortings => Set<ReportSorting>();
    public DbSet<ReportSchedule> ReportSchedules => Set<ReportSchedule>();
    public DbSet<ReportShare> ReportShares => Set<ReportShare>();
    public DbSet<ReportFolder> ReportFolders => Set<ReportFolder>();
    public DbSet<ReportTag> ReportTags => Set<ReportTag>();
    public DbSet<ReportDefinitionTag> ReportDefinitionTags => Set<ReportDefinitionTag>();
    public DbSet<ReportUserState> ReportUserStates => Set<ReportUserState>();

    // Document Engine
    public DbSet<DocumentTemplate> DocumentTemplates => Set<DocumentTemplate>();
    public DbSet<DocumentTemplateVersion> DocumentTemplateVersions => Set<DocumentTemplateVersion>();
    public DbSet<DocumentTemplateToken> DocumentTemplateTokens => Set<DocumentTemplateToken>();
    public DbSet<GeneratedDocument> GeneratedDocuments => Set<GeneratedDocument>();
    public DbSet<CompanyBranding> CompanyBrandings => Set<CompanyBranding>();
    public DbSet<DocumentWorkflowLink> DocumentWorkflowLinks => Set<DocumentWorkflowLink>();
    public DbSet<PageTemplate> PageTemplates => Set<PageTemplate>();

    // Organization Graph Engine
    public DbSet<OrgNode> OrgNodes => Set<OrgNode>();
    public DbSet<OrgEdge> OrgEdges => Set<OrgEdge>();
    public DbSet<OrgGraphLayout> OrgGraphLayouts => Set<OrgGraphLayout>();
    public DbSet<EmployeeReportingLine> EmployeeReportingLines => Set<EmployeeReportingLine>();

    // Workflow Enhancement
    public DbSet<WorkflowDynamicApprover> WorkflowDynamicApprovers => Set<WorkflowDynamicApprover>();
    public DbSet<WorkflowDynamicCondition> WorkflowDynamicConditions => Set<WorkflowDynamicCondition>();
    public DbSet<WorkflowAction> WorkflowActions => Set<WorkflowAction>();
    public DbSet<WorkflowSimulation> WorkflowSimulations => Set<WorkflowSimulation>();

    // Flow Builder Engine (decoupled linked-list approval workflows: definition -> steps -> requests)
    public DbSet<HR.Domain.Engines.FlowBuilder.WorkflowDefinition> FlowDefinitions => Set<HR.Domain.Engines.FlowBuilder.WorkflowDefinition>();
    public DbSet<HR.Domain.Engines.FlowBuilder.WorkflowStep> FlowSteps => Set<HR.Domain.Engines.FlowBuilder.WorkflowStep>();
    public DbSet<HR.Domain.Engines.FlowBuilder.WorkflowRequest> FlowRequests => Set<HR.Domain.Engines.FlowBuilder.WorkflowRequest>();
    public DbSet<HR.Domain.Engines.FlowBuilder.WorkflowAuditTrail> FlowAuditTrail => Set<HR.Domain.Engines.FlowBuilder.WorkflowAuditTrail>();

    // Company Configuration Engine
    public DbSet<CompanyProfile> CompanyProfiles => Set<CompanyProfile>();
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<Grade> Grades => Set<Grade>();
    public DbSet<CostCenter> CostCenters => Set<CostCenter>();
    public DbSet<CalendarSetting> CalendarSettings => Set<CalendarSetting>();
    public DbSet<FiscalPeriod> FiscalPeriods => Set<FiscalPeriod>();

    // Request Center Engine
    public DbSet<HR.Domain.Engines.Requests.RequestType> RequestTypes => Set<HR.Domain.Engines.Requests.RequestType>();
    public DbSet<HR.Domain.Engines.Requests.RequestImpactMapping> RequestImpactMappings => Set<HR.Domain.Engines.Requests.RequestImpactMapping>();
    public DbSet<HR.Domain.Engines.Requests.RequestEffectDefinition> RequestEffectDefinitions => Set<HR.Domain.Engines.Requests.RequestEffectDefinition>();
    public DbSet<HR.Domain.Engines.Requests.RequestPermission> RequestPermissions => Set<HR.Domain.Engines.Requests.RequestPermission>();
    public DbSet<HR.Domain.Engines.Requests.RequestInstance> RequestInstances => Set<HR.Domain.Engines.Requests.RequestInstance>();
    public DbSet<HR.Domain.Engines.Requests.RequestApproval> RequestApprovals => Set<HR.Domain.Engines.Requests.RequestApproval>();
    public DbSet<HR.Domain.Engines.Requests.RequestStatusHistory> RequestStatusHistories => Set<HR.Domain.Engines.Requests.RequestStatusHistory>();
    public DbSet<HR.Domain.Engines.Requests.RequestTemplateMapping> RequestTemplateMappings => Set<HR.Domain.Engines.Requests.RequestTemplateMapping>();

    // Leave / Attendance / Notifications (request impact targets)
    public DbSet<HR.Domain.Engines.Leave.LeaveBalance> LeaveBalances => Set<HR.Domain.Engines.Leave.LeaveBalance>();

    // Leave records engine (HR-managed approved/assigned leave)
    public DbSet<HR.Domain.Engines.Leave.LeaveRecord> LeaveRecords => Set<HR.Domain.Engines.Leave.LeaveRecord>();
    public DbSet<HR.Domain.Engines.Leave.LeaveBalanceTransaction> LeaveBalanceTransactions => Set<HR.Domain.Engines.Leave.LeaveBalanceTransaction>();
    public DbSet<HR.Domain.Engines.Leave.LeaveAssignment> LeaveAssignments => Set<HR.Domain.Engines.Leave.LeaveAssignment>();
    public DbSet<HR.Domain.Engines.Leave.LeaveCancellation> LeaveCancellations => Set<HR.Domain.Engines.Leave.LeaveCancellation>();
    public DbSet<HR.Domain.Engines.Leave.LeaveAuditLog> LeaveAuditLogs => Set<HR.Domain.Engines.Leave.LeaveAuditLog>();
    public DbSet<HR.Domain.Engines.Attendance.AttendanceRecord> AttendanceRecords => Set<HR.Domain.Engines.Attendance.AttendanceRecord>();

    // Attendance & Shift engine
    public DbSet<HR.Domain.Engines.Attendance.Shift> Shifts => Set<HR.Domain.Engines.Attendance.Shift>();
    public DbSet<HR.Domain.Engines.Attendance.ShiftAssignment> ShiftAssignments => Set<HR.Domain.Engines.Attendance.ShiftAssignment>();
    public DbSet<HR.Domain.Engines.Attendance.AttendancePunch> AttendancePunches => Set<HR.Domain.Engines.Attendance.AttendancePunch>();
    public DbSet<HR.Domain.Engines.Attendance.AttendanceCorrection> AttendanceCorrections => Set<HR.Domain.Engines.Attendance.AttendanceCorrection>();
    public DbSet<HR.Domain.Engines.Attendance.AttendancePolicy> AttendancePolicies => Set<HR.Domain.Engines.Attendance.AttendancePolicy>();
    public DbSet<HR.Domain.Engines.Attendance.AttendanceHoliday> AttendanceHolidays => Set<HR.Domain.Engines.Attendance.AttendanceHoliday>();
    public DbSet<HR.Domain.Engines.Attendance.AttendanceAuditLog> AttendanceAuditLogs => Set<HR.Domain.Engines.Attendance.AttendanceAuditLog>();
    public DbSet<HR.Domain.Engines.Notifications.Notification> Notifications => Set<HR.Domain.Engines.Notifications.Notification>();
    public DbSet<HR.Domain.Engines.Notifications.EmailNotificationQueue> EmailQueue => Set<HR.Domain.Engines.Notifications.EmailNotificationQueue>();

    // Request impact targets — Expenses / Loans (fed by approved requests)
    public DbSet<HR.Domain.Engines.Expenses.Expense> Expenses => Set<HR.Domain.Engines.Expenses.Expense>();
    public DbSet<HR.Domain.Engines.Loans.Loan> Loans => Set<HR.Domain.Engines.Loans.Loan>();
    public DbSet<HR.Domain.Engines.Loans.LoanInstallment> LoanInstallments => Set<HR.Domain.Engines.Loans.LoanInstallment>();

    // Employee personnel documents (ID/Iqama/passport/contract/… with optional expiry)
    public DbSet<HR.Domain.Engines.Documents.EmployeeDocument> EmployeeDocuments => Set<HR.Domain.Engines.Documents.EmployeeDocument>();

    // Notification rules (admin-configured) + dedup ledger for rule-driven notifications
    public DbSet<HR.Domain.Engines.Notifications.NotificationRule> NotificationRules => Set<HR.Domain.Engines.Notifications.NotificationRule>();
    public DbSet<HR.Domain.Engines.Notifications.NotificationDispatch> NotificationDispatches => Set<HR.Domain.Engines.Notifications.NotificationDispatch>();

    // Workflow notification framework (SP1): configurable per-event rules + idempotent dispatch ledger
    public DbSet<HR.Domain.Engines.Notifications.WorkflowNotificationRule> WorkflowNotificationRules => Set<HR.Domain.Engines.Notifications.WorkflowNotificationRule>();
    public DbSet<HR.Domain.Engines.Notifications.WorkflowNotificationDispatch> WorkflowNotificationDispatches => Set<HR.Domain.Engines.Notifications.WorkflowNotificationDispatch>();

    // Financial Calculation Engine (immutable ledger + versioned payroll definitions + configurable rules)
    public DbSet<HR.Domain.Engines.Finance.Entities.FinancialLedgerEntry> FinancialLedgerEntries => Set<HR.Domain.Engines.Finance.Entities.FinancialLedgerEntry>();
    public DbSet<HR.Domain.Engines.Finance.Entities.PayrollDefinition> PayrollDefinitions => Set<HR.Domain.Engines.Finance.Entities.PayrollDefinition>();
    public DbSet<HR.Domain.Engines.Finance.Entities.PayrollDefinitionVersion> PayrollDefinitionVersions => Set<HR.Domain.Engines.Finance.Entities.PayrollDefinitionVersion>();
    public DbSet<HR.Domain.Engines.Finance.Entities.RuleSet> FinanceRuleSets => Set<HR.Domain.Engines.Finance.Entities.RuleSet>();
    public DbSet<HR.Domain.Engines.Finance.Entities.RuleSetVersion> FinanceRuleSetVersions => Set<HR.Domain.Engines.Finance.Entities.RuleSetVersion>();
    public DbSet<HR.Domain.Engines.Finance.Entities.Rule> FinanceRules => Set<HR.Domain.Engines.Finance.Entities.Rule>();
    public DbSet<HR.Domain.Engines.Finance.Entities.FormulaFunction> FinanceFormulaFunctions => Set<HR.Domain.Engines.Finance.Entities.FormulaFunction>();
    public DbSet<HR.Domain.Engines.Finance.Entities.PayrollRun> PayrollRuns => Set<HR.Domain.Engines.Finance.Entities.PayrollRun>();
    public DbSet<HR.Domain.Engines.Finance.Entities.PayrollRunTransition> PayrollRunTransitions => Set<HR.Domain.Engines.Finance.Entities.PayrollRunTransition>();
    public DbSet<HR.Domain.Engines.Finance.Entities.PayrollPayslip> PayrollPayslips => Set<HR.Domain.Engines.Finance.Entities.PayrollPayslip>();
    public DbSet<HR.Domain.Engines.Finance.Entities.PayrollRunItem> PayrollRunItems => Set<HR.Domain.Engines.Finance.Entities.PayrollRunItem>();
    public DbSet<HR.Domain.Engines.Finance.Entities.PayrollRunPopulation> PayrollRunPopulations => Set<HR.Domain.Engines.Finance.Entities.PayrollRunPopulation>();
    public DbSet<HR.Domain.Engines.Finance.Entities.PayrollTransaction> PayrollTransactions => Set<HR.Domain.Engines.Finance.Entities.PayrollTransaction>();
    public DbSet<HR.Domain.Engines.Finance.Entities.PayrollTransactionAttendanceReference> PayrollTransactionAttendanceReferences => Set<HR.Domain.Engines.Finance.Entities.PayrollTransactionAttendanceReference>();
    public DbSet<HR.Domain.Engines.Finance.Entities.PayrollRunCalculation> PayrollRunCalculations => Set<HR.Domain.Engines.Finance.Entities.PayrollRunCalculation>();
    public DbSet<HR.Domain.Engines.Finance.Entities.PayrollCalculationFinding> PayrollCalculationFindings => Set<HR.Domain.Engines.Finance.Entities.PayrollCalculationFinding>();
    public DbSet<HR.Domain.Engines.Finance.Entities.PayrollCalculationExclusion> PayrollCalculationExclusions => Set<HR.Domain.Engines.Finance.Entities.PayrollCalculationExclusion>();
    public DbSet<HR.Domain.Engines.Finance.Entities.PayrollExportJob> PayrollExportJobs => Set<HR.Domain.Engines.Finance.Entities.PayrollExportJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        // Seed permissions
        SeedData.SeedPermissions(modelBuilder);

        // Global query filters for tenant isolation and soft delete
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(TenantEntity).IsAssignableFrom(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType).HasQueryFilter(
                    CreateTenantFilter(entityType.ClrType));
            }
            else if (typeof(AuditableEntity).IsAssignableFrom(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType).HasQueryFilter(
                    CreateSoftDeleteFilter(entityType.ClrType));
            }
        }
    }

    private LambdaExpression CreateTenantFilter(Type entityType)
    {
        var parameter = System.Linq.Expressions.Expression.Parameter(entityType, "e");
        var tenantIdProp = System.Linq.Expressions.Expression.Property(parameter, nameof(TenantEntity.TenantId));
        // Read the current tenant id from the injected _currentUser field on this context instance.
        var currentUserField = System.Linq.Expressions.Expression.Field(
            System.Linq.Expressions.Expression.Constant(this), "_currentUser");
        var tenantIdValue = System.Linq.Expressions.Expression.Property(currentUserField, nameof(ICurrentUserService.TenantId));

        var isDeletedProp = System.Linq.Expressions.Expression.Property(parameter, nameof(AuditableEntity.IsDeleted));

        var tenantFilter = System.Linq.Expressions.Expression.Equal(tenantIdProp, tenantIdValue);
        var softDeleteFilter = System.Linq.Expressions.Expression.Equal(
            isDeletedProp, System.Linq.Expressions.Expression.Constant(false));
        var combined = System.Linq.Expressions.Expression.AndAlso(tenantFilter, softDeleteFilter);

        return System.Linq.Expressions.Expression.Lambda(combined, parameter);
    }

    private LambdaExpression CreateSoftDeleteFilter(Type entityType)
    {
        var parameter = System.Linq.Expressions.Expression.Parameter(entityType, "e");
        var isDeletedProp = System.Linq.Expressions.Expression.Property(parameter, nameof(AuditableEntity.IsDeleted));
        var filter = System.Linq.Expressions.Expression.Equal(
            isDeletedProp, System.Linq.Expressions.Expression.Constant(false));
        return System.Linq.Expressions.Expression.Lambda(filter, parameter);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = DateTime.UtcNow;
                    entry.Entity.CreatedBy = _currentUser.Email;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = DateTime.UtcNow;
                    entry.Entity.UpdatedBy = _currentUser.Email;
                    break;
            }

            if (entry.Entity is TenantEntity tenantEntity && entry.State == EntityState.Added)
            {
                if (tenantEntity.TenantId == Guid.Empty)
                    tenantEntity.TenantId = _currentUser.TenantId;
            }
        }

        return await base.SaveChangesAsync(cancellationToken);
    }
}
