import React from "react";
import { PanelSection, PanelSectionRow } from "cs2/ui";
import { approvalPct, monthLabel, pct, points } from "./format";
import { Dot } from "./parts";

/** Props written by DistrictElectionSection.OnWriteProperties in C#. */
interface DistrictSectionProps {
  hasData: boolean;
  governing?: string;
  governingColor?: string;
  approval?: number | null;
  trend?: (number | null)[];
  election?: {
    number: number;
    month: number;
    turnout: number;
    eligible: number;
    votes: number;
    winner: number;
    margin: number;
    color: string;
    parties: { name: string; color: string; share: number; votes: number }[];
  } | null;
}

/**
 * The "Election" section in the game's own info panel when a district is selected in the city.
 * Uses the game's PanelSection / PanelSectionRow (the same components as its other info sections).
 */
export const DistrictSection = (props: DistrictSectionProps) => {
  if (!props.hasData) return null;
  const e = props.election;
  const winner = e && e.winner >= 0 ? e.parties[e.winner] : undefined;

  return (
    <PanelSection>
      <PanelSectionRow uppercase left="Election" right={e ? monthLabel(e.month) : "No election yet"} />
      {e && winner && e.votes > 0 ? (
        <PanelSectionRow
          left={
            <div className="ea-party">
              <Dot color={winner.color} />
              {`${winner.name} won`}
            </div>
          }
          right={`${pct(winner.share)}, by ${points(e.margin)}`}
        />
      ) : null}
      {e
        ? e.parties.map((p) => (
            <PanelSectionRow
              key={p.name}
              subRow
              left={
                <div className="ea-party">
                  <Dot color={p.color} />
                  {p.name}
                </div>
              }
              right={pct(p.share)}
            />
          ))
        : null}
      {e ? <PanelSectionRow left="Turnout" right={`${pct(e.turnout)} of ${e.eligible.toLocaleString()}`} /> : null}
      <PanelSectionRow
        left={props.governing ? `Approval of ${props.governing}` : "Approval"}
        right={props.approval !== null && props.approval !== undefined ? approvalPct(props.approval) : "not polled yet"}
      />
    </PanelSection>
  );
};
