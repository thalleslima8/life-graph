import { useParams } from "react-router";
import { NodeInspector } from "./NodeInspector";

/** The Node Inspector page: the embeddable Inspector under the page's h1. */
export function NodeInspectorPage() {
  const { nodeId = "" } = useParams();
  return <NodeInspector nodeId={nodeId} headingLevel={1} />;
}
