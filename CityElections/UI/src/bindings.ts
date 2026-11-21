import { bindValue } from "cs2/api";

// Must match ElectionsUISystem.kGroup in C#.
export const GROUP = "cityElections";

export const panelOpen$ = bindValue<boolean>(GROUP, "panelOpen", false);
