/**
 * The pure helpers of the graph views (DA-222): the zoom, the Semantic Zoom's levels of detail
 * and the graph page's path. Nothing here reads the API, so a visitor's page can use them.
 */

/** 1 shows the whole graph; 2 shows it twice as large. The zoom is page state, not the URL's. */
export const INITIAL_ZOOM = 1;
export const MIN_ZOOM = 0.25;
export const MAX_ZOOM = 8;
/** How much one press of "Aproximar" or "Afastar" multiplies or divides the zoom. */
export const ZOOM_STEP = 1.5;

/** The zoom one step closer (`in`) or farther (`out`), within MIN_ZOOM and MAX_ZOOM. */
export function steppedZoom(zoom: number, direction: "in" | "out"): number {
  const next = direction === "in" ? zoom * ZOOM_STEP : zoom / ZOOM_STEP;
  return Math.min(MAX_ZOOM, Math.max(MIN_ZOOM, next));
}

/**
 * The three levels of the basic Semantic Zoom (DA-140): how much of a Node the drawing shows.
 * `far` is a dot; `middle` adds its title and its Type's colour; `near` adds a short summary of
 * the Node and the kinds of its Relations. It is detail by zoom and distance from the root, not
 * a conceptual hierarchy: the model has none.
 */
export type LevelOfDetail = "far" | "middle" | "near";
/** From this zoom on, every Node shows its title. */
export const MIDDLE_ZOOM = 1.5;
/** From this zoom on, Nodes show a summary and Relations their kinds. */
export const NEAR_ZOOM = 3;
/** Zoomed out, the Nodes up to this distance from the root keep their titles: the root and its neighbors. */
export const FAR_TITLED_DISTANCE = 1;

/** The level the zoom gives every Node past FAR_TITLED_DISTANCE: what the person is shown. */
export function zoomLevelOf(zoom: number): LevelOfDetail {
  if (zoom >= NEAR_ZOOM) {
    return "near";
  }

  return zoom >= MIDDLE_ZOOM ? "middle" : "far";
}

/** The level of a Node at `distance` from the root, at this zoom. */
export function levelOfDetail(zoom: number, distance: number): LevelOfDetail {
  const level = zoomLevelOf(zoom);
  return level === "far" && distance <= FAR_TITLED_DISTANCE ? "middle" : level;
}

/** The page of the local graph rooted at a Node. */
export function graphPath(nodeId: string): string {
  return `/nodes/${nodeId}/graph`;
}
