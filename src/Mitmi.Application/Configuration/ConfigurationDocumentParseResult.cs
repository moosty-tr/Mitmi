using Mitmi.Application.Diagnostics;

namespace Mitmi.Application.Configuration;

public sealed class ConfigurationDocumentParseResult
{
    public ConfigurationDocumentParseResult(
        MitmiConfigurationDocument? document,
        IReadOnlyList<ConfigurationIssue> issues)
    {
        Document = document;
        Issues = issues;
    }

    public MitmiConfigurationDocument? Document { get; }

    public IReadOnlyList<ConfigurationIssue> Issues { get; }

    public bool HasErrors => Issues.Any(issue => issue.Severity == ConfigurationIssueSeverity.Error);
}
