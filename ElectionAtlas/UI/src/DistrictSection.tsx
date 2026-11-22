import React from "react";
import { PanelSection, PanelSectionRow } from "cs2/ui";
import { DistrictView, ElectionSummary, partyShare } from "./bindings";
import { approvalPct, count, monthLabel, pct, points } from "./format";
import { Dot } from "./parts";

/** Props written by DistrictElectionSection.OnWriteProperties in C# (shapes shared with the panel). */
interface DistrictSectionProps {
  hasData: boolean;
  governing?: string;
  governingColor?: string;
  district?: DistrictView;
  election?: ElectionSummary | null;
}

/**
 * The "Election" section in the game's own info panel when a district is selected in the city.
 * Uses the game's PanelSection / PanelSectionRow (the same components as its other info sections).
 */
export const DistrictSection = (props: DistrictSectionProps) => {
  const d = props.district;
  if (!props.hasData || !d) return null;
  const e = props.election;
  const parties = e?.parties ?? [];
  const winner = d.winner >= 0 ? parties[d.winner] : undefined;

  return (
    <PanelSection>
      <PanelSectionRow uppercase left="Election" right={e ? monthLabel(e.month) : "No election yet"} />
      {d.profile ? <PanelSectionRow left="Profile" right={d.profile} /> : null}
      {e && winner && d.votes > 0 ? (
        <PanelSectionRow
          left={
            <div className="ea-party">
              <Dot color={winner.color} />
              {`${winner.name} won`}
            </div>
          }
          right={`${pct(d.winnerShare)}, by ${points(d.margin)}`}
        />
      ) : null}
      {e
        ? parties.map((p, i) => (
            <PanelSectionRow
              key={p.id}
              subRow
              left={
                <div className="ea-party">
                  <Dot color={p.color} />
                  {p.name}
                </div>
              }
              right={pct(partyShare(d, i))}
            />
          ))
        : null}
      {e ? <PanelSectionRow left="Turnout" right={`${pct(d.turnout)} of ${count(d.eligible)}`} /> : null}
      <PanelSectionRow
        left={props.governing ? `Approval of ${props.governing}` : "Approval"}
        right={d.approval !== null ? approvalPct(d.approval) : "not polled yet"}
      />
    </PanelSection>
  );
};
