using HR.Application.Engines.Completion;
using HR.Domain.Engines.Assets;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Platform.Services.Completion.Executors;

/// <summary>
/// Hands an asset to the requesting employee as custody.
///
/// Throws when the asset is already out with someone: the engine catches it, rolls back the whole
/// completion transaction and lands the request in CompletionFailed, which is the correct outcome —
/// approving a custody request for a laptop that is already assigned should not half-succeed. The
/// database also carries a filtered unique index over open custodies, so a race between two
/// concurrent approvals fails at commit rather than double-assigning.
/// </summary>
public sealed class AssetsAssignCustodyExecutor : IEffectExecutor
{
    private readonly ApplicationDbContext _db;

    public AssetsAssignCustodyExecutor(ApplicationDbContext db) => _db = db;

    public string EffectType => EffectTypes.AssetsAssignCustody;

    public async Task<EffectExecutionResult> ExecuteAsync(EffectContext ctx, CancellationToken ct)
    {
        var assetId = ctx.Guid("assetId") ?? System.Guid.Empty;
        if (assetId == System.Guid.Empty)
            throw new InvalidOperationException("Assets.AssignCustody: no asset was provided.");

        var asset = await _db.Set<Asset>().FirstOrDefaultAsync(a => a.Id == assetId, ct)
            ?? throw new InvalidOperationException($"Assets.AssignCustody: asset {assetId} not found.");

        if (!asset.IsActive)
            throw new InvalidOperationException($"Assets.AssignCustody: asset '{asset.Code}' is not active.");

        var openCustody = await _db.Set<AssetCustody>()
            .FirstOrDefaultAsync(c => c.AssetId == assetId && c.ReturnedAt == null, ct);
        if (openCustody is not null)
            throw new InvalidOperationException(
                $"Assets.AssignCustody: asset '{asset.Code}' is already assigned and has not been returned.");

        // employeeId is mapped from RequestContext, so it is the requester; falling back to the
        // context's EmployeeId keeps the effect working if the mapping is ever omitted.
        var employeeId = ctx.Guid("employeeId") ?? System.Guid.Empty;
        if (employeeId == System.Guid.Empty) employeeId = ctx.EmployeeId;

        var custody = new AssetCustody
        {
            AssetId = assetId,
            EmployeeId = employeeId,
            RequestInstanceId = ctx.RequestInstanceId,
            AssignedAt = DateTime.UtcNow,
            ExpectedReturnDate = ctx.Date("expectedReturnDate"),
            Notes = ctx.Str("notes"),
        };
        _db.Set<AssetCustody>().Add(custody);

        return EffectExecutionResult.Ok(
            targetEntityType: "AssetCustody",
            targetRecordId: custody.Id,
            after: new { custody.AssetId, custody.EmployeeId, custody.AssignedAt, custody.ExpectedReturnDate },
            summary: $"Asset {asset.Code} assigned to employee {employeeId}");
    }
}
