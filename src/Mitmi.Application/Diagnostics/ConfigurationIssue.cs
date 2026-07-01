namespace Mitmi.Application.Diagnostics;

public sealed record ConfigurationIssue(
    ConfigurationIssueSeverity Severity,
    string Code,
    string Message);
