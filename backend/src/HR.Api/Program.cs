using HR.Api.Middleware;
using HR.Api.Services;
using HR.Application;
using HR.Application.Common.Interfaces;
using HR.Infrastructure;
using HR.Modules.Core;
using HR.Modules.Employees;
using HR.Modules.Identity;
using HR.Modules.Settings;
using HR.Modules.Tasks;
using HR.Modules.Tenancy;
using HR.Modules.ESS;
using HR.Modules.Workflows;
using HR.Modules.Attendance;
using HR.Modules.Payroll;
using HR.Modules.Expenses;
using HR.Modules.Loans;
using HR.Modules.Documents;
using HR.Modules.Reports;
using HR.Modules.Dashboards;
using HR.Modules.Notifications;
using HR.Modules.Platform;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;
using Hangfire;
using Hangfire.PostgreSql;

var builder = WebApplication.CreateBuilder(args);

// Authentication
var jwtKey = builder.Configuration["Jwt:Key"] ?? "super-secret-key-for-development-only-min-32-chars!!";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "HR.Api";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "HR.Client";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

// Core services
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

// Layers
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Modules
builder.Services.AddCoreModule();
builder.Services.AddTenancyModule();
builder.Services.AddIdentityModule(builder.Configuration);
builder.Services.AddEmployeesModule();
builder.Services.AddTasksModule();
builder.Services.AddSettingsModule();
builder.Services.AddESSModule();
builder.Services.AddWorkflowsModule();
builder.Services.AddAttendanceModule();
builder.Services.AddPayrollModule();
builder.Services.AddExpensesModule();
builder.Services.AddLoansModule();
builder.Services.AddDocumentsModule();
builder.Services.AddReportsModule();
builder.Services.AddDashboardsModule();
builder.Services.AddNotificationsModule();
builder.Services.AddPlatformModule();

// Background: scan employee documents against notification rules and create expiry reminders.
builder.Services.AddHostedService<HR.Api.Services.DocumentExpiryHostedService>();
builder.Services.AddHostedService<HR.Api.Services.ReportScheduleHostedService>();
builder.Services.AddHostedService<HR.Api.Services.EmailDeliveryHostedService>();
builder.Services.AddHostedService<HR.Api.Services.ScheduledEffectHostedService>();

// Optional durable background execution for large payroll runs. Off by default — the in-process
// scheduler (registered in AddInfrastructure) handles execution inline unless this is enabled.
if (builder.Configuration.GetValue<bool>("Hangfire:Enabled"))
{
    builder.Services.AddHangfire(cfg => cfg
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UsePostgreSqlStorage(o => o.UseNpgsqlConnection(builder.Configuration.GetConnectionString("DefaultConnection"))));
    builder.Services.AddHangfireServer();
}

// Controllers
builder.Services.AddControllers()
    .AddApplicationPart(typeof(HR.Modules.Identity.Controllers.AuthController).Assembly)
    .AddApplicationPart(typeof(HR.Modules.Core.Controllers.DepartmentsController).Assembly)
    .AddApplicationPart(typeof(HR.Modules.Employees.Controllers.EmployeesController).Assembly)
    .AddApplicationPart(typeof(HR.Modules.Tasks.Controllers.TasksController).Assembly)
    .AddApplicationPart(typeof(HR.Modules.Settings.Controllers.SettingsController).Assembly)
    .AddApplicationPart(typeof(HR.Modules.Attendance.Controllers.AttendanceController).Assembly)
    .AddApplicationPart(typeof(HR.Modules.Platform.Controllers.MetadataController).Assembly)
    .AddApplicationPart(typeof(HR.Modules.Workflows.Controllers.WorkflowDefinitionsController).Assembly)
    .AddApplicationPart(typeof(HR.Modules.Payroll.Controllers.PayrollController).Assembly);

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? new[] { "http://localhost:3000" })
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "HR Cloud API", Version = "v1" });
    // Modular codebase: several modules legitimately share short DTO names (e.g. two
    // WorkflowDefinitionDto types). Use the full type name as the schema id so Swagger generation
    // never collides.
    c.CustomSchemaIds(t => t.FullName?.Replace("+", "."));
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// Middleware
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
