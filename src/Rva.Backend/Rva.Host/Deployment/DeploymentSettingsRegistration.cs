using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Rva.Host.Deployment;

internal static partial class DeploymentSettingsRegistration
{
    /// <summary>Binds the deployment settings and connection strings, and fails startup if any is missing or invalid.</summary>
    public static IServiceCollection AddDeploymentSettings(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DeploymentOptions>().Bind(configuration.GetSection(DeploymentOptions.SectionName)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<DeploymentOptions>, DeploymentOptionsValidator>();

        services.AddOptions<ConnectionStringOptions>().Bind(configuration.GetSection(ConnectionStringOptions.SectionName)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<ConnectionStringOptions>, ConnectionStringOptionsValidator>();
        return services;
    }

    // Lowercase letters and digits, optionally separated by single hyphens.
    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    internal static partial Regex UtilityCodePattern();

    private static string Key(string section, string name) => $"{section}:{name}";

    private sealed class DeploymentOptionsValidator : IValidateOptions<DeploymentOptions>
    {
        public ValidateOptionsResult Validate(string? name, DeploymentOptions options)
        {
            var errors = new List<string>();
            string KeyOf(string property) => Key(DeploymentOptions.SectionName, property);

            if (string.IsNullOrWhiteSpace(options.UtilityCode))
                errors.Add($"{KeyOf(nameof(options.UtilityCode))} is required.");
            else if (!UtilityCodePattern().IsMatch(options.UtilityCode))
                errors.Add($"{KeyOf(nameof(options.UtilityCode))} must be lowercase letters and digits, optionally separated by hyphens (got '{options.UtilityCode}').");

            if (string.IsNullOrWhiteSpace(options.RedisKeyPrefix))
                errors.Add($"{KeyOf(nameof(options.RedisKeyPrefix))} is required.");
            else if (options.RedisKeyPrefix.Any(char.IsWhiteSpace) || !options.RedisKeyPrefix.EndsWith(':'))
                errors.Add($"{KeyOf(nameof(options.RedisKeyPrefix))} must contain no whitespace and end with ':' (got '{options.RedisKeyPrefix}').");

            if (string.IsNullOrWhiteSpace(options.MqttTopicRoot))
                errors.Add($"{KeyOf(nameof(options.MqttTopicRoot))} is required.");
            else if (options.MqttTopicRoot.Any(c => c is '+' or '#' || char.IsWhiteSpace(c))
                     || options.MqttTopicRoot.Split('/').Any(level => level.Length == 0))
                errors.Add($"{KeyOf(nameof(options.MqttTopicRoot))} must be one or more '/'-separated topic levels with no wildcards ('+', '#'), whitespace, or empty levels (got '{options.MqttTopicRoot}').");

            return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
        }
    }

    private sealed class ConnectionStringOptionsValidator : IValidateOptions<ConnectionStringOptions>
    {
        public ValidateOptionsResult Validate(string? name, ConnectionStringOptions options)
        {
            var missing = new (string Name, string Value)[]
                {
                    (nameof(options.Core), options.Core),
                    (nameof(options.Telemetry), options.Telemetry),
                    (nameof(options.Redis), options.Redis),
                }
                .Where(c => string.IsNullOrWhiteSpace(c.Value))
                .Select(c => $"{Key(ConnectionStringOptions.SectionName, c.Name)} is required.")
                .ToList();

            return missing.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(missing);
        }
    }
}
