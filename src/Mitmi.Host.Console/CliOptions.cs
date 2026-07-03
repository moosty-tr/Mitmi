namespace Mitmi.Host.Console;

internal sealed class CliOptions
{
    private CliOptions(
        string? configurationPath,
        string? diagnosticsBundlePath,
        bool hasExplicitConfigurationPath,
        bool initConfig,
        bool validateConfig,
        bool showHelp,
        IReadOnlyList<string> errors)
    {
        ConfigurationPath = configurationPath;
        DiagnosticsBundlePath = diagnosticsBundlePath;
        HasExplicitConfigurationPath = hasExplicitConfigurationPath;
        InitConfig = initConfig;
        ValidateConfig = validateConfig;
        ShowHelp = showHelp;
        Errors = errors;
    }

    public string? ConfigurationPath { get; }

    public string? DiagnosticsBundlePath { get; }

    public bool HasExplicitConfigurationPath { get; }

    public bool InitConfig { get; }

    public bool ValidateConfig { get; }

    public bool ShowHelp { get; }

    public IReadOnlyList<string> Errors { get; }

    public static CliOptions Parse(IReadOnlyList<string> args)
    {
        var errors = new List<string>();
        string? configurationPath = null;
        string? diagnosticsBundlePath = null;
        var hasExplicitConfigurationPath = false;
        var initConfig = false;
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

                case "--init-config":
                    initConfig = true;
                    break;

                case "--bundle-diagnostics":
                    if (index + 1 >= args.Count)
                    {
                        errors.Add("--bundle-diagnostics requires a path value.");
                        break;
                    }

                    diagnosticsBundlePath = args[++index];
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

        if (initConfig && validateConfig)
        {
            errors.Add("--init-config cannot be combined with --validate-config.");
        }

        if (initConfig && diagnosticsBundlePath is not null)
        {
            errors.Add("--init-config cannot be combined with --bundle-diagnostics.");
        }

        if (validateConfig && diagnosticsBundlePath is not null)
        {
            errors.Add("--validate-config cannot be combined with --bundle-diagnostics.");
        }

        return new CliOptions(
            configurationPath,
            diagnosticsBundlePath,
            hasExplicitConfigurationPath,
            initConfig,
            validateConfig,
            showHelp,
            errors);
    }
}
