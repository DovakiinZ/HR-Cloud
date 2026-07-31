using HR.Domain.Engines.Attendance;

namespace HR.Application.Engines.Attendance;

/// <summary>
/// Bridges Application-layer <see cref="PermissionTypeRules"/> (from a MasterDataItem's MetadataJson)
/// and the Domain-layer <see cref="AttendancePolicy"/> (tenant default caps) into a flat
/// <see cref="PermissionLimitSet"/> that the pure evaluator can consume.
/// <para>Mapping rules:
/// <list type="bullet">
///   <item>All five limit dims: type-level value takes precedence; if null the monthly dims
///   fall back to the policy (MaxMinutesPerMonth → policy.PermissionMaxMinutesPerMonth;
///   MaxRequestsPerMonth → policy.PermissionMaxPerMonth).
///   Per-request, per-day-minutes, and per-day-requests have <em>no</em> policy fallback.</item>
///   <item>Behavior always comes from <see cref="PermissionTypeRules.ExceedBehavior"/>;
///   the policy's <c>PermissionCapMode</c> is ignored.</item>
/// </list>
/// </para>
/// Lives in HR.Application (not HR.Domain) because it references the Application-layer type
/// <see cref="PermissionTypeRules"/>; HR.Domain cannot see Application types.
/// </summary>
public static class PermissionLimitResolver
{
    public static PermissionLimitSet Resolve(PermissionTypeRules rules, AttendancePolicy? policy)
        => new(
            MaxMinutesPerRequest: rules.MaxMinutesPerRequest,
            MaxMinutesPerDay:     rules.MaxMinutesPerDay,
            MaxMinutesPerMonth:   rules.MaxMinutesPerMonth ?? policy?.PermissionMaxMinutesPerMonth,
            MaxRequestsPerDay:    rules.MaxRequestsPerDay,
            MaxRequestsPerMonth:  rules.MaxRequestsPerMonth ?? policy?.PermissionMaxPerMonth,
            Behavior:             rules.ExceedBehavior);
}
