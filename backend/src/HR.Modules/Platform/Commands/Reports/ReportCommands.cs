using System.Text.Json.Serialization;
using AutoMapper;
using HR.Application.Common.Exceptions;
using HR.Application.Common.Interfaces;
using HR.Domain.Engines.Reports;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using HR.Modules.Platform.DTOs.Reports;
using HR.Modules.Platform.Services.Reports;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Platform.Commands.Reports;

public record CreateReportCommand : IRequest<ReportDefinitionDto>
{
    public string Code { get; init; } = null!;
    public string NameEn { get; init; } = null!;
    public string NameAr { get; init; } = null!;
    public string? Description { get; init; }
    [JsonConverter(typeof(JsonStringEnumConverter))] public ReportType ReportType { get; init; }
    [JsonConverter(typeof(JsonStringEnumConverter))] public ReportScope Scope { get; init; }
    public Guid PrimaryObjectId { get; init; }
    public Guid? TemplateId { get; init; }
}

public record UpdateReportCommand : IRequest<ReportDefinitionDto>
{
    public Guid Id { get; init; }
    public string NameEn { get; init; } = null!;
    public string NameAr { get; init; } = null!;
    public string? Description { get; init; }
    [JsonConverter(typeof(JsonStringEnumConverter))] public ReportType ReportType { get; init; }
    [JsonConverter(typeof(JsonStringEnumConverter))] public ReportScope Scope { get; init; }
}

public record DeleteReportCommand(Guid Id) : IRequest;

public record PublishReportCommand(Guid Id) : IRequest<ReportDefinitionDto>;

public record CloneReportCommand : IRequest<ReportDefinitionDto>
{
    public Guid SourceReportId { get; init; }
    public string NewCode { get; init; } = null!;
    public string NameEn { get; init; } = null!;
    public string NameAr { get; init; } = null!;
}

public record AddReportFieldCommand : IRequest<ReportFieldDto>
{
    public Guid ReportDefinitionId { get; init; }
    [JsonConverter(typeof(JsonStringEnumConverter))] public ReportFieldType FieldType { get; init; }
    public Guid? ObjectDefinitionId { get; init; }
    public string FieldCode { get; init; } = null!;
    public string DisplayNameEn { get; init; } = null!;
    public string DisplayNameAr { get; init; } = null!;
    [JsonConverter(typeof(JsonStringEnumConverter))] public AggregationType? Aggregation { get; init; }
    public string? CalculationExpression { get; init; }

    /// <summary>Formula as authored (e.g. "ROUND(basicSalary * 0.09, 2)"). Compiled to
    /// CalculationExpression on write; wins over a directly-supplied AST.</summary>
    public string? CalculationText { get; init; }
    public string? FormatPattern { get; init; }
    public int Width { get; init; }
    public int SortOrder { get; init; }
}

public record DeleteReportFieldCommand(Guid Id) : IRequest;

public record AddReportFilterCommand : IRequest<ReportFilterDto>
{
    public Guid ReportDefinitionId { get; init; }
    public string FieldCode { get; init; } = null!;
    [JsonConverter(typeof(JsonStringEnumConverter))] public ReportFilterOperator Operator { get; init; }
    public string? Value { get; init; }
    public string? ValueTo { get; init; }
    public string? LogicalOperator { get; init; }
    public bool IsParameter { get; init; }
}

public record DeleteReportFilterCommand(Guid Id) : IRequest;

public record AddReportGroupingCommand : IRequest<ReportGroupingDto>
{
    public Guid ReportDefinitionId { get; init; }
    public string FieldCode { get; init; } = null!;
    public int SortOrder { get; init; }
}

public record DeleteReportGroupingCommand(Guid Id) : IRequest;

public record AddReportSortingCommand : IRequest<ReportSortingDto>
{
    public Guid ReportDefinitionId { get; init; }
    public string FieldCode { get; init; } = null!;
    [JsonConverter(typeof(JsonStringEnumConverter))] public SortDirection Direction { get; init; }
    public int SortOrder { get; init; }
}

public record DeleteReportSortingCommand(Guid Id) : IRequest;

public record AddReportScheduleCommand : IRequest<ReportScheduleDto>
{
    public Guid ReportDefinitionId { get; init; }
    public ReportScheduleFrequency Frequency { get; init; }
    public string? CronExpression { get; init; }
    public ExportFormat ExportFormat { get; init; }
    public string Recipients { get; init; } = null!;
    public int? TimeOfDayMinutes { get; init; }
    public int? DayOfWeek { get; init; }
    public int? DayOfMonth { get; init; }
}

public record DeleteReportScheduleCommand(Guid Id) : IRequest;

public record CreateReportTemplateCommand : IRequest<ReportTemplateDto>
{
    public string Code { get; init; } = null!;
    public string NameEn { get; init; } = null!;
    public string NameAr { get; init; } = null!;
    public string? Description { get; init; }
    public ReportType ReportType { get; init; }
    public Guid PrimaryObjectId { get; init; }
    public string Configuration { get; init; } = null!;
}

// === Handlers ===

public class CreateReportCommandHandler : IRequestHandler<CreateReportCommand, ReportDefinitionDto>
{
    private readonly ApplicationDbContext _context; private readonly IMapper _mapper; private readonly ICurrentUserService _user;
    public CreateReportCommandHandler(ApplicationDbContext context, IMapper mapper, ICurrentUserService user) { _context = context; _mapper = mapper; _user = user; }
    public async Task<ReportDefinitionDto> Handle(CreateReportCommand request, CancellationToken ct)
    {
        // OwnerId is what makes the creator able to edit their own report: ReportAccessResolver.CanEdit
        // is "owner OR a share granting edit". Leaving it null produced ownerless reports that nobody
        // could edit once the edit guard below started being enforced.
        var entity = new ReportDefinition { Code = request.Code, NameEn = request.NameEn, NameAr = request.NameAr, Description = request.Description, ReportType = request.ReportType, Scope = request.Scope, PrimaryObjectId = request.PrimaryObjectId, TemplateId = request.TemplateId, OwnerId = _user.UserId };
        _context.Set<ReportDefinition>().Add(entity);
        await _context.SaveChangesAsync(ct);
        return _mapper.Map<ReportDefinitionDto>(entity);
    }
}

public class UpdateReportCommandHandler : IRequestHandler<UpdateReportCommand, ReportDefinitionDto>
{
    private readonly ApplicationDbContext _context; private readonly IMapper _mapper; private readonly IReportAccessService _access;
    public UpdateReportCommandHandler(ApplicationDbContext context, IMapper mapper, IReportAccessService access) { _context = context; _mapper = mapper; _access = access; }
    public async Task<ReportDefinitionDto> Handle(UpdateReportCommand request, CancellationToken ct)
    {
        await _access.EnsureCanEditAsync(request.Id, ct);
        var entity = await _context.Set<ReportDefinition>().FindAsync(new object[] { request.Id }, ct) ?? throw new NotFoundException("ReportDefinition", request.Id);
        entity.NameEn = request.NameEn; entity.NameAr = request.NameAr; entity.Description = request.Description; entity.ReportType = request.ReportType; entity.Scope = request.Scope;
        await _context.SaveChangesAsync(ct);
        return _mapper.Map<ReportDefinitionDto>(entity);
    }
}

public class DeleteReportCommandHandler : IRequestHandler<DeleteReportCommand>
{
    private readonly ApplicationDbContext _context; private readonly IReportAccessService _access;
    public DeleteReportCommandHandler(ApplicationDbContext context, IReportAccessService access) { _context = context; _access = access; }
    public async Task Handle(DeleteReportCommand request, CancellationToken ct)
    {
        await _access.EnsureCanEditAsync(request.Id, ct);
        var entity = await _context.Set<ReportDefinition>().FindAsync(new object[] { request.Id }, ct) ?? throw new NotFoundException("ReportDefinition", request.Id);
        entity.IsDeleted = true; entity.DeletedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
    }
}

public class PublishReportCommandHandler : IRequestHandler<PublishReportCommand, ReportDefinitionDto>
{
    private readonly ApplicationDbContext _context; private readonly IMapper _mapper; private readonly IReportAccessService _access;
    public PublishReportCommandHandler(ApplicationDbContext context, IMapper mapper, IReportAccessService access) { _context = context; _mapper = mapper; _access = access; }
    public async Task<ReportDefinitionDto> Handle(PublishReportCommand request, CancellationToken ct)
    {
        await _access.EnsureCanEditAsync(request.Id, ct);
        var entity = await _context.Set<ReportDefinition>().FindAsync(new object[] { request.Id }, ct) ?? throw new NotFoundException("ReportDefinition", request.Id);
        entity.IsPublished = true; entity.Version++;
        await _context.SaveChangesAsync(ct);
        return _mapper.Map<ReportDefinitionDto>(entity);
    }
}

public class CloneReportCommandHandler : IRequestHandler<CloneReportCommand, ReportDefinitionDto>
{
    private readonly ApplicationDbContext _context; private readonly IMapper _mapper; private readonly IReportAccessService _access; private readonly ICurrentUserService _user;
    public CloneReportCommandHandler(ApplicationDbContext context, IMapper mapper, IReportAccessService access, ICurrentUserService user) { _context = context; _mapper = mapper; _access = access; _user = user; }
    public async Task<ReportDefinitionDto> Handle(CloneReportCommand request, CancellationToken ct)
    {
        // Read, not edit: copying a report you are allowed to see is legitimate, and the copy is
        // yours. Requiring edit on the source would block cloning a shared read-only report.
        await _access.EnsureCanReadAsync(request.SourceReportId, ct);
        var source = await _context.Set<ReportDefinition>().Include(r => r.Fields).Include(r => r.Filters).Include(r => r.Groupings).Include(r => r.Sortings).Include(r => r.Relationships).FirstOrDefaultAsync(r => r.Id == request.SourceReportId, ct) ?? throw new NotFoundException("ReportDefinition", request.SourceReportId);
        var clone = new ReportDefinition { Code = request.NewCode, NameEn = request.NameEn, NameAr = request.NameAr, Description = source.Description, ReportType = source.ReportType, Scope = ReportScope.Personal, PrimaryObjectId = source.PrimaryObjectId, OwnerId = _user.UserId };
        _context.Set<ReportDefinition>().Add(clone);
        await _context.SaveChangesAsync(ct);
        foreach (var f in source.Fields) _context.Set<ReportField>().Add(new ReportField { ReportDefinitionId = clone.Id, FieldType = f.FieldType, ObjectDefinitionId = f.ObjectDefinitionId, FieldCode = f.FieldCode, DisplayNameEn = f.DisplayNameEn, DisplayNameAr = f.DisplayNameAr, Aggregation = f.Aggregation, CalculationExpression = f.CalculationExpression, CalculationText = f.CalculationText, FormatPattern = f.FormatPattern, Width = f.Width, SortOrder = f.SortOrder, IsVisible = f.IsVisible });
        foreach (var f in source.Filters) _context.Set<ReportFilter>().Add(new ReportFilter { ReportDefinitionId = clone.Id, FieldCode = f.FieldCode, Operator = f.Operator, Value = f.Value, ValueTo = f.ValueTo, LogicalOperator = f.LogicalOperator, IsParameter = f.IsParameter, SortOrder = f.SortOrder });
        foreach (var g in source.Groupings) _context.Set<ReportGrouping>().Add(new ReportGrouping { ReportDefinitionId = clone.Id, FieldCode = g.FieldCode, SortOrder = g.SortOrder });
        foreach (var s in source.Sortings) _context.Set<ReportSorting>().Add(new ReportSorting { ReportDefinitionId = clone.Id, FieldCode = s.FieldCode, Direction = s.Direction, SortOrder = s.SortOrder });
        // Relationships must clone too: without them a multi-object report's fields reference
        // objects that are no longer joined, which fails at run time rather than at clone time.
        foreach (var rel in source.Relationships) _context.Set<ReportRelationship>().Add(new ReportRelationship { ReportDefinitionId = clone.Id, SourceObjectId = rel.SourceObjectId, TargetObjectId = rel.TargetObjectId, JoinField = rel.JoinField, JoinType = rel.JoinType, SortOrder = rel.SortOrder });
        await _context.SaveChangesAsync(ct);
        return _mapper.Map<ReportDefinitionDto>(clone);
    }
}

public class AddReportFieldCommandHandler : IRequestHandler<AddReportFieldCommand, ReportFieldDto>
{
    private readonly ApplicationDbContext _context; private readonly IMapper _mapper; private readonly IReportAccessService _access;
    public AddReportFieldCommandHandler(ApplicationDbContext context, IMapper mapper, IReportAccessService access) { _context = context; _mapper = mapper; _access = access; }
    public async Task<ReportFieldDto> Handle(AddReportFieldCommand request, CancellationToken ct)
    {
        await _access.EnsureCanEditAsync(request.ReportDefinitionId, ct);
        var calculationExpression = ReportFormulaCompiler.Compile(request.CalculationText, request.CalculationExpression);
        var entity = new ReportField { ReportDefinitionId = request.ReportDefinitionId, FieldType = request.FieldType, ObjectDefinitionId = request.ObjectDefinitionId, FieldCode = request.FieldCode, DisplayNameEn = request.DisplayNameEn, DisplayNameAr = request.DisplayNameAr, Aggregation = request.Aggregation, CalculationExpression = calculationExpression, CalculationText = request.CalculationText, FormatPattern = request.FormatPattern, Width = request.Width, SortOrder = request.SortOrder };
        _context.Set<ReportField>().Add(entity);
        await _context.SaveChangesAsync(ct);
        return _mapper.Map<ReportFieldDto>(entity);
    }
}

public class DeleteReportFieldCommandHandler : IRequestHandler<DeleteReportFieldCommand>
{
    private readonly ApplicationDbContext _context; private readonly IReportAccessService _access;
    public DeleteReportFieldCommandHandler(ApplicationDbContext context, IReportAccessService access) { _context = context; _access = access; }
    public async Task Handle(DeleteReportFieldCommand request, CancellationToken ct)
    {
        var entity = await _context.Set<ReportField>().FindAsync(new object[] { request.Id }, ct) ?? throw new NotFoundException("ReportField", request.Id);
        // The command carries only the child's id, so the owning report has to be resolved before
        // the guard can run — a child id must never be a way around the parent's permissions.
        await _access.EnsureCanEditAsync(entity.ReportDefinitionId, ct);
        _context.Set<ReportField>().Remove(entity); await _context.SaveChangesAsync(ct);
    }
}

public class AddReportFilterCommandHandler : IRequestHandler<AddReportFilterCommand, ReportFilterDto>
{
    private readonly ApplicationDbContext _context; private readonly IMapper _mapper; private readonly IReportAccessService _access;
    public AddReportFilterCommandHandler(ApplicationDbContext context, IMapper mapper, IReportAccessService access) { _context = context; _mapper = mapper; _access = access; }
    public async Task<ReportFilterDto> Handle(AddReportFilterCommand request, CancellationToken ct)
    {
        await _access.EnsureCanEditAsync(request.ReportDefinitionId, ct);
        var entity = new ReportFilter { ReportDefinitionId = request.ReportDefinitionId, FieldCode = request.FieldCode, Operator = request.Operator, Value = request.Value, ValueTo = request.ValueTo, LogicalOperator = request.LogicalOperator, IsParameter = request.IsParameter };
        _context.Set<ReportFilter>().Add(entity);
        await _context.SaveChangesAsync(ct);
        return _mapper.Map<ReportFilterDto>(entity);
    }
}

public class DeleteReportFilterCommandHandler : IRequestHandler<DeleteReportFilterCommand>
{
    private readonly ApplicationDbContext _context; private readonly IReportAccessService _access;
    public DeleteReportFilterCommandHandler(ApplicationDbContext context, IReportAccessService access) { _context = context; _access = access; }
    public async Task Handle(DeleteReportFilterCommand request, CancellationToken ct)
    {
        var entity = await _context.Set<ReportFilter>().FindAsync(new object[] { request.Id }, ct) ?? throw new NotFoundException("ReportFilter", request.Id);
        // The command carries only the child's id, so the owning report has to be resolved before
        // the guard can run — a child id must never be a way around the parent's permissions.
        await _access.EnsureCanEditAsync(entity.ReportDefinitionId, ct);
        _context.Set<ReportFilter>().Remove(entity); await _context.SaveChangesAsync(ct);
    }
}

public class AddReportGroupingCommandHandler : IRequestHandler<AddReportGroupingCommand, ReportGroupingDto>
{
    private readonly ApplicationDbContext _context; private readonly IMapper _mapper; private readonly IReportAccessService _access;
    public AddReportGroupingCommandHandler(ApplicationDbContext context, IMapper mapper, IReportAccessService access) { _context = context; _mapper = mapper; _access = access; }
    public async Task<ReportGroupingDto> Handle(AddReportGroupingCommand request, CancellationToken ct)
    {
        await _access.EnsureCanEditAsync(request.ReportDefinitionId, ct);
        var entity = new ReportGrouping { ReportDefinitionId = request.ReportDefinitionId, FieldCode = request.FieldCode, SortOrder = request.SortOrder };
        _context.Set<ReportGrouping>().Add(entity); await _context.SaveChangesAsync(ct);
        return _mapper.Map<ReportGroupingDto>(entity);
    }
}

public class DeleteReportGroupingCommandHandler : IRequestHandler<DeleteReportGroupingCommand>
{
    private readonly ApplicationDbContext _context; private readonly IReportAccessService _access;
    public DeleteReportGroupingCommandHandler(ApplicationDbContext context, IReportAccessService access) { _context = context; _access = access; }
    public async Task Handle(DeleteReportGroupingCommand request, CancellationToken ct)
    {
        var entity = await _context.Set<ReportGrouping>().FindAsync(new object[] { request.Id }, ct) ?? throw new NotFoundException("ReportGrouping", request.Id);
        // The command carries only the child's id, so the owning report has to be resolved before
        // the guard can run — a child id must never be a way around the parent's permissions.
        await _access.EnsureCanEditAsync(entity.ReportDefinitionId, ct);
        _context.Set<ReportGrouping>().Remove(entity); await _context.SaveChangesAsync(ct);
    }
}

public class AddReportSortingCommandHandler : IRequestHandler<AddReportSortingCommand, ReportSortingDto>
{
    private readonly ApplicationDbContext _context; private readonly IMapper _mapper; private readonly IReportAccessService _access;
    public AddReportSortingCommandHandler(ApplicationDbContext context, IMapper mapper, IReportAccessService access) { _context = context; _mapper = mapper; _access = access; }
    public async Task<ReportSortingDto> Handle(AddReportSortingCommand request, CancellationToken ct)
    {
        await _access.EnsureCanEditAsync(request.ReportDefinitionId, ct);
        var entity = new ReportSorting { ReportDefinitionId = request.ReportDefinitionId, FieldCode = request.FieldCode, Direction = request.Direction, SortOrder = request.SortOrder };
        _context.Set<ReportSorting>().Add(entity); await _context.SaveChangesAsync(ct);
        return _mapper.Map<ReportSortingDto>(entity);
    }
}

public class DeleteReportSortingCommandHandler : IRequestHandler<DeleteReportSortingCommand>
{
    private readonly ApplicationDbContext _context; private readonly IReportAccessService _access;
    public DeleteReportSortingCommandHandler(ApplicationDbContext context, IReportAccessService access) { _context = context; _access = access; }
    public async Task Handle(DeleteReportSortingCommand request, CancellationToken ct)
    {
        var entity = await _context.Set<ReportSorting>().FindAsync(new object[] { request.Id }, ct) ?? throw new NotFoundException("ReportSorting", request.Id);
        // The command carries only the child's id, so the owning report has to be resolved before
        // the guard can run — a child id must never be a way around the parent's permissions.
        await _access.EnsureCanEditAsync(entity.ReportDefinitionId, ct);
        _context.Set<ReportSorting>().Remove(entity); await _context.SaveChangesAsync(ct);
    }
}

public class AddReportScheduleCommandHandler : IRequestHandler<AddReportScheduleCommand, ReportScheduleDto>
{
    private readonly ApplicationDbContext _context; private readonly IMapper _mapper; private readonly IReportAccessService _access;
    public AddReportScheduleCommandHandler(ApplicationDbContext context, IMapper mapper, IReportAccessService access) { _context = context; _mapper = mapper; _access = access; }
    public async Task<ReportScheduleDto> Handle(AddReportScheduleCommand request, CancellationToken ct)
    {
        await _access.EnsureCanEditAsync(request.ReportDefinitionId, ct);
        var entity = new ReportSchedule
        {
            ReportDefinitionId = request.ReportDefinitionId,
            Frequency = request.Frequency,
            CronExpression = request.CronExpression,
            ExportFormat = request.ExportFormat,
            Recipients = request.Recipients,
            TimeOfDayMinutes = request.TimeOfDayMinutes,
            DayOfWeek = request.DayOfWeek,
            DayOfMonth = request.DayOfMonth,
            IsActive = true,
        };
        entity.NextRunAt = HR.Modules.Platform.Services.Reports.ScheduleMath.ComputeNextRun(entity, DateTime.UtcNow);
        _context.Set<ReportSchedule>().Add(entity); await _context.SaveChangesAsync(ct);
        return _mapper.Map<ReportScheduleDto>(entity);
    }
}

public class DeleteReportScheduleCommandHandler : IRequestHandler<DeleteReportScheduleCommand>
{
    private readonly ApplicationDbContext _context; private readonly IReportAccessService _access;
    public DeleteReportScheduleCommandHandler(ApplicationDbContext context, IReportAccessService access) { _context = context; _access = access; }
    public async Task Handle(DeleteReportScheduleCommand request, CancellationToken ct)
    {
        var entity = await _context.Set<ReportSchedule>().FindAsync(new object[] { request.Id }, ct) ?? throw new NotFoundException("ReportSchedule", request.Id);
        // The command carries only the child's id, so the owning report has to be resolved before
        // the guard can run — a child id must never be a way around the parent's permissions.
        await _access.EnsureCanEditAsync(entity.ReportDefinitionId, ct);
        _context.Set<ReportSchedule>().Remove(entity); await _context.SaveChangesAsync(ct);
    }
}

public class CreateReportTemplateCommandHandler : IRequestHandler<CreateReportTemplateCommand, ReportTemplateDto>
{
    private readonly ApplicationDbContext _context; private readonly IMapper _mapper;
    public CreateReportTemplateCommandHandler(ApplicationDbContext context, IMapper mapper) { _context = context; _mapper = mapper; }
    public async Task<ReportTemplateDto> Handle(CreateReportTemplateCommand request, CancellationToken ct)
    {
        var entity = new ReportTemplate { Code = request.Code, NameEn = request.NameEn, NameAr = request.NameAr, Description = request.Description, ReportType = request.ReportType, PrimaryObjectId = request.PrimaryObjectId, Configuration = request.Configuration };
        _context.Set<ReportTemplate>().Add(entity); await _context.SaveChangesAsync(ct);
        return _mapper.Map<ReportTemplateDto>(entity);
    }
}
