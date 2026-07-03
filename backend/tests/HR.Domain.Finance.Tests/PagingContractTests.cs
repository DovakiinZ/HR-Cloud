using FluentAssertions;
using HR.Application.Common.Paging;
using HR.Infrastructure.Common.Paging;
using Xunit;

namespace HR.Domain.Finance.Tests;

public class PagingContractTests
{
    [Fact]
    public async Task Paginates_and_reports_total()
    {
        var data = Enumerable.Range(1, 60).AsQueryable();
        var page = await data.ToPagedResultAsync(new PagedRequest(Page: 2, PageSize: 25), default);
        page.Total.Should().Be(60);
        page.Items.Should().HaveCount(25);
        page.Items.First().Should().Be(26);
    }

    [Fact]
    public async Task Clamps_page_to_minimum_1()
    {
        var data = Enumerable.Range(1, 10).AsQueryable();
        var page = await data.ToPagedResultAsync(new PagedRequest(Page: 0, PageSize: 5), default);
        page.Page.Should().Be(1);
        page.Items.First().Should().Be(1);
    }

    [Fact]
    public async Task Clamps_page_size_to_maximum_200()
    {
        var data = Enumerable.Range(1, 300).AsQueryable();
        var page = await data.ToPagedResultAsync(new PagedRequest(Page: 1, PageSize: 500), default);
        page.PageSize.Should().Be(200);
        page.Items.Should().HaveCount(200);
    }

    [Fact]
    public async Task Clamps_page_size_to_minimum_1()
    {
        var data = Enumerable.Range(1, 10).AsQueryable();
        var page = await data.ToPagedResultAsync(new PagedRequest(Page: 1, PageSize: 0), default);
        page.PageSize.Should().Be(1);
        page.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task Returns_empty_when_page_exceeds_total()
    {
        var data = Enumerable.Range(1, 10).AsQueryable();
        var page = await data.ToPagedResultAsync(new PagedRequest(Page: 99, PageSize: 25), default);
        page.Total.Should().Be(10);
        page.Items.Should().BeEmpty();
    }
}
