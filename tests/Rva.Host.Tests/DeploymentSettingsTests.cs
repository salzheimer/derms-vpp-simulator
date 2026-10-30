using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Rva.Host.Deployment;

namespace Rva.Host.Tests;

/// <summary>Required deployment settings and connection strings are validated when the host starts.</summary>
public class DeploymentSettingsTests
{
    [Fact]
    public async Task Host_starts_with_valid_settings()
    {
        await using var factory = HostFactory.Create();

        using var client = factory.CreateClient();
        var response = await client.GetAsync("/", TestContext.Current.CancellationToken);

        // No module maps an endpoint yet, so any request outside /health is a 404 from a running host.
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("Rva:Deployment:UtilityCode", "", "Rva:Deployment:UtilityCode is required.")]
    [InlineData("Rva:Deployment:UtilityCode", "PGE Utility", "Rva:Deployment:UtilityCode must be lowercase")]
    [InlineData("Rva:Deployment:RedisKeyPrefix", "", "Rva:Deployment:RedisKeyPrefix is required.")]
    [InlineData("Rva:Deployment:RedisKeyPrefix", "rva:dev", "Rva:Deployment:RedisKeyPrefix must contain no whitespace and end with ':'")]
    [InlineData("Rva:Deployment:MqttTopicRoot", "", "Rva:Deployment:MqttTopicRoot is required.")]
    [InlineData("Rva:Deployment:MqttTopicRoot", "rva/#", "Rva:Deployment:MqttTopicRoot must be one or more '/'-separated topic levels")]
    [InlineData("Rva:Deployment:MqttTopicRoot", "/rva/dev", "Rva:Deployment:MqttTopicRoot must be one or more '/'-separated topic levels")]
    [InlineData("Rva:Deployment:MqttTopicRoot", "rva//dev", "Rva:Deployment:MqttTopicRoot must be one or more '/'-separated topic levels")]
    [InlineData("ConnectionStrings:Core", "", "ConnectionStrings:Core is required.")]
    [InlineData("ConnectionStrings:Telemetry", "", "ConnectionStrings:Telemetry is required.")]
    [InlineData("ConnectionStrings:Redis", "", "ConnectionStrings:Redis is required.")]
    public async Task Missing_or_invalid_setting_fails_startup_naming_the_key(string key, string value, string expectedMessage)
    {
        await using var factory = HostFactory.Create(new Dictionary<string, string?> { [key] = value });

        var error = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        Assert.Contains(error.Failures, f => f.StartsWith(expectedMessage, StringComparison.Ordinal));
    }

    [Fact]
    public async Task All_failures_are_reported_together()
    {
        await using var factory = HostFactory.Create(new Dictionary<string, string?>
        {
            ["Rva:Deployment:UtilityCode"] = "",
            ["Rva:Deployment:MqttTopicRoot"] = "",
        });

        var error = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        Assert.Contains("Rva:Deployment:UtilityCode is required.", error.Failures);
        Assert.Contains("Rva:Deployment:MqttTopicRoot is required.", error.Failures);
    }

    [Fact]
    public async Task Settings_are_bound_from_configuration()
    {
        await using var factory = HostFactory.Create(new Dictionary<string, string?>
        {
            ["Rva:Deployment:UtilityCode"] = "pge",
            ["Rva:Deployment:RedisKeyPrefix"] = "rva:pge:",
            ["Rva:Deployment:MqttTopicRoot"] = "rva/pge",
            ["ConnectionStrings:Core"] = "Host=core-db;Database=rva_core_pge",
            ["ConnectionStrings:Telemetry"] = "Host=telemetry-db;Database=rva_telemetry_pge",
            ["ConnectionStrings:Redis"] = "redis-pge:6379",
        });

        var deployment = factory.Services.GetRequiredService<IOptions<DeploymentOptions>>().Value;
        var connectionStrings = factory.Services.GetRequiredService<IOptions<ConnectionStringOptions>>().Value;

        Assert.Equal(("pge", "rva:pge:", "rva/pge"), (deployment.UtilityCode, deployment.RedisKeyPrefix, deployment.MqttTopicRoot));
        Assert.Equal(("Host=core-db;Database=rva_core_pge", "Host=telemetry-db;Database=rva_telemetry_pge", "redis-pge:6379"),
            (connectionStrings.Core, connectionStrings.Telemetry, connectionStrings.Redis));
    }
}
