import React, { useState } from "react";
import { trigger, useValue } from "cs2/api";
import { Panel, Scrollable } from "cs2/ui";
import { citySession$, districts$, GROUP, history$, mapView$, overview$, panelOpen$, polls$ } from "./bindings";
import { DevSection } from "./DevSection";
import { DistrictPopup } from "./DistrictPopup";
import { DistrictsTab } from "./DistrictsTab";
import { GovernmentStrip } from "./GovernmentStrip";
import { HistoryTab, PollsTab } from "./OtherTabs";
import { PartiesTab } from "./PartiesTab";
import { MapLegend } from "./MapLegend";

type Tab = "districts" | "history" | "polls" | "parties";
const TABS: { id: Tab; label: string }[] = [
  { id: "districts", label: "Districts" },
  { id: "history", label: "History" },
  { id: "polls", label: "Polls" },
  { id: "parties", label: "Parties" },
];

export const ElectionsPanel = () => {
  const citySession = useValue(citySession$);
  return <CityElectionsPanel key={citySession} />;
};

const CityElectionsPanel = () => {
  const open = useValue(panelOpen$);
  const mapView = useValue(mapView$);
  const overview = useValue(overview$);
  const districts = useValue(districts$);
  const history = useValue(history$);
  const polls = useValue(polls$);
  const [tab, setTab] = useState<Tab>("districts");
  const [districtId, setDistrictId] = useState<number | null>(null);
  if (!open) return null;

  const city = overview?.cityName;
  const save = overview?.loadedSaveName;
  const cityContext = city && save && city !== save ? `${city} · Loaded save: ${save}` : city || save;

  // Map view: the panel collapses to a small legend so the colored districts are visible.
  // The tab, popup choice and sort survive (state lives here), so "Back to results" returns where you were.
  if (mapView) return <MapLegend d={districts} cityContext={cityContext} />;

  return (
    <>
      <Panel draggable header="Elections" initialPosition={{ x: 0.42, y: 0.25 }} onClose={() => trigger(GROUP, "closePanel")} style={{ width: "580rem" }}>
        <div className="ea-body">
          {cityContext ? <div className="ea-muted ea-small ea-ellipsis ea-city-context" title={cityContext}>{cityContext}</div> : null}
          <GovernmentStrip o={overview} />

          <div className="ea-tabs">
            {TABS.map((t) => (
              <div key={t.id} className={tab === t.id ? "ea-tab ea-tab--active" : "ea-tab"} onClick={() => setTab(t.id)}>
                {t.label}
              </div>
            ))}
          </div>

          <Scrollable vertical style={{ maxHeight: "560rem" }}>
            {tab === "districts" ? (
              <DistrictsTab d={districts} openId={districtId} onOpen={(id) => setDistrictId(id === districtId ? null : id)} />
            ) : null}
            {tab === "history" ? <HistoryTab h={history} shownIndex={districts?.index} onShow={() => setTab("districts")} /> : null}
            {tab === "polls" ? <PollsTab p={polls} /> : null}
            <div style={{ display: tab === "parties" ? undefined : "none" }}>
              <PartiesTab governingId={overview?.governing?.id} />
            </div>
          </Scrollable>

          <DevSection />
        </div>
      </Panel>

      {districtId !== null ? <DistrictPopup d={districts} o={overview} id={districtId} onClose={() => setDistrictId(null)} /> : null}
    </>
  );
};
