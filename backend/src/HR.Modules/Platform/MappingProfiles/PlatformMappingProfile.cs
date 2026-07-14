using System.Text.Json;
using AutoMapper;
using HR.Domain.Engines.Audit;
using HR.Domain.Engines.Automation;
using HR.Domain.Engines.CompanyConfig;
using HR.Domain.Engines.Dashboards;
using HR.Domain.Engines.Documents;
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
using HR.Modules.Platform.DTOs.Audit;
using HR.Modules.Platform.DTOs.Automation;
using HR.Modules.Platform.DTOs.CompanyConfig;
using HR.Modules.Platform.DTOs.Dashboards;
using HR.Modules.Platform.DTOs.Documents;
using HR.Modules.Platform.DTOs.Forms;
using HR.Modules.Platform.DTOs.MasterData;
using HR.Modules.Platform.DTOs.Metadata;
using HR.Modules.Platform.DTOs.ObjectRegistry;
using HR.Modules.Platform.DTOs.OrgGraph;
using HR.Modules.Platform.DTOs.Permissions;
using HR.Modules.Platform.DTOs.Reports;
using HR.Modules.Platform.DTOs.Timeline;
using HR.Modules.Platform.DTOs.Tokens;
using HR.Modules.Platform.DTOs.Workflows;

namespace HR.Modules.Platform.MappingProfiles;

public class PlatformMappingProfile : Profile
{
    public PlatformMappingProfile()
    {
        // Metadata
        CreateMap<MetadataDefinition, MetadataDefinitionDto>();
        CreateMap<MetadataField, MetadataFieldDto>()
            .ForMember(d => d.FieldType, opt => opt.MapFrom(s => s.FieldType.ToString()));
        CreateMap<MetadataOption, MetadataOptionDto>();
        CreateMap<MetadataValue, MetadataValueDto>();

        // Object Registry
        CreateMap<ObjectDefinition, ObjectDefinitionDto>();
        CreateMap<ObjectField, ObjectFieldDto>()
            .ForMember(d => d.FieldType, opt => opt.MapFrom(s => s.FieldType.ToString()));
        CreateMap<ObjectRelationship, ObjectRelationshipDto>()
            .ForMember(d => d.RelationType, opt => opt.MapFrom(s => s.RelationType.ToString()));

        // Permissions
        CreateMap<PermissionTemplate, PermissionTemplateDto>();
        CreateMap<PermissionTemplateItem, PermissionTemplateItemDto>()
            .ForMember(d => d.Scope, opt => opt.MapFrom(s => s.Scope.ToString()));
        CreateMap<UserPermissionTemplate, UserPermissionTemplateDto>()
            .ForMember(d => d.TemplateNameEn, opt => opt.MapFrom(s => s.PermissionTemplate.NameEn))
            .ForMember(d => d.TemplateNameAr, opt => opt.MapFrom(s => s.PermissionTemplate.NameAr));

        // Forms
        CreateMap<FormDefinition, FormDefinitionDto>();
        CreateMap<FormField, FormFieldDto>()
            .ForMember(d => d.FieldType, opt => opt.MapFrom(s => s.FieldType.ToString()));
        CreateMap<FormSubmission, FormSubmissionDto>()
            .ForMember(d => d.Status, opt => opt.MapFrom(s => s.Status.ToString()));
        CreateMap<FormSubmissionValue, FormSubmissionValueDto>();

        // Workflows
        CreateMap<WorkflowDefinition, WorkflowDefinitionDto>();
        CreateMap<WorkflowVersion, WorkflowVersionDto>();
        CreateMap<WorkflowVersion, WorkflowVersionDetailDto>();
        CreateMap<WorkflowNode, WorkflowNodeDto>()
            .ForMember(d => d.NodeType, opt => opt.MapFrom(s => s.NodeType.ToString()));
        CreateMap<WorkflowEdge, WorkflowEdgeDto>();
        CreateMap<WorkflowCondition, WorkflowConditionDto>();
        CreateMap<WorkflowApproverRule, WorkflowApproverRuleDto>()
            .ForMember(d => d.ApproverType, opt => opt.MapFrom(s => s.ApproverType.ToString()));
        CreateMap<WorkflowInstance, WorkflowInstanceDto>()
            .ForMember(d => d.Status, opt => opt.MapFrom(s => s.Status.ToString()));
        CreateMap<WorkflowInstanceStep, WorkflowInstanceStepDto>()
            .ForMember(d => d.Status, opt => opt.MapFrom(s => s.Status.ToString()))
            .ForMember(d => d.ActionType, opt => opt.MapFrom(s => s.ActionType != null ? s.ActionType.ToString() : null));

        // Automation
        CreateMap<AutomationRule, AutomationRuleDto>();
        CreateMap<AutomationTrigger, AutomationTriggerDto>();
        CreateMap<AutomationCondition, AutomationConditionDto>();
        CreateMap<AutomationAction, AutomationActionDto>()
            .ForMember(d => d.ActionType, opt => opt.MapFrom(s => s.ActionType.ToString()));

        // Audit
        CreateMap<AuditEntry, AuditEntryDto>();

        // Timeline
        CreateMap<TimelineEvent, TimelineEventDto>();

        // Tokens
        CreateMap<TokenDefinition, TokenDefinitionDto>()
            .ForMember(d => d.CategoryCode, opt => opt.MapFrom(s => s.Category.Code))
            .ForMember(d => d.CategoryNameEn, opt => opt.MapFrom(s => s.Category.NameEn));
        CreateMap<TokenCategory, TokenCategoryDto>();

        // Dashboards
        CreateMap<DashboardDefinition, DashboardDefinitionDto>();
        CreateMap<DashboardCategory, DashboardCategoryDto>();
        CreateMap<DashboardTemplate, DashboardTemplateDto>();
        CreateMap<DashboardShare, DashboardShareDto>();
        CreateMap<DashboardWidget, DashboardWidgetDto>()
            .ForMember(d => d.WidgetType, opt => opt.MapFrom(s => s.WidgetType.ToString()));
        CreateMap<WidgetDefinition, WidgetDefinitionDto>();
        CreateMap<WidgetDataSource, WidgetDataSourceDto>();
        CreateMap<WidgetDrilldown, WidgetDrilldownDto>();
        CreateMap<WidgetLayout, WidgetLayoutDto>();
        CreateMap<WidgetFilter, WidgetFilterDto>();

        // Reports
        CreateMap<ReportDefinition, ReportDefinitionDto>();
        CreateMap<ReportTemplate, ReportTemplateDto>();
        CreateMap<ReportField, ReportFieldDto>();
        CreateMap<ReportRelationship, ReportRelationshipDto>();
        CreateMap<ReportFilter, ReportFilterDto>();
        CreateMap<ReportGrouping, ReportGroupingDto>();
        CreateMap<ReportSorting, ReportSortingDto>();
        CreateMap<ReportSchedule, ReportScheduleDto>();
        CreateMap<ReportShare, ReportShareDto>();
        CreateMap<ReportFolder, ReportFolderDto>();
        CreateMap<ReportTag, ReportTagDto>();

        // Documents
        CreateMap<DocumentTemplate, DocumentTemplateDto>();
        CreateMap<PageTemplate, PageTemplateDto>();
        CreateMap<DocumentTemplateVersion, DocumentTemplateVersionDto>();
        CreateMap<DocumentTemplateToken, DocumentTemplateTokenDto>();
        CreateMap<GeneratedDocument, GeneratedDocumentDto>();
        CreateMap<CompanyBranding, CompanyBrandingDto>();

        // Organization Graph
        CreateMap<OrgNode, OrgNodeDto>();
        CreateMap<OrgEdge, OrgEdgeDto>();
        CreateMap<OrgGraphLayout, OrgGraphLayoutDto>();
        CreateMap<EmployeeReportingLine, EmployeeReportingLineDto>();

        // Workflow Enhancement
        CreateMap<WorkflowDynamicApprover, WorkflowDynamicApproverDto>();
        CreateMap<WorkflowDynamicCondition, WorkflowDynamicConditionDto>();
        CreateMap<WorkflowAction, WorkflowActionDto>();
        CreateMap<WorkflowSimulation, WorkflowSimulationDto>();

        // Company Configuration
        CreateMap<CompanyProfile, CompanyProfileDto>();
        CreateMap<Position, PositionDto>();
        CreateMap<Grade, GradeDto>();
        CreateMap<CostCenter, CostCenterDto>();
        CreateMap<CalendarSetting, CalendarSettingDto>();
        CreateMap<FiscalPeriod, FiscalPeriodDto>();

        // Master Data Engine
        CreateMap<MasterDataItem, MasterDataItemDto>()
            .ForMember(d => d.Metadata, opt => opt.MapFrom(s => ParseMetadata(s.MetadataJson)));
        CreateMap<MasterDataItem, LookupItemDto>()
            .ForMember(d => d.Label, opt => opt.MapFrom(s =>
                string.IsNullOrWhiteSpace(s.NameAr) ? s.NameEn : s.NameAr))
            .ForMember(d => d.Metadata, opt => opt.MapFrom(s => ParseMetadata(s.MetadataJson)));
    }

    private static Dictionary<string, object>? ParseMetadata(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object>>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
