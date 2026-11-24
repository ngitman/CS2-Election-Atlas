import React, { useEffect } from "react";
import { trigger, useValue } from "cs2/api";
import { electionNotice$, GROUP } from "./bindings";

export const ElectionNoticePopup = ({ onViewResults }: { onViewResults: () => void }) => {
  const notice = useValue(electionNotice$);
  const id = notice?.id;
  useEffect(() => {
    if (id === undefined) return;
    const timer = setTimeout(() => trigger(GROUP, "dismissElectionNotice", id), 8000);
    return () => clearTimeout(timer);
  }, [id]);

  if (!notice) return null;
  const title = notice.results ? "Results are in" : "Polls have closed";
  const city = notice.cityName ? ` in ${notice.cityName}` : "";
  const detail = notice.results
    ? notice.governingParty ? `${notice.governingParty} will govern${city}.` : "No governing party was elected."
    : `The count is underway${city}.`;

  return (
    <div className="ea-notice">
      <div role="status">
        <div className="ea-bold">{title}</div>
        <div className="ea-notice__detail ea-ellipsis" title={detail}>{detail}</div>
      </div>
      <div className="ea-notice__actions">
        <div className="ea-btn ea-btn--primary" onClick={() => {
          if (notice.results) onViewResults();
          trigger(GROUP, "openElectionNotice", notice.id);
        }}>
          {notice.results ? "View results" : "Watch"}
        </div>
        <div className="ea-btn" onClick={() => trigger(GROUP, "dismissElectionNotice", notice.id)}>Dismiss</div>
      </div>
    </div>
  );
};
