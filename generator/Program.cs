using System.Diagnostics;
using System.Globalization;
using PokerDrills.Engine;

// Usage: dotnet run --project generator -- --content ./content --out ./web/public/drills.json --per-rule 200 --seed 42
// Exit codes: 0 ok, 1 rule conflicts (nothing written), 2 bad arguments or invalid content.

CliOptions cli;
try
{
    cli = CliOptions.Parse(args);
}
catch (ArgumentException e)
{
    Console.Error.WriteLine(e.Message);
    Console.Error.WriteLine(CliOptions.Usage);
    return 2;
}

ContentSet content;
try
{
    content = ContentLoader.Load(cli.Content);
}
catch (ContentException e)
{
    Console.Error.WriteLine(e.Message);
    return 2;
}

var stopwatch = Stopwatch.StartNew();
var result = DrillGenerator.Generate(content, new GeneratorOptions
{
    Seed = cli.Seed,
    PerRule = cli.PerRule,
    MaxAttempts = cli.MaxAttempts,
});

Report.PrintTable(result.Reports);

if (result.Conflicts.Count > 0)
{
    Report.PrintConflicts(result.Conflicts);
    Console.Error.WriteLine();
    Console.Error.WriteLine($"{result.Conflicts.Count} conflicting rule pair(s). Fix the rules (e.g. add boardExclude/strength limits). {cli.Out} was NOT written.");
    return 1;
}

var outPath = Path.GetFullPath(cli.Out);
Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
File.WriteAllText(outPath, DrillJson.Serialize(result.File));

var warnings = result.Reports.Sum(r => r.Warnings.Count);
Console.WriteLine();
Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
    $"Wrote {result.File.Drills.Count} drills to {outPath} in {stopwatch.Elapsed.TotalSeconds:0.0}s. Conflicts: 0. Warnings: {warnings}."));
return 0;

internal sealed record CliOptions(string Content, string Out, int PerRule, ulong Seed, int MaxAttempts)
{
    public const string Usage =
        "Usage: dotnet run --project generator -- --content <dir> --out <file> [--per-rule 200] [--seed 42] [--max-attempts 200000]";

    public static CliOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"Unexpected argument '{arg}'");
            var eq = arg.IndexOf('=');
            if (eq > 0)
            {
                values[arg[2..eq]] = arg[(eq + 1)..];
            }
            else
            {
                if (i + 1 >= args.Length) throw new ArgumentException($"Missing value for {arg}");
                values[arg[2..]] = args[++i];
            }
        }

        var known = new[] { "content", "out", "per-rule", "seed", "max-attempts" };
        var unknown = values.Keys.Except(known).ToList();
        if (unknown.Count > 0) throw new ArgumentException($"Unknown option(s): {string.Join(", ", unknown.Select(u => "--" + u))}");

        string Required(string key) =>
            values.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : throw new ArgumentException($"--{key} is required");

        T Number<T>(string key, T fallback, T min) where T : struct, IComparable<T>, IParsable<T>
        {
            if (!values.TryGetValue(key, out var text)) return fallback;
            if (!T.TryParse(text, CultureInfo.InvariantCulture, out var n) || n.CompareTo(min) < 0)
                throw new ArgumentException($"--{key} must be a number >= {min}, got '{text}'");
            return n;
        }

        return new CliOptions(
            Required("content"),
            Required("out"),
            Number("per-rule", 200, 1),
            Number("seed", 42UL, 0UL),
            Number("max-attempts", 200_000, 1));
    }
}

internal static class Report
{
    public static void PrintTable(IReadOnlyList<RuleReport> reports)
    {
        var width = Math.Max(4, reports.Max(r => r.RuleId.Length));
        var header = $"{"Rule".PadRight(width)}  {"Kind",-8}  {"Drills",6}  {"Attempts",8}  {"Conflicts",9}  Warnings";
        Console.WriteLine(header);
        Console.WriteLine(new string('-', header.Length + 12));
        foreach (var r in reports)
        {
            var warnings = r.Warnings.Count == 0 ? "-" : "WARNING: " + string.Join("; ", r.Warnings);
            var conflicts = r.Kind == DrillKinds.Identify ? "-" : r.Conflicts.ToString(CultureInfo.InvariantCulture);
            Console.WriteLine($"{r.RuleId.PadRight(width)}  {r.Kind,-8}  {r.Produced,6}  {r.Attempts,8}  {conflicts,9}  {warnings}");
        }
    }

    public static void PrintConflicts(IReadOnlyList<Conflict> conflicts)
    {
        Console.Error.WriteLine();
        foreach (var c in conflicts)
        {
            var d = c.Example;
            Console.Error.WriteLine($"CONFLICT: {c.RuleId} ({c.Correct}) vs {c.OtherRuleId} ({c.OtherCorrect}) on {c.Count} drill(s)");
            Console.Error.WriteLine($"  example {d.Id}: {d.VillainType}, {d.Line}, hero {string.Join(" ", d.HeroCards)}, " +
                $"board {string.Join(" ", d.Board)}, {d.Facts?.HandClass ?? d.Facts?.HandGroup}" +
                $", draws [{string.Join(", ", d.Facts?.Draws ?? [])}], board [{string.Join(", ", d.Facts?.BoardFlags ?? [])}]");
        }
    }
}
