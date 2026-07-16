using HR.Domain.Engines.Reports;
using HR.Modules.Platform.DTOs.Reports;

namespace HR.Modules.Platform.Services.Reports;

/// <summary>Stitches per-caller state (favorite/pin) and tags onto mapped report DTOs.
///
/// AutoMapper cannot do this: IsFavorite/IsPinned are properties of the (report, caller) pair,
/// not of the report, and tags arrive through a join table. Both are therefore loaded as batched
/// side queries and stitched here — a pure function, so the stitching is testable without a DB.</summary>
public static class ReportListProjector
{
    /// <param name="states">The caller's states only. Rows for other users must be filtered out
    /// upstream — this projector trusts what it is given and cannot tell them apart.</param>
    /// <param name="tagsByReportId">Tags per report id, from the ReportDefinitionTag join.</param>
    public static void Apply(
        IEnumerable<ReportDefinitionDto> reports,
        IReadOnlyCollection<ReportUserState> states,
        IReadOnlyDictionary<Guid, List<ReportTagDto>> tagsByReportId)
    {
        var stateByReportId = new Dictionary<Guid, ReportUserState>();
        foreach (var s in states) stateByReportId[s.ReportDefinitionId] = s;

        foreach (var report in reports)
        {
            if (stateByReportId.TryGetValue(report.Id, out var state))
            {
                report.IsFavorite = state.IsFavorite;
                report.IsPinned = state.IsPinned;
            }

            if (tagsByReportId.TryGetValue(report.Id, out var tags))
                report.Tags = tags;
        }
    }
}
