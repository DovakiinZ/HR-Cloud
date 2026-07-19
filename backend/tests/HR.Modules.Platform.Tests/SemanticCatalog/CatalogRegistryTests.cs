using System.Linq;
using FluentAssertions;
using HR.Application.SemanticCatalog.Contracts;
using HR.Modules.Platform.Services.SemanticCatalog;
using Xunit;

namespace HR.Modules.Platform.Tests.SemanticCatalog;

public class CatalogRegistryTests
{
    private static readonly string[] KnownViz = { "KpiCard", "BarChart", "LineChart", "PieChart", "Table", "Gauge" };
    private static readonly string[] KnownAgg = { "Count", "Sum", "Average", "Min", "Max", "DistinctCount", "Formula" };

    [Fact] public void Domain_codes_unique()
        => CatalogRegistry.Domains.Select(d => d.Code).Should().OnlyHaveUniqueItems();

    [Fact] public void Object_codes_unique()
        => CatalogRegistry.Objects.Select(o => o.ObjectCode).Should().OnlyHaveUniqueItems();

    [Fact] public void Metric_codes_unique()
        => CatalogRegistry.Metrics.Select(m => m.Code).Should().OnlyHaveUniqueItems();

    [Fact] public void Field_group_codes_unique()
        => CatalogRegistry.FieldGroups.Select(g => g.Code).Should().OnlyHaveUniqueItems();

    [Fact]
    public void Every_object_domain_is_defined()
    {
        var domains = CatalogRegistry.Domains.Select(d => d.Code).ToHashSet();
        CatalogRegistry.Objects.Select(o => o.DomainCode).Should().OnlyContain(d => domains.Contains(d));
    }

    [Fact]
    public void Every_field_group_is_defined_globally()
    {
        var groups = CatalogRegistry.FieldGroups.Select(g => g.Code).ToHashSet();
        foreach (var o in CatalogRegistry.Objects)
            o.Fields.Select(f => f.GroupCode).Should().OnlyContain(g => groups.Contains(g), $"object {o.ObjectCode}");
    }

    [Fact]
    public void Every_metric_is_well_formed()
    {
        var domains = CatalogRegistry.Domains.Select(d => d.Code).ToHashSet();
        foreach (var m in CatalogRegistry.Metrics)
        {
            domains.Should().Contain(m.DomainCode, $"metric {m.Code} domain");
            m.RequiredPermissions.Should().NotBeEmpty($"metric {m.Code} permissions");
            KnownViz.Should().Contain(m.DefaultVisualization, $"metric {m.Code} viz");
            KnownAgg.Should().Contain(m.Definition.Aggregation, $"metric {m.Code} agg");
            if (m.Definition.Aggregation == "Formula")
            {
                m.Definition.Formula.Should().NotBeNullOrWhiteSpace($"metric {m.Code} formula");
                m.Definition.Measures.Should().NotBeNullOrEmpty($"metric {m.Code} measures");
            }
        }
    }

    [Fact]
    public void Has_the_seventeen_named_metrics()
    {
        var expected = new[]
        {
            "total_employees","active_employees","new_employees","employees_by_department",
            "gross_payroll","net_payroll","total_deductions","late_employees","absent_employees",
            "overtime_minutes","remaining_leave_balance","pending_requests","expiring_contracts",
            "expiring_documents","total_gosi","total_additions","pending_approvals",
        };
        CatalogRegistry.Metrics.Select(m => m.Code).Should().Contain(expected);
    }
}
