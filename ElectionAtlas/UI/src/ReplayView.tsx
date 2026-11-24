import React from "react";
import { trigger } from "cs2/api";
import { Scrollable } from "cs2/ui";
import { ElectionDayReplay, GROUP } from "./bindings";
import { clock, count, monthLabel, pct } from "./format";
import { Dot, StackBar } from "./parts";

export const ReplayView = ({ r, map = false, onResults }: { r: ElectionDayReplay; map?: boolean; onResults: () => void }) => {
  const leader = r.leader >= 0 ? r.parties[r.leader] : undefined;
  const seatsDecide = r.rule === "MostDistricts";
  const rule = seatsDecide ? "Most districts" : "Popular vote";
  // Keep the original party indices for the governing rule. The displayed order changes as votes arrive.
  const ranked = r.parties.map((party, index) => ({ party, index }))
    .sort((a, b) => (b.party.votes ?? 0) - (a.party.votes ?? 0) || a.index - b.index)
    .map(({ party }) => party);
  const polls = r.beforePolls ? "Opens at 07:00" : r.voting ? "Polls open" : "Polls closed";
  const headline = r.finished ? "Final result" : `${clock(r.hour)} · ${polls}`;
  const wave = r.wave.join(", ");
  const report = r.finished ? "Count complete" : wave ? `Reporting · ${wave}` : "Awaiting district returns";
  const leaderLabel = leader
    ? `${leader.name} ${r.finished ? "governs" : "leads"} · ${rule}`
    : r.finished ? "No government" : `Counting · ${rule}`;

  return (
    <div className={map ? "ea-day ea-day--map" : "ea-day"}>
      <div className="ea-muted ea-small ea-ellipsis">{`${r.sample ? "Sample election" : `Election ${r.number}`} · ${monthLabel(r.month)}${r.live ? " · City clock" : ""}`}</div>
      <div className="ea-day__title">{headline}</div>
      <div className="ea-day__line">
        <span className="ea-ellipsis">{`${count(r.ballots)} ballots cast`}</span>
        <span className="ea-bold ea-nowrap">{`${pct(r.turnout)} turnout`}</span>
      </div>
      <div className="ea-day__line">
        <span className="ea-small ea-ellipsis">{r.voting ? "District turnout" : `${count(r.counted)} ballots counted`}</span>
        {!r.voting && r.districtCount > 0 ? (
          <span className="ea-bold ea-small ea-nowrap">{`${r.districtsReported} of ${r.districtCount} districts`}</span>
        ) : null}
      </div>
      <div className="ea-day__details">
        {r.voting ? (
          <Scrollable vertical className="ea-day__district-scroll">
            {r.districts.map((district, i) => (
              <div key={i} className="ea-day__line">
                <span className="ea-ellipsis" title={district.name}>{district.name}</span>
                <span className="ea-day__votes" title="Ballots cast">{count(district.ballots)}</span>
                <span className="ea-day__share" title="Turnout">{pct(district.turnout)}</span>
              </div>
            ))}
          </Scrollable>
        ) : (
          <>
            <div className="ea-day__line ea-small ea-muted">
              <span className="ea-ellipsis" title={report}>{report}</span>
            </div>
            <div className="ea-day__line ea-small ea-bold">
              {leader ? <Dot color={leader.color} /> : null}
              <span className="ea-ellipsis" title={leaderLabel}>{leaderLabel}</span>
              {r.leadChanged && !r.finished ? <span className="ea-day__lead-change">Lead changes hands</span> : null}
            </div>
            <StackBar parties={ranked} shares={ranked.map((p) => p.share ?? 0)} legend={false} />
            <div className="ea-day__line ea-small ea-muted">
              <span className="ea-ellipsis">Party</span>
              <span className="ea-day__votes">Votes</span>
              <span className="ea-day__share">Share</span>
              {seatsDecide ? <span className="ea-day__seats">Seats</span> : null}
            </div>
            <Scrollable vertical className="ea-day__party-scroll">
              {ranked.map((party) => (
                <div key={party.id} className="ea-day__line">
                  <Dot color={party.color} />
                  <span className="ea-ellipsis" title={party.name}>{party.name}</span>
                  <span className="ea-day__votes">{count(party.votes)}</span>
                  <span className="ea-day__share">{pct(party.share)}</span>
                  {seatsDecide ? <span className="ea-day__seats">{count(party.won)}</span> : null}
                </div>
              ))}
            </Scrollable>
          </>
        )}
      </div>
      <div className="ea-day__controls">
        <div className="ea-day__control">
          {r.finished ? (
            <div className="ea-btn ea-btn--primary" onClick={onResults}>{r.sample ? "Close sample" : "Back to results"}</div>
          ) : (
            <div className="ea-btn" onClick={() => trigger(GROUP, "toggleReplayPause")}>{r.playing ? "Pause" : "Play"}</div>
          )}
        </div>
        <div className="ea-day__control">
          {r.voting ? <div className="ea-btn" onClick={() => trigger(GROUP, "skipToCount")}>Skip to count</div> : null}
        </div>
        <div className="ea-day__control">
          {!r.finished ? <div className="ea-btn" onClick={() => trigger(GROUP, "skipToFinal")}>Final result</div> : null}
        </div>
        <div className="ea-day__control">
          {map ? <div className="ea-btn" onClick={() => trigger(GROUP, "hideMap")}>Back to replay</div> : (
            !r.sample ? <div className="ea-btn" onClick={() => trigger(GROUP, "showMap")}>Show map</div> : null
          )}
        </div>
      </div>
    </div>
  );
};
