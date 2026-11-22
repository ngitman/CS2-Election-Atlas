import React, { useState } from "react";
import { trigger, useValue } from "cs2/api";
import { Panel } from "cs2/ui";
import { Districts, districts$, GROUP, History, history$, Overview, overview$, panelOpen$, Polls, polls$, useJson } from "./bindings";
import { DevSection } from "./DevSection";
import { DistrictPopup } from "./DistrictPopup";
import { DistrictsTab } from "./DistrictsTab";
import { GovernmentStrip } from "./GovernmentStrip";
import { HistoryTab, PartiesTab, PollsTab } from "./OtherTabs";

type Tab = "districts" | "history" | "polls" | "parties";
const TABS: { id: Tab; label: string }[] = [
  { id: "districts", label: "Districts" },
  { id: "history", label: "History" },
  { id: "polls", label: "Polls" },
  { id: "parties", label: "Parties" },
];

export const ElectionsPanel = () => {
  const open = useValue(panelOpen$);
  const overview = useJson<Overview>(overview$);
  const districts = useJson<Districts>(districts$);
  const history = useJson<History>(history$);
  const polls = useJson<Polls>(polls$);
  const [tab, setTab] = useState<Tab>("districts");
  const [districtId, setDistrictId] = useState<number | null>(null);
  if (!open) return null;

  return (
    <>
      <Panel draggable header="Elections" initialPosition={{ x: 0.42, y: 0.25 }} onClose={() => trigger(GROUP, "closePanel")} style={{ width: "580rem" }}>
        <div className="ea-body">
          <GovernmentStrip o={overview} />

          <div className="ea-tabs">
            {TABS.map((t) => (
              <div key={t.id} className={tab === t.id ? "ea-tab ea-tab--active" : "ea-tab"} onClick={() => setTab(t.id)}>
                {t.label}
              </div>
            ))}
          </div>

          <div style={{ maxHeight: "560rem", overflowY: "auto" }}>
            {tab === "districts" ? (
              <DistrictsTab d={districts} openId={districtId} onOpen={(id) => setDistrictId(id === districtId ? null : id)} />
            ) : null}
            {tab === "history" ? <HistoryTab h={history} shownIndex={districts?.index} onShow={() => setTab("districts")} /> : null}
            {tab === "polls" ? <PollsTab p={polls} /> : null}
            {tab === "parties" ? <PartiesTab o={overview} /> : null}
          </div>

          <DevSection />
        </div>
      </Panel>

      {districtId !== null ? <DistrictPopup d={districts} o={overview} id={districtId} onClose={() => setDistrictId(null)} /> : null}
    </>
  );
};
