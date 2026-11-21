import React from "react";
import { trigger, useValue } from "cs2/api";
import { FloatingButton } from "cs2/ui";
import { GROUP, panelOpen$ } from "./bindings";

// Built-in game icon, so the mod ships no image assets yet.
const ICON = "Media/Game/Icons/Administration.svg";

export const ToolbarButton = () => {
  const open = useValue(panelOpen$);
  return <FloatingButton src={ICON} selected={open} onSelect={() => trigger(GROUP, "togglePanel")} />;
};
