import React from "react";
import { trigger, useValue } from "cs2/api";
import { selectedInfo } from "cs2/bindings";
import { Districts, DistrictView, GROUP } from "./bindings";
import { inkFor, monthLabel, pct } from "./format";
import { StackBar } from "./parts";

/** The atlas: the shown election's citywide result, then one tile per district colored by its winner. */
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

      <div className="ea-tiles">
        {(d.districts ?? []).map((district) => (
          <Tile
            key={district.id}
            district={district}
            winnerName={district.winner >= 0 ? parties[district.winner]?.name ?? "" : ""}
            open={openId === district.id}
            selectedInCity={selected?.index === district.index && selected?.version === district.version}
            onClick={() => onOpen(district.id)}
          />
        ))}
      </div>
    </div>
  );
};

const Tile = ({
  district,
  winnerName,
  open,
  selectedInCity,
  onClick,
}: {
  district: DistrictView;
  winnerName: string;
  open: boolean;
  selectedInCity: boolean;
  onClick: () => void;
}) => {
  const ink = inkFor(district.color);
  const hasResult = district.votes > 0 && district.winner >= 0;
  return (
    <div className={open || selectedInCity ? "ea-tile ea-tile--selected" : "ea-tile"} onClick={onClick}>
      <div className="ea-tile__inner" style={{ backgroundColor: district.color, color: ink }}>
        <div className="ea-tile__name">{district.name}</div>
        <div className="ea-tile__share">{hasResult ? `${winnerName} ${pct(district.winnerShare)}` : "No votes"}</div>
        <div className="ea-tile__meta" style={{ opacity: 0.85 }}>
          <span>{hasResult ? `+${(district.margin * 100).toFixed(1)}` : ""}</span>
          <span>{district.approval !== null ? `approval ${Math.round(district.approval)}` : ""}</span>
        </div>
      </div>
    </div>
  );
};
