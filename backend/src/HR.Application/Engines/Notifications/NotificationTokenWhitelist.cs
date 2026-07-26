using System.Text.RegularExpressions;

namespace HR.Application.Engines.Notifications;

/// <summary>The closed set of {{tokens}} a notification template may reference. Mirrors the keys
/// DocumentTokenResolver produces. Unknown tokens are reported for a validation warning and are left
/// visible at render time — never resolved against arbitrary object properties.</summary>
public static class NotificationTokenWhitelist
{
    private static readonly Regex TokenPattern = new(@"\{\{\s*([\w.]+)\s*\}\}", RegexOptions.Compiled);

    public static readonly IReadOnlySet<string> AllowedTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // Employee
        "Employee.FullName", "Employee.EmployeeNumber", "Employee.Department", "Employee.JobTitle",
        "Employee.Manager", "Employee.Nationality", "Employee.NationalId", "Employee.HireDate",
        "Employee.Email", "Employee.Phone",

        // Request
        "Request.Number", "Request.Type", "Request.CreatedDate", "Request.ApprovalDate", "Request.Status",

        // Leave
        "Leave.Type", "Leave.StartDate", "Leave.EndDate", "Leave.Days",

        // Payroll (emitted by DocumentTokenResolver.ResolveForRequestAsync)
        "Payroll.BasicSalary", "Payroll.HousingAllowance", "Payroll.TransportationAllowance",
        "Payroll.TotalSalary", "Payroll.Currency",

        // Company
        "Company.Name", "Company.NameEn", "Company.CR", "Company.VAT", "Company.Address",
        "Company.Phone", "Company.Email", "Company.Website",

        // System
        "System.Today",

        // Legacy aliases (originally-seeded token names; kept so old templates never warn)
        "Request.LeaveType", "Request.StartDate", "Request.EndDate", "Request.Days",
        "EmployeeName", "EmployeeNumber", "Department", "JobTitle",
        "LeaveType", "StartDate", "EndDate",
        "CompanyName", "CRNumber", "VATNumber", "GeneratedDate",
    };

    /// <summary>Distinct tokens in the template that are not on the whitelist.</summary>
    public static IReadOnlyList<string> FindUnknownTokens(string? template)
    {
        if (string.IsNullOrEmpty(template)) return Array.Empty<string>();
        return TokenPattern.Matches(template)
            .Select(m => m.Groups[1].Value)
            .Where(t => !AllowedTokens.Contains(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
