import React, { useEffect } from "react";
import { trigger, useValue } from "cs2/api";
import { ElectionDayReplay, ElectionNotice, electionNotice$, GROUP, liveElection$, panelOpen$, replay$ } from "./bindings";
import { clock, pct } from "./format";
import { Dot } from "./parts";

// The toolbar button's built-in icon, so the mod still ships no image assets.
const ICON = "Media/Game/Icons/Administration.svg";
const NOTICE_MS = 8000;

// The panel owns its tabs, so View results asks it to switch to Districts.
const viewResultsListeners = new Set<() => void>();
export const onViewResults = (listener: () => void) => {
  viewResultsListeners.add(listener);
  return () => {
    viewResultsListeners.delete(listener);
  };
};

/** "Polls open · 14:00 · 38% turnout", "Counting · 3 of 10 districts" or "Results are in". */
const status = (r: ElectionDayReplay): string => {
  if (r.finished) return "Results are in";
  if (r.beforePolls) return "Election Day · Polls open at 07:00";
  if (r.voting) return `Polls open · ${clock(r.hour)} · ${pct(r.turnout, 0)} turnout`;
  return r.districtCount > 0 ? `Counting · ${r.districtsReported} of ${r.districtCount} districts` : "Counting";
};

/** A toast beside the button, like a chirp. It hides itself after a few seconds. */
const Notice = ({ n }: { n: ElectionNotice }) => {
  useEffect(() => {
    const timer = setTimeout(() => trigger(GROUP, "dismissElectionNotice", n.id, true), NOTICE_MS);
    return () => clearTimeout(timer);
  }, [n.id]);

  const message = n.results
    ? `Results are in: ${n.governingParty ?? "no party"} governs`
    : `Polls have closed${n.cityName ? ` in ${n.cityName}` : ""}. Watch the count`;
  const open = () => {
    if (n.results) viewResultsListeners.forEach((listener) => listener());
    trigger(GROUP, "openElectionNotice", n.id);
  };
  return (
    <div className="ea-notice" role="status">
      <div>{message}</div>
      <div className="ea-notice__actions">
        <div className="ea-btn ea-btn--primary" onClick={open}>{n.results ? "View results" : "Watch"}</div>
        <div className="ea-btn" onClick={() => trigger(GROUP, "dismissElectionNotice", n.id, false)}>Dismiss</div>
      </div>
    </div>
  );
};

/**
 * Sits on top of the game's right-hand column, just above Chirper. During the newest election's Election Day it
 * shows a button with a one-line status; one click opens the live replay, a second closes the panel.
 */
export const LiveElection = () => {
  const live = useValue(liveElection$);
  const replay = useValue(replay$);
  const panelOpen = useValue(panelOpen$);
  const notice = useValue(electionNotice$);
  const r = live ? replay : null;
  if (!r && !notice) return null;

  const toggle = () => trigger(GROUP, "toggleLiveElection");
  const leader = r && !r.voting && r.leader >= 0 ? r.parties[r.leader] : undefined;
  return (
    <div className={r ? "ea-live ea-live--button" : "ea-live"}>
      {notice ? <Notice key={notice.id} n={notice} /> : null}
      {r && !notice ? (
        <div className="ea-live__side">
          <div className="ea-live__badge" onClick={toggle}>
            {leader ? <Dot color={leader.color} /> : null}
            <span>{status(r)}</span>
          </div>
        </div>
      ) : null}
      {r ? (
        <div className={panelOpen ? "ea-live__item ea-live__item--on" : "ea-live__item"} onClick={toggle}>
          {/* Tinted like the game's own icons: the SVG is a mask over a white fill. */}
          <div className="ea-live__glyph" style={{ maskImage: `url(${ICON})` }} />
        </div>
      ) : null}
    </div>
  );
};
