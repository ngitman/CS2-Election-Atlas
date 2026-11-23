import React from "react";
import { trigger } from "cs2/api";
import { Districts, GROUP, PartyInfo } from "./bindings";
import { count, monthLabel, pct } from "./format";

export const Dot = ({ color, large }: { color: string; large?: boolean }) => (
  <div className={large ? "ea-dot ea-dot--lg" : "ea-dot"} style={{ backgroundColor: color }} />
);

/** One party's share: name, bar, percentage. */
export const ShareBar = ({ name, color, share, suffix }: { name: string; color: string; share: number; suffix?: string }) => (
  <div className="ea-share">
    <div className="ea-share__name ea-party">
      <Dot color={color} />
      {name}
    </div>
    <div className="ea-bar">
      <div className="ea-bar__fill" style={{ width: `${Math.max(0, Math.min(1, share)) * 100}%`, backgroundColor: color }} />
    </div>
    <div className="ea-share__value">{pct(share)}</div>
    {suffix ? <div className="ea-muted ea-small ea-nowrap" style={{ width: "86rem", textAlign: "right", flexShrink: 0 }}>{suffix}</div> : null}
  </div>
);

/**
 * All parties as one segmented bar with a legend underneath. Coherent mis-sizes wrapped
 * flex lines (later rows overlap whatever follows), so the legend is a grid of fixed-size
 * cells: `columns` per row, every cell the same height.
 */
export const StackBar = ({ parties, shares, legend = true, columns = 3 }: { parties: PartyInfo[]; shares: number[]; legend?: boolean; columns?: number }) => {
  const withVotes = parties.some((p) => p.votes !== undefined);
  const cell: React.CSSProperties = { width: `${100 / columns}%`, height: withVotes ? "42rem" : "24rem" };
  return (
    <div>
      <div className="ea-stack">
        {parties.map((p, i) => (
          <div key={p.id} style={{ width: `${Math.max(0, shares[i] ?? 0) * 100}%`, backgroundColor: p.color }} />
        ))}
      </div>
      {legend ? <div className="ea-legend">
        {parties.map((p, i) => (
          <div key={p.id} className="ea-legend__item" style={cell}>
            <div className="ea-legend__line">
              <Dot color={p.color} />
              <span className="ea-legend__name">{p.name}</span>
              <span className="ea-bold ea-legend__share">{pct(shares[i] ?? 0)}</span>
            </div>
            {p.votes !== undefined ? <div className="ea-muted ea-small ea-legend__votes">{`${count(p.votes)} votes`}</div> : null}
          </div>
        ))}
      </div> : null}
    </div>
  );
};

export const InfoLine = ({ label, children }: { label: React.ReactNode; children?: React.ReactNode }) => (
  <div className="ea-row">
    <div className="ea-row__label">{label}</div>
    <div className="ea-row__value">{children}</div>
  </div>
);

/** Approval over recent months as small columns; gaps where a district wasn't polled. */
export const Sparkline = ({ values }: { values: (number | null)[] }) => (
  <div className="ea-spark">
    {values.map((v, i) => (
      <div
        key={i}
        className={v === null || v === undefined ? "ea-spark__bar ea-spark__bar--gap" : "ea-spark__bar"}
        style={{ height: `${Math.max(4, v === null || v === undefined ? 6 : v)}%` }}
      />
    ))}
  </div>
);

/** Steps through past elections (‹ ›). Shared by the Districts tab and the map legend. */
export const ElectionPicker = ({ d }: { d: Districts }) => {
  const index = d.index ?? 0;
  const older = index > 0;
  const newer = index < d.count - 1;
  return (
    <div className="ea-picker">
      <div className={older ? "ea-iconbtn" : "ea-iconbtn ea-iconbtn--off"} onClick={() => older && trigger(GROUP, "stepElection", -1)}>
        ‹
      </div>
      <div className="ea-picker__title">
        <div className="ea-bold">{`Election ${d.number} · ${monthLabel(d.month)}`}</div>
        {d.count > 1 ? <div className="ea-muted ea-small">{`${index + 1} of ${d.count}`}</div> : null}
      </div>
      <div className={newer ? "ea-iconbtn" : "ea-iconbtn ea-iconbtn--off"} onClick={() => newer && trigger(GROUP, "stepElection", 1)}>
        ›
      </div>
    </div>
  );
};
