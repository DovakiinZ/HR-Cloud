using HR.Application.Common.Exceptions;
using HR.Application.Engines.Forms;
using HR.Domain.Enums;
using HR.Modules.Platform.DTOs.Forms;
using MediatR;

namespace HR.Modules.Platform.Commands.Forms;

public record UpdateFormFieldCommand : IRequest<FormFieldDto>
{
    public Guid Id { get; init; }
    /// <summary>
    /// The field's internal key. Changing this is only permitted for Custom-classified fields;
    /// SystemRequired / BusinessRequired / Optional fields have their Code locked.
    /// </summary>
    public string? Code { get; init; }
    public string NameEn { get; init; } = null!;
    public string NameAr { get; init; } = null!;
    public FieldType FieldType { get; init; }
    public bool IsRequired { get; init; }
    public int SortOrder { get; init; }
    public string? SectionName { get; init; }
    public string? Placeholder { get; init; }
    public string? DefaultValue { get; init; }
    public string? ValidationRules { get; init; }
    public string? Options { get; init; }
}

public class UpdateFormFieldCommandHandler : IRequestHandler<UpdateFormFieldCommand, FormFieldDto>
{
    private readonly HR.Infrastructure.Persistence.ApplicationDbContext _context;
    private readonly AutoMapper.IMapper _mapper;

    public UpdateFormFieldCommandHandler(HR.Infrastructure.Persistence.ApplicationDbContext context, AutoMapper.IMapper mapper)
    {
        _context = context;
        _mapper = mapper;
    }

    public async Task<FormFieldDto> Handle(UpdateFormFieldCommand request, CancellationToken cancellationToken)
    {
        var entity = await _context.FormFields.FindAsync(new object[] { request.Id }, cancellationToken)
            ?? throw new NotFoundException("FormField", request.Id);

        // ── Field-lock guards ────────────────────────────────────────────────
        var classification = FormFieldClassification.Of(entity.MetadataJson);

        if (classification == FieldClassification.SystemRequired)
        {
            if (!request.IsRequired)
                throw new ForbiddenException(
                    "لا يمكن تعطيل حقل نظامي مطلوب / A system-required field cannot be made optional or disabled.");
        }

        // Code change is only allowed for Custom fields.
        var incomingCode = request.Code;
        if (incomingCode is not null
            && !string.Equals(entity.Code, incomingCode, StringComparison.Ordinal)
            && classification != FieldClassification.Custom)
        {
            throw new ForbiddenException(
                "لا يمكن تغيير المُعرّف الداخلي للحقل / The internal field key cannot be changed.");
        }

        // ── Apply changes ────────────────────────────────────────────────────
        if (incomingCode is not null)
            entity.Code = incomingCode;

        entity.NameEn = request.NameEn;
        entity.NameAr = request.NameAr;
        entity.FieldType = request.FieldType;
        entity.IsRequired = request.IsRequired;
        entity.SortOrder = request.SortOrder;
        entity.SectionName = request.SectionName;
        entity.Placeholder = request.Placeholder;
        entity.DefaultValue = request.DefaultValue;
        entity.ValidationRules = request.ValidationRules;
        entity.Options = request.Options;

        await _context.SaveChangesAsync(cancellationToken);

        return _mapper.Map<FormFieldDto>(entity);
    }
}
