const SUITS: Record<string, { symbol: string; red: boolean; name: string }> = {
  s: { symbol: '♠', red: false, name: 'spades' },
  h: { symbol: '♥', red: true, name: 'hearts' },
  d: { symbol: '♦', red: true, name: 'diamonds' },
  c: { symbol: '♣', red: false, name: 'clubs' },
};

const RANK_NAMES: Record<string, string> = { T: '10', J: 'Jack', Q: 'Queen', K: 'King', A: 'Ace' };

type Size = 'mini' | 'table' | 'hero' | 'normal' | 'big';

/** One card from a code like "As" or "Td": rank plus suit symbol, red for hearts and diamonds. */
export function PlayingCard({ code, size = 'normal' }: { code: string; size?: Size }) {
  const rank = code.charAt(0);
  const suit = SUITS[code.charAt(1)];
  if (!suit) return <span className="pcard">{code}</span>;
  const label = `${RANK_NAMES[rank] ?? rank} of ${suit.name}`;
  return (
    <span className={`pcard pcard-${size}${suit.red ? ' red' : ''}`} role="img" aria-label={label}>
      <span className="rank">{rank === 'T' ? '10' : rank}</span>
      <span className="suit">{suit.symbol}</span>
    </span>
  );
}

export function CardRow({ codes, size = 'normal' }: { codes: string[]; size?: Size }) {
  return (
    <span className={`card-row card-row-${size}`}>
      {codes.map((c) => (
        <PlayingCard key={c} code={c} size={size} />
      ))}
    </span>
  );
}

/** Renders "[Ks 7d 2c]" groups inside action-history text as mini cards. */
export function TextWithCards({ text }: { text: string }) {
  const parts = text.split(/(\[[^\]]*\])/);
  return (
    <>
      {parts.map((part, i) =>
        part.startsWith('[') && part.endsWith(']') ? (
          <CardRow key={i} codes={part.slice(1, -1).split(' ').filter(Boolean)} size="mini" />
        ) : (
          <span key={i}>{part}</span>
        ),
      )}
    </>
  );
}
