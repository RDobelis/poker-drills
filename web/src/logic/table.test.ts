import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import type { DrillFile } from '../types';
import { SEATS, buildFrames, decisionStreet, describeFrame, slotOf, tableAt, toCallFor, totalPot, type Seat } from './table';

// Replays every action drill in the generated drills.json: the table the student sees must end exactly
// at the pot, stacks and amount to call the C# generator computed.
const file = JSON.parse(readFileSync(new URL('../../public/drills.json', import.meta.url), 'utf8')) as DrillFile;
const actionDrills = file.drills.filter((d) => d.kind === 'action');
const typeName = (id: string) => file.types.find((t) => t.id === id)?.name ?? id;

describe('table replay of the generated drills', () => {
  it('ends at the drill pot, stacks and amount to call for every drill', () => {
    expect(actionDrills.length).toBeGreaterThan(1000);
    for (const d of actionDrills) {
      const frames = buildFrames(d);
      const end = tableAt(d, frames[frames.length - 1]);
      const hero = d.heroPosition as Seat;
      const villain = d.villainPosition as Seat;
      expect(totalPot(end), d.id).toBeCloseTo(d.pot!, 6);
      expect(end.seats[hero].stack, d.id).toBeCloseTo(d.stacks!.hero, 6);
      expect(end.seats[villain].stack, d.id).toBeCloseTo(d.stacks!.villain, 6);
      expect(toCallFor(end, hero), d.id).toBeCloseTo(d.toCall!, 6);
      expect(end.boardCount, d.id).toBe(d.board.length);
      expect(end.street).toBe(decisionStreet(d));
      expect(end.seats[hero].folded || end.seats[villain].folded).toBe(false);
    }
  });

  it('plays frames forward: streets in order, board revealed street by street, folds grouped', () => {
    const river = actionDrills.find((d) => d.board.length === 5 && d.heroPosition === 'CO')!;
    const frames = buildFrames(river);
    const boards = frames.map((f) => tableAt(river, f).boardCount);
    expect(boards[0]).toBe(0);
    expect(boards).toEqual([...boards].sort((a, b) => a - b));
    expect(new Set(boards)).toEqual(new Set([0, 3, 4, 5]));
    for (let i = 1; i < frames.length; i++) expect(frames[i].applied).toBeGreaterThanOrEqual(frames[i - 1].applied);

    // CO open: "UTG, MP fold" is one frame, and the caption says so.
    const foldFrame = frames.findIndex((f, i) => i > 0 && f.applied - frames[i - 1].applied === 2);
    expect(describeFrame(river, frames, foldFrame, typeName)).toBe('UTG, MP fold');
    expect(describeFrame(river, frames, foldFrame + 1, typeName)).toBe('You raise to 2.5bb');
  });

  it('names the villain by type and moves chips into the pot between streets', () => {
    const d = actionDrills.find((x) => x.line === 'SRP_HeroOOP_FacingFlopCbet')!;
    const frames = buildFrames(d);
    const lastCaption = describeFrame(d, frames, frames.length - 1, typeName);
    expect(lastCaption).toMatch(new RegExp(`^${typeName(d.villainType)} \\(${d.villainPosition}\\) bets \\d+(\\.\\d+)?bb$`));
    const flopDeal = frames.find((f) => f.street === 'Flop')!;
    const atFlop = tableAt(d, flopDeal);
    expect(atFlop.pot).toBeCloseTo(5.5, 6); // open + call + dead small blind
    expect(Object.values(atFlop.seats).every((s) => s.inFront === 0)).toBe(true);
  });

  it('seats a typed player everywhere except hero, villain keeping the rule type', () => {
    const typeIds = new Set(file.types.map((t) => t.id));
    for (const d of actionDrills) {
      expect([...d.players.map((p) => p.seat)].sort(), d.id).toEqual(SEATS.filter((s) => s !== d.heroPosition).sort());
      expect(d.players.find((p) => p.seat === d.villainPosition)?.type, d.id).toBe(d.villainType);
      for (const p of d.players) expect(typeIds.has(p.type), d.id).toBe(true);
    }
  });

  it('seats hero at the bottom and goes clockwise', () => {
    expect(slotOf('CO', 'CO')).toBe(0);
    expect(slotOf('BTN', 'CO')).toBe(1);
    expect(slotOf('BB', 'CO')).toBe(3);
    expect(slotOf('MP', 'CO')).toBe(5);
  });
});
