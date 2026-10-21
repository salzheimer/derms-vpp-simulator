using Rva.Host.Deployment;
using Rva.Host.IntegrationEvents;
using Rva.Modules.GridTopology;
using Rva.Modules.Simulation;
using Rva.Modules.Telemetry;
using Serilog;

// Composition root: host-wide services first, then each module's Add<Area>Module / Map<Area>Endpoints and nothing
// else module-specific (.ai/rules/csharp-monolith.md).
var builder = WebApplication.CreateBuilder(args);

// Serilog is configured here only; modules log through ILogger<T>. Settings come from the "Serilog" section.
builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

builder.Services.AddDeploymentSettings(builder.Configuration);
builder.Services.AddIntegrationEvents();

builder.Services
    .AddGridTopologyModule(builder.Configuration)
    .AddTelemetryModule(builder.Configuration)
    .AddSimulationModule(builder.Configuration);

var app = builder.Build();

app.UseSerilogRequestLogging();

app.MapGridTopologyEndpoints();
app.MapTelemetryEndpoints();
app.MapSimulationEndpoints();

app.Run();
