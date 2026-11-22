import React from "react";
import { trigger } from "cs2/api";
import { Districts, GROUP, PartyInfo } from "./bindings";
import { monthLabel, pct } from "./format";

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

/** All parties as one segmented bar with a legend underneath. */
export const StackBar = ({ parties, shares }: { parties: PartyInfo[]; shares: number[] }) => (
  <div>
    <div className="ea-stack">
      {parties.map((p, i) => (
        <div key={p.id} style={{ width: `${Math.max(0, shares[i] ?? 0) * 100}%`, backgroundColor: p.color }} />
      ))}
    </div>
    <div className="ea-legend">
      {parties.map((p, i) => (
        <div key={p.id} className="ea-legend__item">
          <Dot color={p.color} />
          <span>{p.name}</span>
          <span className="ea-bold" style={{ marginLeft: "6rem" }}>
            {pct(shares[i] ?? 0)}
          </span>
        </div>
      ))}
    </div>
  </div>
);

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
