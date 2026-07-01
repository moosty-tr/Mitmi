using System.Text.Json;
using System.Text.Json.Serialization;
using Mitmi.Application.Diagnostics;

namespace Mitmi.Application.Configuration;

public static class ConfigurationDocumentParser
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static ConfigurationDocumentParseResult Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        try
        {
            var document = JsonSerializer.Deserialize<MitmiConfigurationDocument>(json, SerializerOptions);
            if (document is null)
            {
                return Failed("Configuration JSON did not contain an object.");
            }

            return new ConfigurationDocumentParseResult(document, Array.Empty<ConfigurationIssue>());
        }
        catch (JsonException exception)
        {
            return Failed($"Configuration JSON is invalid: {exception.Message}");
        }
    }

    private static ConfigurationDocumentParseResult Failed(string message)
    {
        return new ConfigurationDocumentParseResult(
            document: null,
            issues:
            [
                new ConfigurationIssue(
                    ConfigurationIssueSeverity.Error,
                    ConfigurationIssueCodes.InvalidJson,
                    message)
            ]);
    }
}
