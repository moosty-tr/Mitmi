namespace Mitmi.Host.Console;

internal sealed class CliOptions
{
    private CliOptions(
        string? configurationPath,
        bool hasExplicitConfigurationPath,
        bool validateConfig,
        bool showHelp,
        IReadOnlyList<string> errors)
    {
        ConfigurationPath = configurationPath;
        HasExplicitConfigurationPath = hasExplicitConfigurationPath;
        ValidateConfig = validateConfig;
        ShowHelp = showHelp;
        Errors = errors;
    }

    public string? ConfigurationPath { get; }

    public bool HasExplicitConfigurationPath { get; }

    public bool ValidateConfig { get; }

    public bool ShowHelp { get; }

    public IReadOnlyList<string> Errors { get; }

    public static CliOptions Parse(IReadOnlyList<string> args)
    {
        var errors = new List<string>();
        string? configurationPath = null;
        var hasExplicitConfigurationPath = false;
        var validateConfig = false;
        var showHelp = false;

        for (var index = 0; index < args.Count; index++)
        {
            var arg = args[index];
            switch (arg)
            {
                case "--help":
                case "-h":
                    showHelp = true;
                    break;

                case "--validate-config":
                    validateConfig = true;
                    break;

                case "--config":
                    if (index + 1 >= args.Count)
                    {
                        errors.Add("--config requires a path value.");
                        break;
                    }

                    configurationPath = args[++index];
                    hasExplicitConfigurationPath = true;
                    break;

                default:
                    errors.Add($"Unknown argument '{arg}'.");
                    break;
            }
        }

        return new CliOptions(configurationPath, hasExplicitConfigurationPath, validateConfig, showHelp, errors);
    }
}
