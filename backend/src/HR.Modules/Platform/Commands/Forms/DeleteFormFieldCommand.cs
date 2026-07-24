using HR.Application.Common.Exceptions;
using HR.Application.Engines.Completion;
using HR.Application.Engines.Forms;
using HR.Domain.Engines.Forms;
using HR.Domain.Engines.Requests;
using HR.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Platform.Commands.Forms;

public record DeleteFormFieldCommand(Guid Id) : IRequest;

public class DeleteFormFieldCommandHandler : IRequestHandler<DeleteFormFieldCommand>
{
    private readonly HR.Infrastructure.Persistence.ApplicationDbContext _context;

    public DeleteFormFieldCommandHandler(HR.Infrastructure.Persistence.ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task Handle(DeleteFormFieldCommand request, CancellationToken cancellationToken)
    {
        var entity = await _context.FormFields.FindAsync(new object[] { request.Id }, cancellationToken)
            ?? throw new NotFoundException("FormField", request.Id);

        // ── Field-lock guards ────────────────────────────────────────────────
        var classification = FormFieldClassification.Of(entity.MetadataJson);

        if (classification == FieldClassification.SystemRequired)
            throw new ForbiddenException(
                "حقل نظامي مطلوب ولا يمكن حذفه / System-required field cannot be deleted.");

        if (await IsMappedByRequiredEffectAsync(entity, cancellationToken))
            throw new ForbiddenException(
                "هذا الحقل مرتبط بإجراء مطلوب ولا يمكن حذفه / This field is used by a required effect and cannot be deleted.");

        _context.FormFields.Remove(entity);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Returns true when any enabled, required RequestEffectDefinition on a RequestType that uses
    /// this field's form has an input mapping whose source is FormField and whose key equals this
    /// field's Code.
    /// </summary>
    private async Task<bool> IsMappedByRequiredEffectAsync(FormField field, CancellationToken ct)
    {
        var configs = await _context.Set<RequestType>()
            .Where(t => t.FormDefinitionId == field.FormDefinitionId)
            .Join(
                _context.Set<RequestEffectDefinition>()
                    .Where(e => e.IsRequired && e.IsEnabled),
                t => t.Id,
                e => e.RequestTypeId,
                (t, e) => e.ConfigurationJson)
            .ToListAsync(ct);

        foreach (var json in configs)
        {
            var cfg = EffectConfiguration.TryParse(json);
            if (cfg is null) continue;
            if (cfg.Inputs.Values.Any(m =>
                    m.Source == EffectValueSource.FormField
                    && string.Equals(m.Key, field.Code, StringComparison.OrdinalIgnoreCase)))
                return true;
        }
        return false;
    }
}
