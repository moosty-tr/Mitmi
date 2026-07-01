namespace Mitmi.Host.Console;

internal sealed class CliOptions
{
    private CliOptions(
        string? configurationPath,
        bool validateConfig,
        bool showHelp,
        IReadOnlyList<string> errors)
    {
        ConfigurationPath = configurationPath;
        ValidateConfig = validateConfig;
        ShowHelp = showHelp;
        Errors = errors;
    }

    public string? ConfigurationPath { get; }

    public bool ValidateConfig { get; }

    public bool ShowHelp { get; }

    public IReadOnlyList<string> Errors { get; }

    public static CliOptions Parse(IReadOnlyList<string> args)
    {
        var errors = new List<string>();
        string? configurationPath = null;
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
                    break;

                default:
                    errors.Add($"Unknown argument '{arg}'.");
                    break;
            }
        }

        if (!showHelp && string.IsNullOrWhiteSpace(configurationPath))
        {
            errors.Add("Missing required --config <path> argument.");
        }

        return new CliOptions(configurationPath, validateConfig, showHelp, errors);
    }
}
