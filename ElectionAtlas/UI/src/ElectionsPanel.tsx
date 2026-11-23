import React, { useMemo, useState } from "react";
import { trigger, useValue } from "cs2/api";
import { Panel, Scrollable } from "cs2/ui";
import { citySession$, districts$, GROUP, history$, mapView$, overview$, panelOpen$, polls$, replay$ } from "./bindings";
import { DevToggle, DevWindow } from "./DevSection";
import { DistrictPopup } from "./DistrictPopup";
import { DistrictsTab } from "./DistrictsTab";
import { GovernmentStrip } from "./GovernmentStrip";
import { HistoryTab, PollsTab } from "./OtherTabs";
import { PartiesTab } from "./PartiesTab";
import { MapLegend } from "./MapLegend";
import { ReplayView } from "./ReplayView";

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
  const replay = useValue(replay$);
  const [tab, setTab] = useState<Tab>("districts");
  const [districtId, setDistrictId] = useState<number | null>(null);
  const [devOpen, setDevOpen] = useState(false);
  // Preserve a draft party lineup and leave the idle results table alone while the replay updates.
  const results = useMemo(() => (
    <>
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
    </>
  ), [overview, tab, districts, districtId, history, polls]);
  if (!open) return null;

  const city = overview?.cityName;
  const save = overview?.loadedSaveName;
  const cityContext = city && save && city !== save ? `${city} · Loaded save: ${save}` : city || save;

  // Map view: the panel collapses to a small legend so the colored districts are visible.
  // The tab and popup choice live here, so "Back to results" returns where you were.
  const replayResults = () => {
    trigger(GROUP, "closeReplay");
    if (!replay?.sample) setTab("districts");
  };
  if (mapView) return <MapLegend d={districts} cityContext={cityContext} replay={replay} onResults={replayResults} />;

  return (
    <>
      <Panel draggable header={replay ? "Election Day" : "Elections"} initialPosition={{ x: 0.42, y: 0.25 }} onClose={() => trigger(GROUP, "closePanel")} style={{ width: replay ? "416rem" : "580rem" }}>
        <div className="ea-body">
          {cityContext ? <div className="ea-muted ea-small ea-ellipsis ea-city-context" title={cityContext}>{cityContext}</div> : null}
          {replay ? <ReplayView r={replay} onResults={replayResults} /> : null}
          <div style={{ display: replay ? "none" : undefined }}>{results}</div>

          <DevToggle open={devOpen} onToggle={() => setDevOpen(!devOpen)} />
        </div>
      </Panel>

      {devOpen ? <DevWindow replay={replay !== null} onClose={() => setDevOpen(false)} /> : null}
      {!replay && districtId !== null ? <DistrictPopup d={districts} o={overview} id={districtId} onClose={() => setDistrictId(null)} /> : null}
    </>
  );
};
