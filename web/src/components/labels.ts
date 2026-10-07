export const LINE_LABELS: Record<string, string> = {
  Pre_IsoVsLimper: 'Preflop · villain limps',
  SRP_HeroIP_FlopVillainChecks: 'Flop · villain checks',
  SRP_HeroOOP_FacingFlopCbet: 'Flop · facing c-bet',
  SRP_HeroIP_RiverVillainChecks: 'River · villain checks',
  SRP_HeroIP_FacingRiverBet: 'River · facing bet',
  Pre_FacingThreeBet: 'Preflop · facing a 3-bet',
  SRP_3Way_FlopCheckedToHero: 'Flop · 3-way, checked to you',
  Identify: 'Read the HUD',
};

export const lineLabel = (line: string): string => LINE_LABELS[line] ?? line;

/** "station-river-value" -> "Station river value". */
export function ruleTitle(ruleId: string): string {
  const words = ruleId.split('-');
  return words.map((w, i) => (i === 0 ? w.charAt(0).toUpperCase() + w.slice(1) : w)).join(' ');
}

export const STAT_LABELS: [key: string, label: string][] = [
  ['VPIP', 'VPIP'],
  ['PFR', 'PFR'],
  ['3Bet', '3Bet'],
  ['WTSD', 'WTSD'],
  ['AF', 'AF'],
  ['FoldToCbet', 'Fold c-bet'],
];

export const bb = (n: number): string => `${Number(n.toFixed(2))}bb`;
