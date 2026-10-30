using System.Net;

namespace Rva.Host.Tests;

/// <summary>The host answers its liveness probe, used by container and compose health checks.</summary>
public class HealthEndpointTests
{
    [Fact]
    public async Task Health_endpoint_reports_healthy()
    {
        await using var factory = HostFactory.Create();

        using var client = factory.CreateClient();
        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
