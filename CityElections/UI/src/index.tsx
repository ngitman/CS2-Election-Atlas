import { ModRegistrar } from "cs2/modding";
import { ElectionsPanel } from "./ElectionsPanel";
import { ToolbarButton } from "./ToolbarButton";

const register: ModRegistrar = (moduleRegistry) => {
  moduleRegistry.append("GameTopLeft", ToolbarButton);
  moduleRegistry.append("Game", ElectionsPanel);
};

export default register;
