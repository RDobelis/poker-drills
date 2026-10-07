using System.Text.Encodings.Web;
using System.Text.Json;

namespace PokerDrills.Engine;

public static class DrillKinds
{
    public const string Action = "action";
    public const string Identify = "identify";
}

public sealed record Stacks(double Hero, double Villain);

/// <summary>A player sitting at the table (every seat except hero's, villain included).</summary>
public sealed record SeatPlayer(string Seat, string Type, StatLine Stats);

/// <summary>One step of the hand for the table view. <c>To</c> = seat's total in front of it on this street.</summary>
public sealed record DrillAction(string Street, string Seat, string Kind, double To);

/// <summary>Classifier output stored with each drill so a coach can audit the mechanics.</summary>
public sealed record DrillFacts(
    string? HandClass,
    IReadOnlyList<string> Draws,
    IReadOnlyList<string> BoardFlags,
    string? HighCard,
    string? HandGroup,
    string? Hand);

public sealed record Drill
{
    public required string Id { get; init; }
    public required string RuleId { get; init; }
    public required string Kind { get; init; }
    public required string Line { get; init; }
    public required string VillainType { get; init; }
    public required StatLine VillainStats { get; init; }
    public string? HeroPosition { get; init; }
    public string? VillainPosition { get; init; }
    public Stacks? Stacks { get; init; }
    public double? Pot { get; init; }
    public double? ToCall { get; init; }
    public required IReadOnlyList<string> ActionHistory { get; init; }
    public required IReadOnlyList<DrillAction> Actions { get; init; }
    public required IReadOnlyList<SeatPlayer> Players { get; init; }

    /// <summary>Seats still to act after hero at the decision (empty postflop).</summary>
    public required IReadOnlyList<string> Behind { get; init; }

    /// <summary>Other opponents still in the hand besides villain (extra limpers, the small blind in 3-way pots).</summary>
    public required IReadOnlyList<string> Others { get; init; }
    public required IReadOnlyList<string> HeroCards { get; init; }
    public required IReadOnlyList<string> Board { get; init; }
    public required string Question { get; init; }
    public required IReadOnlyList<DrillOption> Options { get; init; }
    public required string Correct { get; init; }
    public required string Reason { get; init; }
    public DrillFacts? Facts { get; init; }
}

public sealed record RuleSummary(
    string Id,
    string Kind,
    string VillainType,
    string Line,
    string Conditions,
    string Correct,
    string Reason,
    bool Placeholder);

public sealed record TypeSummary(string Id, string Name, string ShortName, string Description, IReadOnlyDictionary<string, double[]> Ranges);

/// <summary>Root of web/public/drills.json.</summary>
public sealed record DrillFile(
    int SchemaVersion,
    ulong Seed,
    IReadOnlyList<RuleSummary> Rules,
    IReadOnlyList<TypeSummary> Types,
    IReadOnlyList<Drill> Drills);

public static class DrillJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keep "–" etc. readable; file is static data
        WriteIndented = false,
    };

    public static string Serialize(DrillFile file) => JsonSerializer.Serialize(file, Options);

    public static DrillFile Deserialize(string json) =>
        JsonSerializer.Deserialize<DrillFile>(json, Options) ?? throw new JsonException("Empty drill file");
}
