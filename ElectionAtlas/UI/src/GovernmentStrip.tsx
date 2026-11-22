import React from "react";
import { Overview } from "./bindings";
import { monthLabel, months } from "./format";
import { Dot } from "./parts";

/** One glance: who governs, how the city feels, when it votes next. Plus a callout when something's wrong. */
export const GovernmentStrip = ({ o }: { o: Overview | null }) => {
  if (!o || !o.state) return null;

  const callout =
    o.state === "NoCityHall" ? (
      <div className="ea-callout ea-callout--info">Build a City Hall to start holding elections.</div>
    ) : o.state === "Suspended" ? (
      <div className="ea-callout">The City Hall is gone, so elections are suspended. Rebuild it to resume; past results are kept.</div>
    ) : o.shutDown ? (
      <div className="ea-callout">
        Government shutdown: the City Hall isn't working{o.shutdownMonths > 0 ? ` (${months(o.shutdownMonths)} this term)` : ""}. The
        governing party loses approval every month, and votes at the next election.
      </div>
    ) : o.shutdownMonths > 0 ? (
      <div className="ea-callout">
        The City Hall was shut down for {months(o.shutdownMonths)} this term. Voters will remember at the next election.
      </div>
    ) : null;

  const next =
    o.monthsUntil < 0 ? "Not scheduled" : o.monthsUntil === 0 ? "Due now" : monthLabel(o.nextElection);
  const nextCaption = o.reading ? "Counting votes…" : o.monthsUntil > 0 ? `in ${months(o.monthsUntil)}` : o.state === "AwaitingFounding" ? "founding election" : "";

  return (
    <div>
      {callout}
      <div className="ea-strip">
        <div className="ea-strip__cell">
          <div className="ea-muted ea-small">Governing</div>
          <div className="ea-strip__value ea-party">
            {o.governing ? <Dot color={o.governing.color} large /> : null}
            {o.governing ? o.governing.name : "Nobody yet"}
          </div>
        </div>
        <div className="ea-strip__cell">
          <div className="ea-muted ea-small">Approval</div>
          <div className="ea-strip__value">{o.poll ? Math.round(o.poll.approval) : "-"}</div>
          <div className="ea-muted ea-small">{o.poll ? `poll of ${monthLabel(o.poll.month)}` : "no poll yet"}</div>
        </div>
        <div className="ea-strip__cell">
          <div className="ea-muted ea-small">Next election</div>
          <div className="ea-strip__value">{next}</div>
          <div className="ea-muted ea-small">{nextCaption}</div>
        </div>
      </div>
    </div>
  );
};
