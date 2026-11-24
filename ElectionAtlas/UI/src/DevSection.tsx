import React from "react";
import { bindValue, trigger, useValue } from "cs2/api";
import { Panel, Scrollable } from "cs2/ui";
import { GovernmentState, GROUP, governmentState$ } from "./bindings";

// Release sends devTools=false, so this drawer never renders in the public build.
const devTools$ = bindValue<boolean>(GROUP, "devTools", false);
const devStatus$ = bindValue<string>(GROUP, "devStatus", "");

const STATES: { id: GovernmentState; label: string }[] = [
  { id: "NoCityHall", label: "No City Hall" },
  { id: "AwaitingFounding", label: "Awaiting founding" },
  { id: "Active", label: "Active" },
  { id: "Suspended", label: "Suspended" },
];

// Coherent's flex-wrap mis-sizes wrapped lines (buttons overlap the next row), so each
// command sits in a fixed-size cell of a four-column grid instead of free-wrapping.
const Cmd = ({ label, command, arg, on }: { label: string; command: string; arg?: string; on?: boolean }) => (
  <div className="ea-dev__cell">
    <div
      className={on ? "ea-btn ea-btn--on" : "ea-btn"}
      onClick={() => (arg === undefined ? trigger(GROUP, command) : trigger(GROUP, command, arg))}
    >
      {label}
    </div>
  </div>
);

const Group = ({ title, children, wide = false }: { title: string; children: React.ReactNode; wide?: boolean }) => (
  <>
    <div className="ea-dev__group">{title}</div>
    <div className={wide ? "ea-dev__grid ea-dev__grid--wide" : "ea-dev__grid"}>{children}</div>
  </>
);

/** One row in the Elections panel that opens or closes the developer window (dev builds only). */
export const DevToggle = ({ open, onToggle }: { open: boolean; onToggle: () => void }) => {
  const enabled = useValue(devTools$);
  if (!enabled) return null;
  return (
    <div className="ea-dev">
      <div className="ea-dev__toggle" onClick={onToggle}>
        <span>Developer tools</span>
        <span>{open ? "Close" : "Open"}</span>
      </div>
    </div>
  );
};

/**
 * The developer commands in their own draggable window, so they never push the Elections panel off screen.
 * Rendered beside the panel, not inside it.
 */
export const DevWindow = ({ replay, onClose }: { replay: boolean; onClose: () => void }) => {
  const enabled = useValue(devTools$);
  const text = useValue(devStatus$);
  const state = useValue(governmentState$);
  if (!enabled) return null;

  return (
    <Panel draggable header="Developer tools" initialPosition={{ x: 0.02, y: 0.2 }} onClose={onClose} style={{ width: "560rem" }}>
      <Scrollable vertical style={{ maxHeight: "620rem" }}>
        <div className="ea-body">
          {!replay ? (
            <>
              <Group title="Simulation">
                <Cmd label="Run election" command="devRunElection" />
                <Cmd label="Run poll" command="devRunPoll" />
                <Cmd label="+1 month" command="devAdvanceMonth" />
                <Cmd label="Skip to election" command="devSkipToElection" />
                <Cmd label="Reset" command="devReset" />
              </Group>
              <Group title="Diagnostics">
                <Cmd label="Spike report" command="devSpikeReport" />
                <Cmd label="Export city CSV" command="devExportCity" />
                <Cmd label="Benchmark readers" command="devBenchmark" />
              </Group>
            </>
          ) : null}
          <Group title="Election Day replays">
            <Cmd label="Watch latest" command="devWatchLatest" />
            <Cmd label="Use city clock" command="devWatchCityClock" />
            <Cmd label="Close race" command="devWatchSample" arg="close-race" />
            <Cmd label="Close race (most districts)" command="devWatchSample" arg="close-race-most-districts" />
            <Cmd label="Four parties" command="devWatchSample" arg="four-parties" />
            <Cmd label="Outside residents" command="devWatchSample" arg="outside-residents" />
          </Group>
          <Group title="Election notifications" wide>
            <Cmd label="Show polls-closed notice" command="devNoticePollsClosed" />
            <Cmd label="Show results notice" command="devNoticeResults" />
          </Group>
          {replay ? (
            <Group title="Replay controls">
              <Cmd label="Step replay" command="devDayStep" />
              <Cmd label="Fast pace" command="devDayPace" arg="fast" />
              <Cmd label="Normal pace" command="devDayPace" arg="normal" />
              <Cmd label="One per wave" command="devDaySingle" />
              <Cmd label="Three per wave" command="devDayWaves" />
              <Cmd label="Automatic waves" command="devDayAutoWaves" />
              <Cmd label="Clock 06:55" command="devDayClock" arg="415" />
              <Cmd label="Clock 18:55" command="devDayClock" arg="1135" />
              <Cmd label="Clock 23:55" command="devDayClock" arg="1435" />
            </Group>
          ) : null}
          {!replay ? (
            <Group title="Government">
              {STATES.map((s) => (
                <Cmd key={s.id} label={s.label} command="devSetGovernment" arg={s.id} on={s.id === state} />
              ))}
            </Group>
          ) : null}
          {text ? (
            <div className="ea-dev__status">
              {text.split("\n").map((line, i) => (
                <div key={i}>{line}</div>
              ))}
            </div>
          ) : null}
        </div>
      </Scrollable>
    </Panel>
  );
};
