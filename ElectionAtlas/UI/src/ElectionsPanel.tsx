import React from "react";
import { trigger, useValue } from "cs2/api";
import { Panel } from "cs2/ui";
import { GovernmentState, GROUP, governmentState$, panelOpen$ } from "./bindings";

const STATUS: Record<GovernmentState, { title: string; detail: string }> = {
  NoCityHall: {
    title: "No City Hall",
    detail: "Build a City Hall to start holding elections.",
  },
  AwaitingFounding: {
    title: "Elections unlocked",
    detail: "Your city has a City Hall. The founding election will be scheduled here.",
  },
  Active: {
    title: "Elections running",
    detail: "Monthly polls are running and elections follow the cadence in Options.",
  },
  Suspended: {
    title: "Government suspended",
    detail: "The City Hall is gone. Rebuild one to resume elections; past results are kept.",
  },
};

// Phase 2: shows the City Hall gate's state. Overview / Parties / Results / Polling tabs arrive in phase 6.
export const ElectionsPanel = () => {
  const open = useValue(panelOpen$);
  const state = useValue(governmentState$);
  if (!open) return null;

  const status = STATUS[state] ?? STATUS.NoCityHall;
  return (
    <Panel draggable header="Elections" initialPosition={{ x: 0.5, y: 0.3 }} onClose={() => trigger(GROUP, "closePanel")}>
      <p>
        <strong>{status.title}</strong>
      </p>
      <p>{status.detail}</p>
    </Panel>
  );
};
