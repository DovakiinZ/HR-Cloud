using System.Collections.Generic;
using System.Text;
using FluentAssertions;
using HR.Application.Common.Exceptions;
using HR.Modules.Platform.Services.Reports;
using Xunit;

namespace HR.Modules.Platform.Tests.Reports;

public class SifReportExporterTests
{
    private static ReportColumn Col(string code) => new() { Code = code, Label = code };

    private static ReportResult ValidResult()
    {
        var result = new ReportResult
        {
            Columns = new()
            {
                Col("EmployeeNumber"), Col("NationalId"), Col("EmployeeName"),
                Col("Iban"), Col("BankCode"), Col("NetAmount"), Col("Currency"),
            },
            Rows = new()
            {
                new ReportRow(new Dictionary<string, object?>
                {
                    ["EmployeeNumber"] = "E1", ["NationalId"] = "1122334455", ["EmployeeName"] = "Ali",
                    ["Iban"] = "SA0380000000608010167519", ["BankCode"] = "RIBLSARI",
                    ["NetAmount"] = 5000.0, ["Currency"] = "SAR",
                }),
            },
        };
        return result;
    }

    [Fact]
    public void Exports_valid_sif_csv_with_header_and_row()
    {
        var bytes = SifReportExporter.Export(ValidResult());
        bytes.Should().NotBeNullOrEmpty();
        var text = Encoding.UTF8.GetString(bytes);
        text.Should().Contain("IBAN");
        text.Should().Contain("SA0380000000608010167519");
        text.Should().Contain("5000.00"); // WPS 2-decimal formatting
    }

    [Fact]
    public void Missing_required_column_throws_naming_it()
    {
        var result = ValidResult();
        result.Columns.RemoveAll(c => c.Code == "Iban");
        var act = () => SifReportExporter.Export(result);
        act.Should().Throw<ValidationException>().Which.Message.Should().Contain("Iban");
    }
}
