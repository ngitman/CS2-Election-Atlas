import React, { useState } from "react";
import { trigger, useValue } from "cs2/api";
import { selectedInfo } from "cs2/bindings";
import { Districts, DistrictView, GROUP, partyShare } from "./bindings";
import { approvalPct, count, pct, withAlpha } from "./format";
import { ElectionPicker, StackBar } from "./parts";

/** The atlas: the shown election's citywide result, then every district's result as a table. */
export const DistrictsTab = ({ d, onOpen, openId }: { d: Districts | null; onOpen: (id: number) => void; openId: number | null }) => {
  const selected = useValue(selectedInfo.selectedEntity$);

  if (!d || !d.count) {
    return <div className="ea-empty">No elections yet. The founding election comes one cadence period after the City Hall goes up.</div>;
  }

  const parties = d.parties ?? [];

  return (
    <div>
      <ElectionPicker d={d} />

      <StackBar parties={parties} shares={parties.map((p) => p.share ?? 0)} />
      <div className="ea-maprow">
        <div className="ea-muted ea-small">
          {`Turnout ${pct(d.turnout)} of ${count(d.eligible)} voters. ` +
            (d.governing !== undefined && d.governing >= 0 ? `${parties[d.governing]?.name} formed the government` : "No government formed") +
            (d.rule === "MostDistricts" ? " by winning the most districts." : " on the popular vote.")}
        </div>
        <div className="ea-btn ea-btn--primary ea-btn--slim" onClick={() => trigger(GROUP, "showMap")}>
          View on map
        </div>
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

type SortKey = "name" | "votes" | "margin" | "turnout" | "approval" | number;
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
  // Residents outside every drawn district: counted citywide, never a seat, so pinned below the districts.
  const all = d.districts ?? [];
  const rows = all.filter((x) => x.seat);
  const outside = all.find((x) => !x.seat && x.eligible > 0);

  const value = (x: DistrictView): number | string => {
    if (sort.key === "name") return x.name.toLowerCase();
    if (sort.key === "margin") return x.votes > 0 ? x.margin : -1;
    if (sort.key === "votes") return x.votes;
    if (sort.key === "turnout") return x.turnout;
    if (sort.key === "approval") return x.approval ?? -1;
    return partyShare(x, sort.key);
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

  const Cells = ({ x }: { x: DistrictView }) => {
    const voted = x.votes > 0 && x.winner >= 0;
    const seat = x.seat;
    return (
      <>
        <div className="ea-td ea-td--name">
          <div className={seat ? "ea-bold ea-ellipsis" : "ea-dim ea-ellipsis"}>{x.name}</div>
          {x.profile ? <div className="ea-muted ea-small ea-ellipsis">{x.profile}</div> : null}
        </div>
        {parties.map((p, i) => (
          <div key={p.id} className={voted && seat && i === x.winner ? "ea-td ea-td--num ea-bold" : "ea-td ea-td--num ea-dim"}>
            {voted ? pct(partyShare(x, i)) : "-"}
          </div>
        ))}
        <div className="ea-td ea-td--num">{count(x.votes)}</div>
        <div className="ea-td ea-td--num">{voted && seat ? `+${(x.margin * 100).toFixed(1)}` : "-"}</div>
        <div className="ea-td ea-td--num">{pct(x.turnout)}</div>
        <div className="ea-td ea-td--num">{approvalPct(x.approval)}</div>
      </>
    );
  };

  return (
    <div className="ea-table">
      <div className="ea-tr ea-tr--head">
        <Head k="name" label="District" className="ea-td ea-td--name" />
        {parties.map((p, i) => (
          <Head key={p.id} k={i} label={p.name} className="ea-td ea-td--num" />
        ))}
        <Head k="votes" label="Votes" className="ea-td ea-td--num" />
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
            <Cells x={x} />
          </div>
        );
      })}

      {outside ? (
        <div
          className={openId === outside.id ? "ea-tr ea-tr--row ea-tr--outside ea-tr--active" : "ea-tr ea-tr--row ea-tr--outside"}
          onClick={() => onOpen(outside.id)}
        >
          <Cells x={outside} />
        </div>
      ) : null}

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
        <div className="ea-pager__label ea-muted ea-small" style={{ marginTop: "6rem" }}>{`${sorted.length} ${sorted.length === 1 ? "district" : "districts"} · click a row for details`}</div>
      )}
    </div>
  );
};
