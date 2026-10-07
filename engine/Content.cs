using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PokerDrills.Engine;

public sealed class ContentException(IReadOnlyList<string> errors)
    : Exception("Invalid content:" + Environment.NewLine + string.Join(Environment.NewLine, errors.Select(e => "  - " + e)))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

public sealed record ContentSet(IReadOnlyList<PlayerType> Types, IReadOnlyList<Rule> Rules, PreflopRanges Ranges);

/// <summary>
/// Loads and validates /content. Validation is strict (unknown properties, unknown names,
/// impossible answers) because the files are hand-edited by a coach.
/// </summary>
public static partial class ContentLoader
{
    public const string IdentifyRulePrefix = "identify-";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex IdPattern();

    public static ContentSet Load(string contentDir)
    {
        var typesPath = Path.Combine(contentDir, "types.json");
        var rangesPath = Path.Combine(contentDir, "ranges.json");
        var rulesDir = Path.Combine(contentDir, "rules");
        foreach (var file in new[] { typesPath, rangesPath })
        {
            if (!File.Exists(file)) throw new ContentException([$"Missing {file}"]);
        }
        if (!Directory.Exists(rulesDir)) throw new ContentException([$"Missing {rulesDir}"]);

        var types = ParseTypes(File.ReadAllText(typesPath));
        var ranges = ParseRanges(File.ReadAllText(rangesPath));

        var errors = new List<string>();
        var rules = new List<Rule>();
        foreach (var file in Directory.GetFiles(rulesDir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            try
            {
                rules.Add(ParseRule(File.ReadAllText(file), types, Path.GetFileNameWithoutExtension(file)));
            }
            catch (ContentException e)
            {
                errors.AddRange(e.Errors.Select(x => $"{Path.GetFileName(file)}: {x}"));
            }
        }

        if (rules.Count == 0 && errors.Count == 0) errors.Add($"No rule files found in {rulesDir}");
        errors.AddRange(rules.GroupBy(r => r.Id).Where(g => g.Count() > 1).Select(g => $"Duplicate rule id '{g.Key}'"));
        if (errors.Count > 0) throw new ContentException(errors);

        return new ContentSet(types, rules, ranges);
    }

    /// <summary>Seats hero can open from in the postflop lines, and openers hero can face from the BB.</summary>
    private static readonly IReadOnlyList<Position> OpenSeats =
        FlopVillainChecksLine.HeroSeats.Union(SrpToRiverLine.HeroSeats).ToList();

    private static readonly IReadOnlyList<Position> BigBlindCallSeats = FacingFlopCbetLine.VillainSeats;

    public static PreflopRanges ParseRanges(string json)
    {
        var dto = Deserialize<RangesDto>(json);
        var errors = new List<string>();
        var open = ParseSeatRanges(dto.Open, "open", OpenSeats, errors);
        var call = ParseSeatRanges(dto.BigBlindCall, "bigBlindCall", BigBlindCallSeats, errors);
        if (errors.Count > 0) throw new ContentException(errors.Select(e => "ranges.json: " + e).ToList());
        return new PreflopRanges(open, call);
    }

    private static Dictionary<Position, HandRange> ParseSeatRanges(
        Dictionary<string, string>? raw, string field, IEnumerable<Position> required, List<string> errors)
    {
        var result = new Dictionary<Position, HandRange>();
        var seen = new HashSet<Position>();
        foreach (var (key, text) in raw ?? [])
        {
            if (!TryParseName<Position>(key, out var seat))
            {
                errors.Add($"{field}: unknown position '{key}'");
                continue;
            }
            seen.Add(seat);
            try
            {
                result[seat] = HandRange.Parse(text);
            }
            catch (FormatException e)
            {
                errors.Add($"{field}.{key}: {e.Message}");
            }
        }
        errors.AddRange(required.Where(s => !seen.Contains(s)).Select(s => $"{field}: missing a range for {s}"));
        return result;
    }

    public static IReadOnlyList<PlayerType> ParseTypes(string json)
    {
        var dto = Deserialize<TypesFileDto>(json);
        if (dto.Types is not { Count: > 0 }) throw new ContentException(["types.json: 'types' must list at least one type"]);

        var errors = new List<string>();
        var types = new List<PlayerType>();
        for (var i = 0; i < dto.Types.Count; i++)
        {
            var t = dto.Types[i];
            if (string.IsNullOrWhiteSpace(t.Id) || !t.Id.All(char.IsAsciiLetterOrDigit))
            {
                errors.Add($"types[{i}]: id is required and must be letters/digits only");
                continue;
            }

            var where = $"type '{t.Id}'";
            var ranges = new Dictionary<string, StatRange>();
            foreach (var key in StatKeys.All)
            {
                if (t.Ranges is null || !t.Ranges.TryGetValue(key, out var r) || r is not { Length: 2 })
                {
                    errors.Add($"{where}: ranges.{key} must be [min, max]");
                    continue;
                }
                var range = new StatRange(r[0], r[1]);
                var (lo, hi) = key == StatKeys.Af ? StatSampler.TenthBounds(range) : StatSampler.IntBounds(range);
                if (r[0] > r[1] || lo > hi)
                {
                    errors.Add($"{where}: ranges.{key} [{r[0]}, {r[1]}] contains no valid value");
                    continue;
                }
                ranges[key] = range;
            }
            errors.AddRange((t.Ranges?.Keys ?? Enumerable.Empty<string>()).Where(k => !StatKeys.All.Contains(k)).Select(k => $"{where}: unknown stat '{k}'"));

            if (t.TableShare is < 0) errors.Add($"{where}: tableShare must be 0 or more");
            types.Add(new PlayerType(t.Id, string.IsNullOrWhiteSpace(t.Name) ? t.Id : t.Name, t.Description ?? "", t.Placeholder,
                ranges, Math.Max(0, t.TableShare ?? 1), t.ShortName));
        }

        errors.AddRange(types.GroupBy(t => t.Id).Where(g => g.Count() > 1).Select(g => $"Duplicate type id '{g.Key}'"));
        if (types.Count > 0 && types.All(t => t.TableShare == 0)) errors.Add("At least one type needs a tableShare above 0");
        if (errors.Count > 0) throw new ContentException(errors);
        return types;
    }

    public static Rule ParseRule(string json, IReadOnlyList<PlayerType> types, string? expectedId = null)
    {
        var dto = Deserialize<RuleDto>(json);
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(dto.Id) || !IdPattern().IsMatch(dto.Id))
            errors.Add("id is required and must be kebab-case (a-z, 0-9, '-')");
        else if (dto.Id.StartsWith(IdentifyRulePrefix, StringComparison.Ordinal))
            errors.Add($"id must not start with '{IdentifyRulePrefix}' (reserved for identification drills)");
        else if (expectedId is not null && dto.Id != expectedId)
            errors.Add($"id '{dto.Id}' must match the file name '{expectedId}'");

        if (types.All(t => t.Id != dto.VillainType))
            errors.Add($"villainType '{dto.VillainType}' is not one of: {string.Join(", ", types.Select(t => t.Id))}");

        LineTemplate? template = null;
        if (TryParseName<LineId>(dto.Line, out var lineId)) template = LineTemplate.For(lineId);
        else errors.Add($"line '{dto.Line}' is not one of: {string.Join(", ", Enum.GetNames<LineId>())}");

        StrengthRange? hero = null;
        List<HandGroup>? groups = null;
        var draws = DrawRequirement.Any;
        BoardFlags require = BoardFlags.None, exclude = BoardFlags.None;

        if (template is { DecisionStreet: Street.Preflop })
        {
            if (dto.HeroGroup is not { Count: > 0 }) errors.Add("preflop rules need a non-empty heroGroup");
            else groups = ParseNames<HandGroup>(dto.HeroGroup, "heroGroup", errors);
            if (dto.Hero is not null) errors.Add("preflop rules use heroGroup, not hero");
            if (dto.Draws is not null) errors.Add("draws only applies to postflop rules");
            if (dto.BoardRequire is { Count: > 0 } || dto.BoardExclude is { Count: > 0 })
                errors.Add("boardRequire/boardExclude only apply to postflop rules");
        }
        else if (template is not null)
        {
            if (dto.HeroGroup is not null) errors.Add("postflop rules use hero, not heroGroup");
            if (dto.Hero is null)
            {
                errors.Add("postflop rules need hero.minStrength and hero.maxStrength");
            }
            else
            {
                var minOk = TryParseName<HandClass>(dto.Hero.MinStrength, out var min);
                var maxOk = TryParseName<HandClass>(dto.Hero.MaxStrength, out var max);
                if (!minOk) errors.Add($"hero.minStrength '{dto.Hero.MinStrength}' is not one of: {string.Join(", ", Enum.GetNames<HandClass>())}");
                if (!maxOk) errors.Add($"hero.maxStrength '{dto.Hero.MaxStrength}' is not one of: {string.Join(", ", Enum.GetNames<HandClass>())}");
                if (minOk && maxOk && min > max) errors.Add($"hero.minStrength {min} is stronger than maxStrength {max}");
                if (minOk && maxOk) hero = new StrengthRange(min, max);
            }

            if (dto.Draws is null) draws = DrawRequirement.Any;
            else if (string.Equals(dto.Draws, "none", StringComparison.OrdinalIgnoreCase)) draws = DrawRequirement.None;
            else errors.Add($"draws must be null or \"none\", got '{dto.Draws}'");

            require = ParseFlags(dto.BoardRequire, "boardRequire", errors);
            exclude = ParseFlags(dto.BoardExclude, "boardExclude", errors);
            if ((require & exclude) != 0)
                errors.Add($"flags both required and excluded: {string.Join(", ", BoardAnalyzer.Names(require & exclude))}");
        }

        var behindRequire = TypeIds(dto.Behind?.Require, "behind.require", types, errors);
        var behindExclude = TypeIds(dto.Behind?.Exclude, "behind.exclude", types, errors);
        if (behindRequire.Intersect(behindExclude).Any())
            errors.Add($"behind: {string.Join(", ", behindRequire.Intersect(behindExclude))} both required and excluded");
        if (template is { CanHavePlayersBehind: false } && (behindRequire.Count > 0 || behindExclude.Count > 0))
            errors.Add($"behind: nobody is left to act behind hero in {template.Id}; behind conditions only apply to Pre_IsoVsLimper");

        var othersRequire = TypeIds(dto.Others?.Require, "others.require", types, errors);
        var othersExclude = TypeIds(dto.Others?.Exclude, "others.exclude", types, errors);
        if (othersRequire.Intersect(othersExclude).Any())
            errors.Add($"others: {string.Join(", ", othersRequire.Intersect(othersExclude))} both required and excluded");
        if (template is { CanHaveOtherOpponents: false } && (othersRequire.Count > 0 || othersExclude.Count > 0))
            errors.Add($"others: {template.Id} is heads-up; others conditions apply to multiway spots (Pre_IsoVsLimper, SRP_3Way_FlopCheckedToHero)");

        if (template is not null && (dto.Correct is null || !template.OptionIds.Contains(dto.Correct)))
            errors.Add($"correct '{dto.Correct}' is not an option of {template.Id}: {string.Join(", ", template.OptionIds)}");
        if (string.IsNullOrWhiteSpace(dto.Reason)) errors.Add("reason is required");

        if (errors.Count > 0) throw new ContentException(errors);

        return new Rule
        {
            Id = dto.Id!,
            Placeholder = dto.Placeholder,
            VillainType = dto.VillainType!,
            Line = lineId,
            Hero = hero,
            HeroGroup = groups,
            Draws = draws,
            BoardRequire = require,
            BoardExclude = exclude,
            BehindRequire = behindRequire,
            BehindExclude = behindExclude,
            OthersRequire = othersRequire,
            OthersExclude = othersExclude,
            Correct = dto.Correct!,
            Reason = dto.Reason!.Trim(),
        };
    }

    /// <summary>Player type names (case-insensitive) mapped to their ids; unknown names are errors.</summary>
    private static List<string> TypeIds(IEnumerable<string>? names, string field, IReadOnlyList<PlayerType> types, List<string> errors)
    {
        var ids = new List<string>();
        foreach (var name in names ?? [])
        {
            var type = types.FirstOrDefault(t => string.Equals(t.Id, name, StringComparison.OrdinalIgnoreCase));
            if (type is null) errors.Add($"{field}: '{name}' is not one of: {string.Join(", ", types.Select(t => t.Id))}");
            else if (!ids.Contains(type.Id)) ids.Add(type.Id);
        }
        return ids;
    }

    /// <summary>Enum names only (case-insensitive); numeric strings are rejected.</summary>
    private static bool TryParseName<T>(string? text, out T value) where T : struct, Enum
    {
        var name = Enum.GetNames<T>().FirstOrDefault(n => string.Equals(n, text, StringComparison.OrdinalIgnoreCase));
        value = name is null ? default : Enum.Parse<T>(name);
        return name is not null;
    }

    private static List<T> ParseNames<T>(IEnumerable<string> names, string field, List<string> errors) where T : struct, Enum
    {
        var result = new List<T>();
        foreach (var n in names)
        {
            if (TryParseName<T>(n, out var v)) result.Add(v);
            else errors.Add($"{field}: '{n}' is not one of: {string.Join(", ", Enum.GetNames<T>())}");
        }
        return result;
    }

    private static BoardFlags ParseFlags(IEnumerable<string>? names, string field, List<string> errors)
    {
        var flags = BoardFlags.None;
        foreach (var f in ParseNames<BoardFlags>(names ?? [], field, errors))
        {
            if (f == BoardFlags.None) errors.Add($"{field}: 'None' is not a board flag");
            flags |= f;
        }
        return flags;
    }

    private static T Deserialize<T>(string json) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, Json) ?? throw new ContentException(["file is empty"]);
        }
        catch (JsonException e)
        {
            throw new ContentException([$"invalid JSON: {e.Message}"]);
        }
    }

    private sealed class TypesFileDto
    {
        public List<TypeDto>? Types { get; set; }
    }

    private sealed class TypeDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public bool Placeholder { get; set; }
        public int? TableShare { get; set; }
        public string? ShortName { get; set; }
        public Dictionary<string, double[]>? Ranges { get; set; }
    }

    private sealed class RuleDto
    {
        public string? Id { get; set; }
        public bool Placeholder { get; set; }
        public string? VillainType { get; set; }
        public string? Line { get; set; }
        public HeroDto? Hero { get; set; }
        public List<string>? HeroGroup { get; set; }
        public string? Draws { get; set; }
        public List<string>? BoardRequire { get; set; }
        public List<string>? BoardExclude { get; set; }
        public BehindDto? Behind { get; set; }
        public BehindDto? Others { get; set; }
        public string? Correct { get; set; }
        public string? Reason { get; set; }
    }

    private sealed class RangesDto
    {
        public bool Placeholder { get; set; }
        public string? Description { get; set; }
        public Dictionary<string, string>? Open { get; set; }
        public Dictionary<string, string>? BigBlindCall { get; set; }
    }

    private sealed class BehindDto
    {
        public List<string>? Require { get; set; }
        public List<string>? Exclude { get; set; }
    }

    private sealed class HeroDto
    {
        public string? MinStrength { get; set; }
        public string? MaxStrength { get; set; }
    }
}
