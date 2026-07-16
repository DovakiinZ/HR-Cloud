using System;
using System.Collections.Generic;
using FluentAssertions;
using HR.Domain.Engines.Reports;
using HR.Modules.Platform.DTOs.Reports;
using HR.Modules.Platform.Services.Reports;
using Xunit;

namespace HR.Modules.Platform.Tests.Reports;

/// <summary>ReportDefinitionDto dropped FolderId, tags, and favorite/pin state even though the
/// entity carried them, so the list page had no folder or tag state to render. These lock the
/// stitching of the two things AutoMapper cannot map: per-caller state and join-table tags.</summary>
public class ReportListProjectionTests
{
    private static ReportDefinitionDto Report(Guid id) => new() { Id = id, Code = "R", NameEn = "R", NameAr = "ر", ReportType = "Tabular", Scope = "Personal" };

    private static ReportUserState State(Guid reportId, bool favorite = false, bool pinned = false)
        => new() { ReportDefinitionId = reportId, IsFavorite = favorite, IsPinned = pinned };

    private static Dictionary<Guid, List<ReportTagDto>> NoTags() => new();

    [Fact]
    public void Caller_state_projects_onto_the_matching_report()
    {
        var id = Guid.NewGuid();
        var dto = Report(id);

        ReportListProjector.Apply(new[] { dto }, new[] { State(id, favorite: true, pinned: true) }, NoTags());

        dto.IsFavorite.Should().BeTrue();
        dto.IsPinned.Should().BeTrue();
    }

    [Fact]
    public void A_report_with_no_state_row_is_neither_favorite_nor_pinned()
    {
        var dto = Report(Guid.NewGuid());

        ReportListProjector.Apply(new[] { dto }, Array.Empty<ReportUserState>(), NoTags());

        dto.IsFavorite.Should().BeFalse();
        dto.IsPinned.Should().BeFalse();
    }

    /// <summary>The per-caller guarantee: a second user's favorite must not leak onto this
    /// caller's view. Upstream filters states by UserId; this asserts the projector keys on the
    /// report id it was given and never cross-assigns.</summary>
    [Fact]
    public void State_belonging_to_another_report_does_not_leak_across()
    {
        var mine = Report(Guid.NewGuid());
        var other = Report(Guid.NewGuid());

        ReportListProjector.Apply(new[] { mine, other }, new[] { State(mine.Id, favorite: true) }, NoTags());

        mine.IsFavorite.Should().BeTrue();
        other.IsFavorite.Should().BeFalse();
    }

    [Fact]
    public void Tags_attach_to_their_own_report_only()
    {
        var tagged = Report(Guid.NewGuid());
        var untagged = Report(Guid.NewGuid());
        var tag = new ReportTagDto { Id = Guid.NewGuid(), Name = "Finance", Color = "#0af" };

        ReportListProjector.Apply(
            new[] { tagged, untagged },
            Array.Empty<ReportUserState>(),
            new Dictionary<Guid, List<ReportTagDto>> { [tagged.Id] = new() { tag } });

        tagged.Tags.Should().ContainSingle().Which.Name.Should().Be("Finance");
        untagged.Tags.Should().BeEmpty();
    }
}
