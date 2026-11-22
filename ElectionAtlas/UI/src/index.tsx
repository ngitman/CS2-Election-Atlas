import { ModRegistrar } from "cs2/modding";
import { DistrictSection } from "./DistrictSection";
import { ElectionsPanel } from "./ElectionsPanel";
import { ToolbarButton } from "./ToolbarButton";

// Tells the game to load ElectionAtlas.css next to this module (webpack emits it from src/styles.css).
export const hasCSS = true;

// Where the game keeps the components for its selected-info sections, keyed by C# type name.
const SECTIONS_MODULE = "game-ui/game/components/selected-info-panel/selected-info-sections/selected-info-sections.tsx";
const DISTRICT_SECTION_TYPE = "ElectionAtlas.Systems.DistrictElectionSection";

const register: ModRegistrar = (moduleRegistry) => {
  moduleRegistry.append("GameTopLeft", ToolbarButton);
  moduleRegistry.append("Game", ElectionsPanel);

  // "Election" section in the info panel of a district selected in the city.
  moduleRegistry.extend(SECTIONS_MODULE, "selectedInfoSectionComponents", (components: any) => {
    components[DISTRICT_SECTION_TYPE] = (props: any) => DistrictSection(props);
    return components;
  });
};

export default register;
