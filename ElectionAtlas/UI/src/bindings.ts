import { bindValue } from "cs2/api";

// Must match ElectionsUISystem.kGroup in C#.
export const GROUP = "electionAtlas";

export const panelOpen$ = bindValue<boolean>(GROUP, "panelOpen", false);
