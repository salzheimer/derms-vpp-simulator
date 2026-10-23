namespace Rva.Host.Deployment;

/// <summary>
/// Identity of this deployment. One deployment serves exactly one utility, so everything that would collide
/// between two utilities sharing infrastructure is configured here (see PHASE_1_PLAN.md, cross-cutting decisions).
/// </summary>
internal sealed class DeploymentOptions
{
    public const string SectionName = "Rva:Deployment";

    /// <summary>Short code for the utility this deployment serves, e.g. <c>pge</c>.</summary>
    public string UtilityCode { get; init; } = "";

    /// <summary>Prefix for every Redis key this deployment writes, e.g. <c>rva:pge:</c>.</summary>
    public string RedisKeyPrefix { get; init; } = "";

    /// <summary>Topic level under which every MQTT topic this deployment uses lives, e.g. <c>rva/pge</c>.</summary>
    public string MqttTopicRoot { get; init; } = "";
}

/// <summary>The <c>ConnectionStrings</c> section. Database names are part of each connection string.</summary>
internal sealed class ConnectionStringOptions
{
    public const string SectionName = "ConnectionStrings";

    /// <summary>The <c>rva_core</c> PostgreSQL database (topology and simulation schemas).</summary>
    public string Core { get; init; } = "";

    /// <summary>The <c>rva_telemetry</c> TimescaleDB database.</summary>
    public string Telemetry { get; init; } = "";

    public string Redis { get; init; } = "";
}
