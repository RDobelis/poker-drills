using PokerDrills.Engine;

namespace PokerDrills.Engine.Tests;

internal static class TestHelpers
{
    public static Card[] Cards(string text) => Card.ParseMany(text);

    private static readonly Lazy<string> Root = new(() =>
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "content", "types.json"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not find repo root (content/types.json)");
    });

    public static string ContentDir => Path.Combine(Root.Value, "content");

    private static readonly Lazy<ContentSet> RealContent = new(() => ContentLoader.Load(ContentDir));

    public static ContentSet Content => RealContent.Value;
}
