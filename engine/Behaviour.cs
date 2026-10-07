namespace PokerDrills.Engine;

public enum PreflopSituation
{
    /// <summary>Nobody has raised yet (limpers are fine).</summary>
    Unopened,

    /// <summary>Somebody has raised.</summary>
    FacingRaise,
}

public enum PreflopAction { Fold, Limp, Call, Raise }

/// <summary>
/// How a player acts preflop, read from that player's own HUD line. A placeholder model:
/// unopened, they raise PFR% and limp <see cref="LimpShare"/> of their non-raising VPIP;
/// facing a raise, they 3-bet 3Bet% and cold-call <see cref="ColdCallShare"/> of their non-raising VPIP.
/// Everything else is a fold.
/// </summary>
public static class PreflopBehaviour
{
    public const double LimpShare = 0.6;
    public const double ColdCallShare = 0.5;

    /// <summary>Probabilities of folding, entering passively (limp or call) and raising.</summary>
    public static (double Fold, double Passive, double Raise) Odds(StatLine stats, PreflopSituation situation)
    {
        var loose = Math.Max(0, stats.Vpip - stats.Pfr) / 100.0;
        var (passive, raise) = situation == PreflopSituation.Unopened
            ? (LimpShare * loose, stats.Pfr / 100.0)
            : (ColdCallShare * loose, stats.ThreeBet / 100.0);
        return (Math.Max(0, 1 - passive - raise), passive, raise);
    }

    public static PreflopAction Draw(StatLine stats, PreflopSituation situation, Rng rng)
    {
        var (fold, passive, _) = Odds(stats, situation);
        var x = rng.NextDouble();
        if (x < fold) return PreflopAction.Fold;
        if (x < fold + passive) return situation == PreflopSituation.Unopened ? PreflopAction.Limp : PreflopAction.Call;
        return PreflopAction.Raise;
    }
}
