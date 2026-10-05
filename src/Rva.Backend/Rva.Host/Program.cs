// Composition root. Module registration (Add<Area>Module / Map<Area>Endpoints), Serilog, and the
// integration-event publisher are wired in a later Phase 1 Step 0 change.
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.Run();
