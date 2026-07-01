using Mitmi.Application.Diagnostics;

namespace Mitmi.Application.Configuration;

public sealed class ConfigurationValidationResult
{
    public ConfigurationValidationResult(
        RuntimeConfiguration? runtimeConfiguration,
        IReadOnlyList<ConfigurationIssue> issues)
    {
        RuntimeConfiguration = runtimeConfiguration;
        Issues = issues;
    }

    public RuntimeConfiguration? RuntimeConfiguration { get; }

    public IReadOnlyList<ConfigurationIssue> Issues { get; }

    public bool HasErrors => Issues.Any(issue => issue.Severity == ConfigurationIssueSeverity.Error);

    public IReadOnlyList<ConfigurationIssue> Errors =>
        Issues.Where(issue => issue.Severity == ConfigurationIssueSeverity.Error).ToArray();

    public IReadOnlyList<ConfigurationIssue> Warnings =>
        Issues.Where(issue => issue.Severity == ConfigurationIssueSeverity.Warning).ToArray();
}
