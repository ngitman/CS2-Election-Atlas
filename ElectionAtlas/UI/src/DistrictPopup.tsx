import React from "react";
import { camera, selectedInfo } from "cs2/bindings";
import { Panel } from "cs2/ui";
import { Districts, Overview } from "./bindings";
import { approvalPct, count, monthLabel, pct, points } from "./format";
import { InfoLine, ShareBar, Sparkline } from "./parts";

/** A floating card for one district: how it voted, and how it feels about the government now. */
export const DistrictPopup = ({ d, o, id, onClose }: { d: Districts | null; o: Overview | null; id: number; onClose: () => void }) => {
  const district = d?.districts?.find((x) => x.id === id);
  if (!d || !district) return null;

  const parties = d.parties ?? [];
  const winner = district.winner >= 0 ? parties[district.winner] : undefined;
  const governing = o?.governing;
  const trend = district.trend ?? [];

  const showInCity = () => {
    const entity = { index: district.index, version: district.version };
    selectedInfo.selectEntity(entity);
    camera.focusEntity(entity);
  };

  return (
    <Panel draggable header={district.name} initialPosition={{ x: 0.82, y: 0.3 }} onClose={onClose} style={{ width: "380rem" }}>
      <div className="ea-body">
        {district.id === -1 ? (
          <div className="ea-hero__caption ea-muted">
            {`Residents whose homes are outside every drawn district. Their votes count toward the city total in election ${d.number}, but there is no seat here to win.`}
          </div>
        ) : winner && district.votes > 0 ? (
          <>
            <div className="ea-hero" style={{ color: winner.color }}>
              {pct(district.winnerShare)}
            </div>
            <div className="ea-hero__caption">
              <span className="ea-bold">{winner.name}</span>
              <span className="ea-muted">{` won by ${points(district.margin)} in election ${d.number} (${monthLabel(d.month)})`}</span>
            </div>
          </>
        ) : (
          <div className="ea-hero__caption ea-muted">Nobody voted here in election {d.number}.</div>
        )}

        {parties.map((p, i) => {
          const share = district.votes > 0 ? (district.partyVotes[i] ?? 0) / district.votes : 0;
          return <ShareBar key={p.id} name={p.name} color={p.color} share={share} suffix={`${count(district.partyVotes[i])} votes`} />;
        })}

        <div className="ea-section">
          <InfoLine label="Turnout">{pct(district.turnout)}</InfoLine>
          <InfoLine label="Votes cast">{`${count(district.votes)} of ${count(district.eligible)}`}</InfoLine>
          <InfoLine label={governing ? `Approval of ${governing.name} now` : "Approval now"}>
            {district.approval !== null ? approvalPct(district.approval) : "not polled yet"}
          </InfoLine>
        </div>

        {trend.length > 1 ? (
          <div className="ea-section">
            <div className="ea-muted ea-small">Approval, last {trend.length} months</div>
            <Sparkline values={trend} />
          </div>
        ) : null}

        <div className="ea-actions">
          {district.selectable ? (
            <div className="ea-btn ea-btn--primary" onClick={showInCity}>
              Show in city
            </div>
          ) : null}
          <div className="ea-btn" onClick={onClose}>
            Close
          </div>
        </div>
      </div>
    </Panel>
  );
};
