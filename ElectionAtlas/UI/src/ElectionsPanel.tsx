import React from "react";
import { trigger, useValue } from "cs2/api";
import { Panel } from "cs2/ui";
import { GROUP, panelOpen$ } from "./bindings";

// Phase 1: an empty panel that proves the C# <-> UI bindings work.
// Overview / Parties / Results / Polling tabs arrive in phase 6.
export const ElectionsPanel = () => {
  const open = useValue(panelOpen$);
  if (!open) return null;

  return (
    <Panel draggable header="Elections" initialPosition={{ x: 0.5, y: 0.3 }} onClose={() => trigger(GROUP, "closePanel")}>
      <p>Build a City Hall to start holding elections.</p>
    </Panel>
  );
};
