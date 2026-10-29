import type { components } from "@/api/schema.gen";
import type { RenderableGraph } from "@/features/graph/graphShape";
import { PROPERTY_FALSE_TEXT, PROPERTY_TRUE_TEXT } from "./visitorMessages";

/**
 * The visitor's whole client (DA-215, DA-216, DA-222): two reads of `/api/shared`, the link's
 * token in the Authorization header, never a cookie, a cache or a Referer. It is the visitor
 * bundle's single API layer; the app's authenticated client never enters this bundle.
 */

type Schemas = components["schemas"];

/**
 * Any failure of a link: the visitor is never told why (DA-079), so there is one kind. The
 * original failure stays in `cause` for debugging (GEN-033) and is never shown.
 */
export class SharedLinkUnavailable extends Error {
  constructor(options?: ErrorOptions) {
    super("The shared link is unavailable.", options);
    this.name = "SharedLinkUnavailable";
  }
}

export type VisitorGraphView = { publicRef: string; graph: RenderableGraph };

export type VisitorNodeDetails = {
  id: string;
  title: string;
  body: string | null;
  typeName: string | null;
  /** The Resource's link (DA-077); `null` when the Node is not a Resource or the link is not http(s). */
  resourceUrl: string | null;
  properties: { name: string; value: string }[];
  tags: string[];
  relations: {
    id: string;
    sourceNodeId: string;
    kind: string;
    targetNodeId: string;
  }[];
};

/** The members of the share and the Hard Relations among them. */
export async function fetchSharedGraph(
  token: string,
  signal?: AbortSignal,
): Promise<VisitorGraphView> {
  return toVisitorGraphView(
    await getShared<Schemas["VisitorGraph"]>("/api/shared", token, signal),
  );
}

/** One member, with what the share allows of it (DA-077). */
export async function fetchSharedNode(
  token: string,
  nodeId: string,
  signal?: AbortSignal,
): Promise<VisitorNodeDetails> {
  return toVisitorNodeDetails(
    await getShared<Schemas["VisitorNode"]>(
      `/api/shared/nodes/${encodeURIComponent(nodeId)}`,
      token,
      signal,
    ),
  );
}

async function getShared<T>(
  path: string,
  token: string,
  signal: AbortSignal | undefined,
): Promise<T> {
  let response: Response;
  try {
    response = await fetch(path, {
      headers: { Accept: "application/json", Authorization: `Bearer ${token}` },
      credentials: "omit",
      cache: "no-store",
      referrerPolicy: "no-referrer",
      ...(signal ? { signal } : {}),
    });
  } catch (error) {
    if (signal?.aborted) {
      throw error;
    }

    throw new SharedLinkUnavailable({ cause: error });
  }

  if (!response.ok) {
    throw new SharedLinkUnavailable({ cause: response.status });
  }

  try {
    return (await response.json()) as T;
  } catch (error) {
    throw new SharedLinkUnavailable({ cause: error });
  }
}

/**
 * The visitor's DTO as the drawing and the list take it, field by field (DA-077): whatever else
 * the answer carries is dropped, never spread into the page.
 */
export function toVisitorGraphView(
  wire: Schemas["VisitorGraph"],
): VisitorGraphView {
  return {
    publicRef: wire.publicRef,
    graph: {
      rootId: wire.rootNodeId,
      // A member no shown Relation reaches has no distance; it is listed after the farthest.
      nodes: wire.nodes.map((node) => ({
        id: node.id,
        title: node.title,
        typeName: node.typeName,
        distance:
          node.distance === null
            ? unreachedDistance(wire.nodes)
            : Number(node.distance),
      })),
      edges: wire.relations.map((relation) => ({
        id: relation.id,
        sourceNodeId: relation.sourceNodeId,
        targetNodeId: relation.targetNodeId,
        kind: relation.kind,
      })),
    },
  };
}

export function toVisitorNodeDetails(
  wire: Schemas["VisitorNode"],
): VisitorNodeDetails {
  return {
    id: wire.id,
    title: wire.title,
    body: wire.body,
    typeName: wire.typeName,
    resourceUrl: webLink(wire.resourceUrl),
    properties: wire.properties.map((property) => ({
      name: property.name,
      value: propertyText(property.value),
    })),
    tags: wire.tags.map((tag) => tag),
    relations: wire.relations.map((relation) => ({
      id: relation.id,
      sourceNodeId: relation.sourceNodeId,
      kind: relation.kind,
      targetNodeId: relation.targetNodeId,
    })),
  };
}

const WEB_LINK_PROTOCOLS = new Set(["http:", "https:"]);

/** Only an http(s) link becomes an anchor: never a `javascript:` or `data:` one (FE-050). */
function webLink(url: string | null): string | null {
  if (url === null || !URL.canParse(url)) {
    return null;
  }

  return WEB_LINK_PROTOCOLS.has(new URL(url).protocol) ? url : null;
}

function unreachedDistance(
  nodes: readonly Schemas["VisitorGraphNode"][],
): number {
  return (
    Math.max(
      0,
      ...nodes.map((node) =>
        node.distance === null ? 0 : Number(node.distance),
      ),
    ) + 1
  );
}

const numberFormat = new Intl.NumberFormat("pt-BR");

/** A Property's value as plain text: the page never renders a value as HTML (FE-050). */
function propertyText(value: unknown): string {
  if (typeof value === "string") {
    return value;
  }

  if (typeof value === "number") {
    return numberFormat.format(value);
  }

  if (typeof value === "boolean") {
    return value ? PROPERTY_TRUE_TEXT : PROPERTY_FALSE_TEXT;
  }

  if (Array.isArray(value)) {
    return value.map(propertyText).join(", ");
  }

  return value === null || value === undefined ? "" : JSON.stringify(value);
}
