import React, { useState } from "react";
import { trigger, useValue } from "cs2/api";
import { selectedInfo } from "cs2/bindings";
import { Districts, DistrictView, GROUP } from "./bindings";
import { approvalPct, monthLabel, pct, withAlpha } from "./format";
import { StackBar } from "./parts";

/** The atlas: the shown election's citywide result, then every district's result as a table. */
export const DistrictsTab = ({ d, onOpen, openId }: { d: Districts | null; onOpen: (id: number) => void; openId: number | null }) => {
  const selected = useValue(selectedInfo.selectedEntity$);

  if (!d || !d.count) {
    return <div className="ea-empty">No elections yet. The founding election comes one cadence period after the City Hall goes up.</div>;
  }

  const parties = d.parties ?? [];
  const index = d.index ?? 0;
  const older = index > 0;
  const newer = index < d.count - 1;

  return (
    <div>
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

      <StackBar parties={parties} shares={parties.map((p) => p.share ?? 0)} />
      <div className="ea-muted ea-small">
        {`Turnout ${pct(d.turnout)} of ${(d.eligible ?? 0).toLocaleString()} voters. ` +
          (d.governing !== undefined && d.governing >= 0 ? `${parties[d.governing]?.name} formed the government` : "No government formed") +
          (d.rule === "MostDistricts" ? " by winning the most districts." : " on the popular vote.")}
      </div>

      <DistrictTable
        d={d}
        openId={openId}
        selected={selected}
        onOpen={onOpen}
      />
    </div>
  );
};

type SortKey = "name" | "margin" | "turnout" | "approval" | number;
const PAGE_SIZE = 8;

/** Every district as a row: each party's share as a column, then margin, turnout and approval.
 *  Rows are tinted with the district's map color (the winner's color, stronger for safer seats). */
const DistrictTable = ({
  d,
  openId,
  selected,
  onOpen,
}: {
  d: Districts;
  openId: number | null;
  selected: { index: number; version: number } | null;
  onOpen: (id: number) => void;
}) => {
  const [sort, setSort] = useState<{ key: SortKey; desc: boolean }>({ key: "name", desc: false });
  const [page, setPage] = useState(0);
  const parties = d.parties ?? [];
  const rows = d.districts ?? [];

  const share = (x: DistrictView, i: number) => (x.votes > 0 ? (x.partyVotes[i] ?? 0) / x.votes : 0);
  const value = (x: DistrictView): number | string => {
    if (sort.key === "name") return x.name.toLowerCase();
    if (sort.key === "margin") return x.votes > 0 ? x.margin : -1;
    if (sort.key === "turnout") return x.turnout;
    if (sort.key === "approval") return x.approval ?? -1;
    return share(x, sort.key);
  };
  const sorted = rows.slice().sort((a, b) => {
    const va = value(a);
    const vb = value(b);
    const c = va < vb ? -1 : va > vb ? 1 : 0;
    return sort.desc ? -c : c;
  });

  const pages = Math.max(1, Math.ceil(sorted.length / PAGE_SIZE));
  const current = Math.min(page, pages - 1);
  const shown = sorted.slice(current * PAGE_SIZE, current * PAGE_SIZE + PAGE_SIZE);

  // Numbers read best descending, names ascending; clicking the active column flips it.
  const sortBy = (key: SortKey) => {
    setSort((s) => (s.key === key ? { key, desc: !s.desc } : { key, desc: key !== "name" }));
    setPage(0);
  };
  const Head = ({ k, label, className }: { k: SortKey; label: string; className: string }) => (
    <div className={sort.key === k ? `${className} ea-th ea-th--on` : `${className} ea-th`} onClick={() => sortBy(k)}>
      {sort.key === k ? `${label} ${sort.desc ? "▼" : "▲"}` : label}
    </div>
  );

  return (
    <div className="ea-table">
      <div className="ea-tr ea-tr--head">
        <Head k="name" label="District" className="ea-td ea-td--name" />
        {parties.map((p, i) => (
          <Head key={p.id} k={i} label={p.name} className="ea-td ea-td--num" />
        ))}
        <Head k="margin" label="Margin" className="ea-td ea-td--num" />
        <Head k="turnout" label="Turnout" className="ea-td ea-td--num" />
        <Head k="approval" label="Approval" className="ea-td ea-td--num" />
      </div>

      {shown.map((x) => {
        const voted = x.votes > 0 && x.winner >= 0;
        const active = openId === x.id || (selected?.index === x.index && selected?.version === x.version);
        return (
          <div
            key={x.id}
            className={active ? "ea-tr ea-tr--row ea-tr--active" : "ea-tr ea-tr--row"}
            style={{ backgroundColor: voted ? withAlpha(x.color, 0.32) : "rgba(255, 255, 255, 0.03)", borderLeftColor: voted ? x.color : "rgba(255, 255, 255, 0.15)" }}
            onClick={() => onOpen(x.id)}
          >
            <div className="ea-td ea-td--name ea-bold">{x.name}</div>
            {parties.map((p, i) => (
              <div key={p.id} className={voted && i === x.winner ? "ea-td ea-td--num ea-bold" : "ea-td ea-td--num ea-dim"}>
                {voted ? pct(share(x, i)) : "-"}
              </div>
            ))}
            <div className="ea-td ea-td--num">{voted ? `+${(x.margin * 100).toFixed(1)}` : "-"}</div>
            <div className="ea-td ea-td--num">{pct(x.turnout)}</div>
            <div className="ea-td ea-td--num">{approvalPct(x.approval)}</div>
          </div>
        );
      })}

      {pages > 1 ? (
        <div className="ea-pager">
          <div className={current > 0 ? "ea-iconbtn" : "ea-iconbtn ea-iconbtn--off"} onClick={() => current > 0 && setPage(current - 1)}>
            ‹
          </div>
          <div className="ea-pager__label ea-muted">{`${current * PAGE_SIZE + 1}–${current * PAGE_SIZE + shown.length} of ${sorted.length} districts`}</div>
          <div className={current < pages - 1 ? "ea-iconbtn" : "ea-iconbtn ea-iconbtn--off"} onClick={() => current < pages - 1 && setPage(current + 1)}>
            ›
          </div>
        </div>
      ) : (
        <div className="ea-pager__label ea-muted ea-small" style={{ marginTop: "6rem" }}>{`${sorted.length} districts · click a row for details`}</div>
      )}
    </div>
  );
};
