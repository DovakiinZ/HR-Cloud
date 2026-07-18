using HR.Domain.Engines.ObjectRegistry;
using HR.Infrastructure.Persistence;
using HR.Modules.Platform.Services.Catalog;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Platform.Commands.ObjectRegistry;

/// <summary>
/// Idempotent seed that registers the five main HR entities as
/// <see cref="ObjectDefinition"/> rows so the report engine and the
/// Report Field Registry can target them.  Additive only — never
/// deletes or modifies existing rows.
/// </summary>
public sealed record RegisterReportableObjectsCommand : IRequest<int>;

public sealed class RegisterReportableObjectsCommandHandler
    : IRequestHandler<RegisterReportableObjectsCommand, int>
{
    private static readonly string[] ReportableCodes =
    {
        "Employee",
        "AttendanceRecord",
        "PayrollPayslip",
        "LeaveBalance",
        "RequestInstance",
    };

    private readonly ApplicationDbContext _db;
    private readonly IObjectCatalogService _catalog;
    private readonly ILogger<RegisterReportableObjectsCommandHandler> _logger;

    public RegisterReportableObjectsCommandHandler(
        ApplicationDbContext db,
        IObjectCatalogService catalog,
        ILogger<RegisterReportableObjectsCommandHandler> logger)
    {
        _db      = db;
        _catalog = catalog;
        _logger  = logger;
    }

    public async Task<int> Handle(
        RegisterReportableObjectsCommand request,
        CancellationToken cancellationToken)
    {
        // Tenant-scoped read of already-registered codes (global query filter is active).
        var existing = await _db.ObjectDefinitions
            .Select(o => o.Code)
            .ToListAsync(cancellationToken);

        var existingSet = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);

        int created = 0;

        foreach (var code in ReportableCodes)
        {
            if (existingSet.Contains(code))
                continue;

            var resolved = _catalog.Resolve(code);
            var obj      = _catalog.GetObject(code);

            if (resolved is null || obj is null)
            {
                _logger.LogWarning(
                    "RegisterReportableObjects: code '{Code}' is not resolvable in the object catalog — skipped.",
                    code);
                continue;
            }

            _db.ObjectDefinitions.Add(new ObjectDefinition
            {
                Code      = code,
                NameEn    = obj.NameEn,
                NameAr    = obj.NameAr,
                Module    = string.IsNullOrWhiteSpace(obj.Module) ? "Reports" : obj.Module,
                TableName = resolved.TableName,
                IsSystem  = true,
                IsActive  = true,
            });

            created++;
        }

        if (created > 0)
            await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "RegisterReportableObjects: {Created} object definition(s) registered.",
            created);

        return created;
    }
}
