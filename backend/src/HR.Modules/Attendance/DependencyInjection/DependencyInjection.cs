using System.Reflection;
using HR.Application.Engines.Completion;
using HR.Modules.Attendance.Services;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.Attendance;

public static class DependencyInjection
{
    public static IServiceCollection AddAttendanceModule(this IServiceCollection services)
    {
        services.AddScoped<IAttendanceCalculationService, AttendanceCalculationService>();
        services.AddScoped<IShiftResolver, ShiftResolver>();
        services.AddScoped<IAttendanceService, AttendanceService>();
        services.AddScoped<IAttendancePermissionTypeService, AttendancePermissionTypeService>();
        // Completion effects this module owns (ApplyLeaveDays, CreatePunch, Correct).
        services.AddEffectExecutorsFromAssembly(Assembly.GetExecutingAssembly());
        return services;
    }
}
