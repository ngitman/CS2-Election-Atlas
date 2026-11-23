import React from "react";
import { trigger } from "cs2/api";
import { GROUP, History, Polls } from "./bindings";
import { approvalPct, monthLabel, pct } from "./format";
import { Dot, ShareBar, Sparkline, StackBar } from "./parts";

// ------------------------------------------------------------------ History

/** Every election, newest first. Clicking one shows it on the district tiles. */
export const HistoryTab = ({ h, shownIndex, onShow }: { h: History | null; shownIndex: number | undefined; onShow: () => void }) => {
  const elections = h?.elections ?? [];
  if (elections.length === 0) return <div className="ea-empty">No elections yet.</div>;

  return (
    <div>
      {elections.map((e) => {
        const winner = e.governing >= 0 ? e.parties[e.governing] : undefined;
        return (
          <div
            key={e.number}
            className={e.index === shownIndex ? "ea-card ea-card--active" : "ea-card"}
            onClick={() => {
              trigger(GROUP, "showElection", e.index);
              onShow();
            }}
          >
            <div className="ea-card__head">
              <div className="ea-party">
                {winner ? <Dot color={winner.color} /> : null}
                <span className="ea-bold">{winner ? `${winner.name} won` : "No government"}</span>
              </div>
              <div className="ea-muted">{`Election ${e.number}, ${monthLabel(e.month)}`}</div>
            </div>
            <StackBar parties={e.parties} shares={e.parties.map((p) => p.share ?? 0)} />
            <div className="ea-muted ea-small">{`Turnout ${pct(e.turnout)}`}</div>
            <div className="ea-actions">
              <div className="ea-btn" onClick={(event) => {
                event.stopPropagation();
                trigger(GROUP, "watchElectionDay", e.index);
              }}>Watch Election Day</div>
            </div>
          </div>
        );
      })}
    </div>
  );
};

// ------------------------------------------------------------------ Polls

export const PollsTab = ({ p }: { p: Polls | null }) => {
  const records = p?.records ?? [];
  if (!p || records.length === 0) return <div className="ea-empty">No polls yet. Polls run every month while a City Hall stands.</div>;
  const last = records[records.length - 1];

  return (
    <div>
      <div className="ea-section__title">Approval of the government</div>
      <Sparkline values={records.map((r) => r.approval)} />
      <div className="ea-axis">
        <span>{monthLabel(records[0].month)}</span>
        <span>{`${monthLabel(last.month)}: ${approvalPct(last.approval)}`}</span>
      </div>

      <div className="ea-section">
        <div className="ea-section__title">{`Voting intention, ${monthLabel(last.month)}`}</div>
        {p.parties.map((party, i) => (
          <ShareBar key={party.id} name={party.name} color={party.color} share={last.intention[i] ?? 0} />
        ))}
      </div>

      <div className="ea-section">
        <div className="ea-section__title">Month by month</div>
        {records
          .slice(-12)
          .reverse()
          .map((r) => (
            <div key={r.month} className="ea-share">
              <div className="ea-share__name ea-muted">{monthLabel(r.month)}</div>
              <div className="ea-stack" style={{ flex: 1, margin: "0 10rem 0 0" }}>
                {p.parties.map((party, i) => (
                  <div key={party.id} style={{ width: `${(r.intention[i] ?? 0) * 100}%`, backgroundColor: party.color }} />
                ))}
              </div>
              <div className="ea-share__value">{approvalPct(r.approval)}</div>
            </div>
          ))}
      </div>
    </div>
  );
};
