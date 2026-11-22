import React, { useState } from "react";
import { bindValue, trigger, useValue } from "cs2/api";
import { GovernmentState, GROUP, governmentState$ } from "./bindings";

// These bindings only exist in dev builds (DEV_TOOLS). In Release they keep their fallback values,
// so devTools stays false and this drawer never renders.
const devTools$ = bindValue<boolean>(GROUP, "devTools", false);
const devStatus$ = bindValue<string>(GROUP, "devStatus", "");

const STATES: { id: GovernmentState; label: string }[] = [
  { id: "NoCityHall", label: "No City Hall" },
  { id: "AwaitingFounding", label: "Awaiting founding" },
  { id: "Active", label: "Active" },
  { id: "Suspended", label: "Suspended" },
];

const Cmd = ({ label, command, arg, on }: { label: string; command: string; arg?: string; on?: boolean }) => (
  <div
    className={on ? "ea-btn ea-btn--on" : "ea-btn"}
    onClick={() => (arg === undefined ? trigger(GROUP, command) : trigger(GROUP, command, arg))}
  >
    {label}
  </div>
);

/** Collapsible developer drawer (dev builds only). */
export const DevSection = () => {
  const enabled = useValue(devTools$);
  const text = useValue(devStatus$);
  const state = useValue(governmentState$);
  const [open, setOpen] = useState(false);
  if (!enabled) return null;

  return (
    <div className="ea-dev">
      <div className="ea-dev__toggle" onClick={() => setOpen(!open)}>
        <span>Developer tools</span>
        <span>{open ? "Hide" : "Show"}</span>
      </div>
      {open ? (
        <>
          <div className="ea-actions">
            <Cmd label="Run election" command="devRunElection" />
            <Cmd label="Run poll" command="devRunPoll" />
            <Cmd label="+1 month" command="devAdvanceMonth" />
            <Cmd label="Skip to election" command="devSkipToElection" />
            <Cmd label="Reset" command="devReset" />
          </div>
          <div className="ea-actions" style={{ marginTop: "0" }}>
            <Cmd label="Switch parties" command="devSwitchParties" />
            <Cmd label="Spike report" command="devSpikeReport" />
            <Cmd label="Export city CSV" command="devExportCity" />
            <Cmd label="Benchmark readers" command="devBenchmark" />
          </div>
          <div className="ea-actions" style={{ marginTop: "0" }}>
            {STATES.map((s) => (
              <Cmd key={s.id} label={s.label} command="devSetGovernment" arg={s.id} on={s.id === state} />
            ))}
          </div>
          {text ? (
            <div className="ea-dev__status">
              {text.split("\n").map((line, i) => (
                <div key={i}>{line}</div>
              ))}
            </div>
          ) : null}
        </>
      ) : null}
    </div>
  );
};
