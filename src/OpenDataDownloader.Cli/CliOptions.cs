using System.Globalization;

namespace OpenDataDownloader.Cli;

/// <summary>
/// Parsed command-line options for the OpenDataDownloader CLI.
/// </summary>
internal sealed class CliOptions
{
    public const string CommandCkan = "ckan";
    public const string CommandNormattivaCollections = "normattiva-collections";
    public const string CommandNormattivaAll = "normattiva-all";
    public const string CommandNormattivaSearch = "normattiva-search";

    public string Command { get; private set; } = CommandCkan;
    public bool ShowHelp { get; private set; }

    public string OutputDirectory { get; private set; } = Path.Combine(Environment.CurrentDirectory, "OpenData");
    public string? Portal { get; private set; }
    public string? ApiKey { get; private set; }
    public bool IncludeDocuments { get; private set; }
    public bool DryRun { get; private set; }
    public int? Limit { get; private set; }
    public int DelayMs { get; private set; } = 200;
    public int MaxConcurrency { get; private set; } = 4;

    public string? Formato { get; private set; }
    public string? Export { get; private set; }
    public string? Query { get; private set; }
    public string? Email { get; private set; }
    public DateTime? FromDate { get; private set; }
    public DateTime? ToDate { get; private set; }
    public bool AllVariants { get; private set; }

    public static CliOptions Parse(string[] args)
    {
        var options = new CliOptions();
        var positionals = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (!arg.StartsWith('-'))
            {
                positionals.Add(arg);
                continue;
            }

            var (name, inlineValue) = SplitOption(arg);
            string? TakeValue()
            {
                if (inlineValue != null)
                {
                    return inlineValue;
                }

                if (i + 1 >= args.Length || args[i + 1].StartsWith("--"))
                {
                    throw new ArgumentException($"L'opzione --{name} richiede un valore.");
                }

                return args[++i];
            }

            switch (name)
            {
                case "help":
                case "h":
                    options.ShowHelp = true;
                    break;

                case "output":
                case "o":
                    options.OutputDirectory = TakeValue()!;
                    break;

                case "portal":
                case "p":
                    options.Portal = TakeValue();
                    break;

                case "api-key":
                    options.ApiKey = TakeValue();
                    break;

                case "include-documents":
                    options.IncludeDocuments = ParseBool(inlineValue, defaultValue: true);
                    break;

                case "dry-run":
                    options.DryRun = ParseBool(inlineValue, defaultValue: true);
                    break;

                case "limit":
                    options.Limit = ParsePositiveInt(TakeValue()!, name);
                    break;

                case "delay":
                    options.DelayMs = ParseNonNegativeInt(TakeValue()!, name);
                    break;

                case "concurrency":
                    options.MaxConcurrency = ParsePositiveInt(TakeValue()!, name);
                    break;

                case "formato":
                case "format":
                    options.Formato = TakeValue();
                    break;

                case "export":
                    options.Export = TakeValue();
                    break;

                case "query":
                case "q":
                    options.Query = TakeValue();
                    break;

                case "email":
                    options.Email = TakeValue();
                    break;

                case "from":
                    options.FromDate = ParseDate(TakeValue()!, name);
                    break;

                case "to":
                    options.ToDate = ParseDate(TakeValue()!, name);
                    break;

                case "variants":
                    options.AllVariants = ParseBool(inlineValue, defaultValue: true);
                    break;

                default:
                    throw new ArgumentException($"Opzione sconosciuta: --{name}");
            }
        }

        if (positionals.Count > 1)
        {
            throw new ArgumentException(
                $"Comando non valido: '{string.Join(' ', positionals)}'. Specifica un solo comando.");
        }

        if (positionals.Count == 1)
        {
            options.Command = positionals[0].ToLowerInvariant();
        }
        else if (args.Length == 0)
        {
            // Running without arguments is not a download request: show help.
            options.ShowHelp = true;
        }

        if (options.Command is not (CommandCkan
            or CommandNormattivaCollections
            or CommandNormattivaAll
            or CommandNormattivaSearch))
        {
            throw new ArgumentException($"Comando sconosciuto: '{options.Command}'.");
        }

        if (options.Limit is <= 0)
        {
            options.Limit = null;
        }

        return options;
    }

    private static (string Name, string? Value) SplitOption(string arg)
    {
        var trimmed = arg.TrimStart('-');
        var separator = trimmed.IndexOf('=');
        if (separator < 0)
        {
            return (trimmed.ToLowerInvariant(), null);
        }

        return (trimmed[..separator].ToLowerInvariant(), trimmed[(separator + 1)..]);
    }

    private static bool ParseBool(string? value, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("1", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || value.Equals("si", StringComparison.OrdinalIgnoreCase);
    }

    private static int ParsePositiveInt(string value, string name)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) || result <= 0)
        {
            throw new ArgumentException($"--{name} deve essere un intero positivo (ricevuto: '{value}').");
        }

        return result;
    }

    private static int ParseNonNegativeInt(string value, string name)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) || result < 0)
        {
            throw new ArgumentException($"--{name} deve essere un intero non negativo (ricevuto: '{value}').");
        }

        return result;
    }

    private static DateTime ParseDate(string value, string name)
    {
        if (DateTime.TryParseExact(
                value,
                new[] { "yyyy-MM-dd", "yyyy/MM/dd", "dd/MM/yyyy" },
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            return date;
        }

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            return date;
        }

        throw new ArgumentException($"--{name} deve essere una data valida (es. 2024-12-31).");
    }
}
