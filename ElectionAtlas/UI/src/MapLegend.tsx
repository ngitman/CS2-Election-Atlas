import React from "react";
import { trigger } from "cs2/api";
import { Panel } from "cs2/ui";
import { Districts, ElectionDayReplay, GROUP } from "./bindings";
import { Dot, ElectionPicker, StackBar } from "./parts";
import { ReplayView } from "./ReplayView";

/**
 * The panel collapsed for the map view: which election the map shows, what the colors mean, and the way back.
 * Closing it ends the map view (the colors go with it).
 */
export const MapLegend = ({ d, cityContext, replay, onResults }: {
  d: Districts | null; cityContext?: string | null; replay: ElectionDayReplay | null; onResults: () => void;
}) => {
  const parties = d?.parties ?? [];
  return (
    <Panel draggable header="Election map" initialPosition={{ x: 0.02, y: 0.2 }} onClose={() => trigger(GROUP, "closePanel")} style={{ width: "300rem" }}>
      <div className="ea-body">
        {cityContext ? <div className="ea-muted ea-small ea-ellipsis ea-city-context" title={cityContext}>{cityContext}</div> : null}
        {replay ? <ReplayView r={replay} map onResults={onResults} /> : (
          <>
            {d && d.count ? (
              <>
                <ElectionPicker d={d} />
                <StackBar parties={parties} shares={parties.map((p) => p.share ?? 0)} columns={2} />
                {parties.map((p) => (
                  <div key={p.id} className="ea-legend-row">
                    <Dot color={p.color} />
                    <span className="ea-legend-row__name">{p.name}</span>
                    <span className="ea-muted ea-small">{`${p.won ?? 0} ${p.won === 1 ? "district" : "districts"}`}</span>
                  </div>
                ))}
                <div className="ea-tint">
                  <div className="ea-tint__bar" />
                  <div className="ea-axis">
                    <span>close race</span>
                    <span>safe seat</span>
                  </div>
                </div>
                <div className="ea-muted ea-small">Grey districts have no result in this election. Click a district for its details.</div>
              </>
            ) : (
              <div className="ea-empty">No election to show yet.</div>
            )}
            <div className="ea-actions">
              <div className="ea-btn ea-btn--primary" onClick={() => trigger(GROUP, "hideMap")}>
                Back to results
              </div>
            </div>
          </>
        )}
      </div>
    </Panel>
  );
};
