using Rva.Host.Deployment;
using Rva.Host.Errors;
using Rva.Host.Health;
using Rva.Host.IntegrationEvents;
using Rva.Modules.GridTopology;
using Rva.Modules.Simulation;
using Rva.Modules.Telemetry;
using Serilog;
using Serilog.Events;

// Composition root: host-wide services first, then each module's Add<Area>Module / Map<Area>Endpoints and nothing
// else module-specific (.ai/rules/csharp-monolith.md).
var builder = WebApplication.CreateBuilder(args);

// Serilog is configured here only; modules log through ILogger<T>. Settings come from the "Serilog" section.
// preserveStaticLogger: the host owns its logger and leaves the static Serilog.Log alone, so hosts in one process (the
// in-memory test hosts) never log into, or on disposal close, each other's pipeline.
builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext(),
    preserveStaticLogger: true);

builder.Services.AddDeploymentSettings(builder.Configuration);
builder.Services.AddIntegrationEvents();
builder.Services.AddRvaProblemDetails();

// Modules register readiness checks tagged "ready"; /health runs none of them (HealthEndpoints).
builder.Services.AddHealthChecks();

builder.Services
    .AddGridTopologyModule(builder.Configuration)
    .AddTelemetryModule(builder.Configuration)
    .AddSimulationModule(builder.Configuration);

var app = builder.Build();

// The exception that caused a 5xx is logged once at Error by whoever handled it; the request summary stays below Error
// so a failed request is not counted twice.
// The middleware would otherwise write to the static Serilog.Log, which this host does not set.
app.UseSerilogRequestLogging(options =>
{
    options.Logger = app.Services.GetRequiredService<Serilog.ILogger>();
    options.GetLevel = (context, _, exception) =>
        exception is not null ? LogEventLevel.Error
        : context.Response.StatusCode >= StatusCodes.Status500InternalServerError ? LogEventLevel.Warning
        : LogEventLevel.Information;
});

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapRvaHealthEndpoints();

app.MapGridTopologyEndpoints();
app.MapTelemetryEndpoints();
app.MapSimulationEndpoints();

app.Run();
