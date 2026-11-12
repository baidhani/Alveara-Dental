import tealDoodle from "../../assets/login/tile-teal-doodle.jpg";
import blueDoodle from "../../assets/login/tile-blue-doodle.jpg";
import blueOperatory from "../../assets/login/tile-blue-operatory.jpg";
import violetOperatory from "../../assets/login/tile-violet-operatory.jpg";
import neonDoodle from "../../assets/login/tile-neon-doodle.jpg";

/** ALV-N020-C01: how a background fills the sign-in page. */
export type LoginDisplayMode = "Tile" | "Stretch" | "Fill";
export const DISPLAY_MODES: readonly LoginDisplayMode[] = ["Tile", "Stretch", "Fill"];

export interface BundledBackground {
  key: string;
  name: string;
  url: string;
  /** Painted under the image (its dominant colour): what shows if the image cannot load. */
  fallbackColor: string;
}

/**
 * The five owner-supplied backgrounds bundled with the application (same origin, content-hashed by the build). Must list exactly the keys of the server's
 * LoginBackgroundCatalog.Bundled (a server test reads this file). The first is the deployment default.
 */
export const BUNDLED_BACKGROUNDS: readonly BundledBackground[] = [
  { key: "teal-doodle", name: "Teal doodle", url: tealDoodle, fallbackColor: "#042B30" },
  { key: "blue-doodle", name: "Blue doodle", url: blueDoodle, fallbackColor: "#0D314E" },
  { key: "blue-operatory", name: "Blue operatory", url: blueOperatory, fallbackColor: "#0B2A50" },
  { key: "violet-operatory", name: "Violet operatory", url: violetOperatory, fallbackColor: "#2D1F52" },
  { key: "neon-doodle", name: "Neon doodle", url: neonDoodle, fallbackColor: "#000000" },
];

export const DEFAULT_BACKGROUND = BUNDLED_BACKGROUNDS[0];

export function bundledBackground(key: string | null | undefined): BundledBackground | undefined {
  return BUNDLED_BACKGROUNDS.find((b) => b.key === key);
}

/** The CSS for a background in a display mode. Tile repeats at natural size; Stretch fills exactly (may distort); Fill covers, keeps proportions, may crop. */
export function backgroundCss(url: string, mode: LoginDisplayMode, fallbackColor: string): Record<string, string> {
  const base = { backgroundColor: fallbackColor, backgroundImage: `url("${url}")` };
  if (mode === "Stretch") return { ...base, backgroundSize: "100% 100%", backgroundRepeat: "no-repeat" };
  if (mode === "Fill") return { ...base, backgroundSize: "cover", backgroundPosition: "center", backgroundRepeat: "no-repeat" };
  return { ...base, backgroundSize: "auto", backgroundRepeat: "repeat" };
}
