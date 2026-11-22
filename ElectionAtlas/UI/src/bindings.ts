import { bindValue } from "cs2/api";

// Must match ElectionsUISystem.kGroup in C#. Shapes below mirror Systems/UiWriters.cs.
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
  /** False for the outside-any-district bucket: no seat, nothing on the map to select. */
  seat: boolean;
  name: string;
  /** "Urban core", "Suburban", ... or "" until the census has seen the district. */
  profile: string;
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
// Raw bindings: C# writes these objects straight into the UI (no JSON strings to parse).
export const overview$ = bindValue<Overview | null>(GROUP, "overview", null);
export const districts$ = bindValue<Districts | null>(GROUP, "districts", null);
export const history$ = bindValue<History | null>(GROUP, "history", null);
export const polls$ = bindValue<Polls | null>(GROUP, "polls", null);
/** The panel is collapsed to the map legend and districts are colored by result. */
export const mapView$ = bindValue<boolean>(GROUP, "mapView", false);

/** A party's share of a district's votes. The one place this is computed in the UI. */
export const partyShare = (d: DistrictView, party: number): number => (d.votes > 0 ? (d.partyVotes[party] ?? 0) / d.votes : 0);
