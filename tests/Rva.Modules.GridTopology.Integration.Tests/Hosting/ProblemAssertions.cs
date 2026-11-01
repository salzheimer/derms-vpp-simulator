using System.Net;
using System.Text.Json;
using Rva.Modules.GridTopology.Integration.Tests.Infrastructure;

namespace Rva.Modules.GridTopology.Integration.Tests.Hosting;

/// <summary>The checks every unexpected-failure test makes (plan p1-s01 section 4, U2 and U3).</summary>
internal static class ProblemAssertions
{
    // Nothing about the database or the code may reach a client.
    private static readonly string[] Leaks = ["Npgsql", "Postgres", "SQLSTATE", "Exception", "Host=", "Password", "topology.", "ck_", "   at "];

    /// <summary>U2: problem+json with the status, code, and a traceId, and nothing internal. Returns the traceId.</summary>
    public static async Task<string> AssertSanitizedProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code, params string[] alsoForbidden)
    {
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        foreach (var leak in Leaks.Concat(alsoForbidden))
            Assert.DoesNotContain(leak, body, StringComparison.OrdinalIgnoreCase);

        using var json = JsonDocument.Parse(body);
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
        var traceId = json.RootElement.GetProperty("traceId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(traceId));
        return traceId!;
    }

    /// <summary>U3: exactly one Error-level entry, carrying the exception and the request's trace id. Returns it.</summary>
    public static Serilog.Events.LogEvent AssertOneErrorLogged(SerilogCapture logs, string traceId)
    {
        var error = Assert.Single(logs.Errors);
        Assert.NotNull(error.Exception);
        Assert.NotNull(error.TraceId);
        Assert.Contains(error.TraceId.Value.ToHexString(), traceId, StringComparison.Ordinal);
        return error;
    }
}
