import { useMemo } from "react";
import { bindValue, useValue, ValueBinding } from "cs2/api";

// Must match ElectionsUISystem.kGroup in C#.
export const GROUP = "electionAtlas";

export type GovernmentState = "NoCityHall" | "AwaitingFounding" | "Active" | "Suspended";

export interface PartyInfo {
  id: string;
  name: string;
  color: string;
  economic?: number;
  social?: number;
  share?: number;
  votes?: number;
  won?: number;
}

export interface ElectionSummary {
  number: number;
  month: number;
  rule: string;
  turnout: number;
  target: number;
  eligible: number;
  votes: number;
  governing: number;
  parties: PartyInfo[];
}

export interface Overview {
  state: GovernmentState;
  today: number;
  cityHalls: number;
  efficiency: number;
  shutDown: boolean;
  switchedOff: boolean;
  shutdownMonths: number;
  nextElection: number;
  monthsUntil: number;
  elections: number;
  reading: boolean;
  devMonthOffset: number;
  governing: PartyInfo | null;
  parties: PartyInfo[];
  poll: { month: number; approval: number; intention: number[] } | null;
}

export interface DistrictView {
  id: number;
  index: number;
  version: number;
  selectable: boolean;
  name: string;
  eligible: number;
  votes: number;
  turnout: number;
  winner: number;
  winnerShare: number;
  margin: number;
  color: string;
  approval: number | null;
  partyVotes: number[];
  trend: (number | null)[];
}

export interface Districts extends Partial<ElectionSummary> {
  count: number;
  index?: number;
  approvalMonth?: number;
  districts?: DistrictView[];
}

export interface History {
  elections: (ElectionSummary & { index: number })[];
}

export interface Polls {
  parties: PartyInfo[];
  records: { month: number; approval: number; governing: string | null; intention: number[] }[];
}

export const panelOpen$ = bindValue<boolean>(GROUP, "panelOpen", false);
export const governmentState$ = bindValue<GovernmentState>(GROUP, "governmentState", "NoCityHall");
export const overview$ = bindValue<string>(GROUP, "overview", "{}");
export const districts$ = bindValue<string>(GROUP, "districts", "{}");
export const history$ = bindValue<string>(GROUP, "history", "{}");
export const polls$ = bindValue<string>(GROUP, "polls", "{}");

/** Decodes a JSON string binding once per change. */
export function useJson<T>(binding: ValueBinding<string>): T | null {
  const text = useValue(binding);
  return useMemo(() => {
    try {
      return JSON.parse(text) as T;
    } catch {
      return null;
    }
  }, [text]);
}
