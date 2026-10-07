import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import type { DrillFile } from '../types';
import { decisionsOf } from './hand';
import { SEATS, buildFrames, decisionStreet, describeFrame, slotOf, tableAt, toCallFor, totalPot, type Seat } from './table';

// Replays every action drill in the generated drills.json, and every decision of every hand: the table the student
// sees must end exactly at the pot, stacks and amount to call the C# generator computed.
const file = JSON.parse(readFileSync(new URL('../../public/drills.json', import.meta.url), 'utf8')) as DrillFile;
const hands = file.drills.filter((d) => d.kind === 'hand');
const actionDrills = [...file.drills.filter((d) => d.kind === 'action'), ...hands.flatMap(decisionsOf)];
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

    // CO open: "UTG, MP fold" is one frame, and the caption names each folder by type.
    const foldFrame = frames.findIndex((f, i) => i > 0 && f.applied - frames[i - 1].applied === 2);
    const typeAt = (seat: string) => typeName(river.players.find((p) => p.seat === seat)!.type);
    expect(describeFrame(river, frames, foldFrame, typeName)).toBe(`${typeAt('UTG')} (UTG), ${typeAt('MP')} (MP) fold`);
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

  it('marks as behind exactly the seats still to act after hero, each with a seated player', () => {
    for (const d of actionDrills) {
      const end = tableAt(d, buildFrames(d).at(-1)!);
      const heroIndex = SEATS.indexOf(d.heroPosition as Seat);
      const expected = SEATS.filter((s, i) => i > heroIndex && !d.actions.some((a) => a.seat === s && a.kind !== 'Post'));
      expect(d.behind, d.id).toEqual(expected);
      for (const s of d.behind) {
        expect(end.seats[s].folded, d.id).toBe(false);
        expect(d.players.some((p) => p.seat === s), d.id).toBe(true);
      }
      if (d.board.length > 0) expect(d.behind, d.id).toEqual([]); // postflop everyone else is out
    }
    const maniacBehind = actionDrills.filter((d) => d.ruleId === 'station-iso-playable-maniac-behind');
    expect(maniacBehind.length).toBeGreaterThan(0);
    for (const d of maniacBehind) {
      expect(d.behind.some((s) => d.players.find((p) => p.seat === s)?.type === 'Maniac'), d.id).toBe(true);
    }
  });

  it('shows extra limpers acting by type in "villain limps" spots', () => {
    const iso = actionDrills.filter((d) => d.line === 'Pre_IsoVsLimper');
    const withExtra = iso.filter((d) => d.actions.some((a) => a.kind === 'Limp' && a.seat !== d.villainPosition));
    expect(withExtra.length).toBeGreaterThan(0);
    for (const d of withExtra) {
      const frames = buildFrames(d);
      const captions = frames.map((_, i) => describeFrame(d, frames, i, typeName));
      const limper = d.actions.find((a) => a.kind === 'Limp' && a.seat !== d.villainPosition)!;
      const type = typeName(d.players.find((p) => p.seat === limper.seat)!.type);
      expect(captions, d.id).toContain(`${type} (${limper.seat}) limps 1bb`);
    }
    // Besides hero and villain, only the opponents the spot is built around (the 3-way small blind) raise or call.
    for (const d of actionDrills) {
      const rest = d.actions.filter(
        (a) => a.street === 'Preflop' && a.seat !== d.heroPosition && a.seat !== d.villainPosition && !d.others.includes(a.seat),
      );
      expect(rest.every((a) => a.kind === 'Post' || a.kind === 'Fold' || a.kind === 'Limp'), d.id).toBe(true);
    }
  });

  it('names 3-bets and keeps the 3-way small blind in the hand', () => {
    const threeBet = actionDrills.find((d) => d.line === 'Pre_FacingThreeBet')!;
    const frames = buildFrames(threeBet);
    const captions = frames.map((_, i) => describeFrame(threeBet, frames, i, typeName));
    const raiseTo = threeBet.actions.find((a) => a.seat === threeBet.villainPosition && a.kind === 'Raise')!.to;
    expect(captions).toContain(`${typeName(threeBet.villainType)} (${threeBet.villainPosition}) 3-bets to ${raiseTo}bb`);
    expect(tableAt(threeBet, frames.at(-1)!).seats[threeBet.villainPosition as Seat].label).toBe('3-bet');

    for (const d of actionDrills.filter((x) => x.line === 'SRP_3Way_FlopCheckedToHero')) {
      expect(d.others, d.id).toEqual(['SB']);
      const end = tableAt(d, buildFrames(d).at(-1)!);
      expect(end.seats.SB.folded || end.seats.BB.folded, d.id).toBe(false);
    }
  });

  it('plays a hand on from one decision to the next along the correct answer', () => {
    expect(hands.length).toBeGreaterThan(100);
    for (const h of hands) {
      const decisions = decisionsOf(h);
      expect(decisions.length, h.id).toBeGreaterThanOrEqual(2);
      expect(decisions.at(-1)!.board, h.id).toEqual(h.board);
      for (let i = 1; i < decisions.length; i++) {
        const prev = decisions[i - 1];
        const cur = decisions[i];
        expect(cur.actions.slice(0, prev.actions.length), cur.id).toEqual(prev.actions);
        expect(cur.board.slice(0, prev.board.length), cur.id).toEqual(prev.board);
        const heroNext = cur.actions[prev.actions.length];
        expect(heroNext.seat).toBe(h.heroPosition);
        expect(heroNext.kind, cur.id).toBe(prev.correct === 'Check' ? 'Check' : 'Bet');
        // The table picks the replay up at the previous decision.
        const frames = buildFrames(cur);
        const start = frames.findIndex((f) => f.applied >= prev.actions.length);
        expect(totalPot(tableAt(cur, frames[start])), cur.id).toBeCloseTo(prev.pot!, 6);
        expect(tableAt(cur, frames[start]).street, cur.id).toBe(decisionStreet(prev));
      }
    }
  });

  it('seats hero at the bottom and goes clockwise', () => {
    expect(slotOf('CO', 'CO')).toBe(0);
    expect(slotOf('BTN', 'CO')).toBe(1);
    expect(slotOf('BB', 'CO')).toBe(3);
    expect(slotOf('MP', 'CO')).toBe(5);
  });
});
