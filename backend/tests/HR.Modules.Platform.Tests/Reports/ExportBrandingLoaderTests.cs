using FluentAssertions;
using HR.Modules.Platform.Services.Reports;
using Xunit;

namespace HR.Modules.Platform.Tests.Reports;

public class ExportBrandingLoaderTests
{
    [Theory]
    [InlineData("/api/files/8d1e9b7a-1111-2222-3333-444455556666", true)]
    [InlineData("8d1e9b7a-1111-2222-3333-444455556666", true)]
    [InlineData("/api/files/not-a-guid", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void TryGetFileId_parses_guid_from_url_tail(string? url, bool expected)
        => ExportBrandingLoader.TryGetFileId(url, out _).Should().Be(expected);
}
