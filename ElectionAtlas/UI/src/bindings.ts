import { bindValue } from "cs2/api";

// Must match ElectionsUISystem.kGroup in C#.
export const GROUP = "electionAtlas";

export type GovernmentState = "NoCityHall" | "AwaitingFounding" | "Active" | "Suspended";

export const panelOpen$ = bindValue<boolean>(GROUP, "panelOpen", false);
export const governmentState$ = bindValue<GovernmentState>(GROUP, "governmentState", "NoCityHall");
