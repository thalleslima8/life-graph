import { useEffect, useRef } from "react";
import { Button } from "@/components/ui/button";
import type { VisitorNodeDetails } from "./visitorApi";
import {
  LINK_UNAVAILABLE_MESSAGE,
  VISITOR_BODY_LABEL,
  VISITOR_RESOURCE_LABEL,
  VISITOR_INSPECTOR_CLOSE_LABEL,
  VISITOR_INSPECTOR_LABEL,
  VISITOR_LOADING_MESSAGE,
  VISITOR_NO_TYPE_LABEL,
  VISITOR_PROPERTIES_LABEL,
  VISITOR_RELATIONS_LABEL,
  VISITOR_TAGS_LABEL,
} from "./visitorMessages";

/** The member the visitor opened, as the page read it; its Relations already in words. */
export type VisitorInspectedNode =
  | { status: "loading" }
  | { status: "unavailable" }
  | {
      status: "ready";
      details: VisitorNodeDetails;
      relations: { id: string; text: string }[];
    };

export type VisitorNodeInspectorProps = {
  node: VisitorInspectedNode;
  /** The level of the Node's title among the page's headings. */
  headingLevel: 2 | 3;
  onClose: () => void;
};

/**
 * A member of a SharedGraphView, read-only (DA-077, DA-222): only what the share allows, as
 * plain text (FE-050), with no edit control. Any failure is the page's one neutral state.
 */
export function VisitorNodeInspector({
  node,
  headingLevel,
  onClose,
}: VisitorNodeInspectorProps) {
  const Heading = headingLevel === 2 ? "h2" : "h3";
  const Section = headingLevel === 2 ? "h3" : "h4";
  const headingRef = useRef<HTMLHeadingElement>(null);
  const shownId = node.status === "ready" ? node.details.id : undefined;
  // Opening a Node moves the reader to it (FE-032).
  useEffect(() => {
    if (shownId !== undefined) {
      headingRef.current?.focus();
    }
  }, [shownId]);

  return (
    <section
      aria-label={VISITOR_INSPECTOR_LABEL}
      className="space-y-4 rounded-md border p-4"
    >
      {node.status === "loading" && (
        <p role="status" className="text-sm text-muted-foreground">
          {VISITOR_LOADING_MESSAGE}
        </p>
      )}
      {node.status === "unavailable" && (
        <p role="alert">{LINK_UNAVAILABLE_MESSAGE}</p>
      )}
      {node.status === "ready" && (
        <>
          <div className="space-y-1">
            <Heading
              ref={headingRef}
              tabIndex={-1}
              className="text-xl font-semibold focus-visible:outline-none"
            >
              {node.details.title}
            </Heading>
            <p className="text-sm text-muted-foreground">
              {node.details.typeName ?? VISITOR_NO_TYPE_LABEL}
            </p>
          </div>
          {node.details.resourceUrl !== null && (
            <div className="space-y-1">
              <Section className="text-sm font-medium">
                {VISITOR_RESOURCE_LABEL}
              </Section>
              <a
                href={node.details.resourceUrl}
                target="_blank"
                rel="noopener noreferrer"
                className="break-all underline underline-offset-4"
              >
                {node.details.resourceUrl}
              </a>
            </div>
          )}
          {node.details.body && (
            <div className="space-y-1">
              <Section className="text-sm font-medium">
                {VISITOR_BODY_LABEL}
              </Section>
              <p className="whitespace-pre-wrap">{node.details.body}</p>
            </div>
          )}
          {node.details.properties.length > 0 && (
            <div className="space-y-1">
              <Section className="text-sm font-medium">
                {VISITOR_PROPERTIES_LABEL}
              </Section>
              <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 text-sm">
                {node.details.properties.map((property) => (
                  <div key={property.name} className="contents">
                    <dt className="font-medium">{property.name}</dt>
                    <dd className="break-words">{property.value}</dd>
                  </div>
                ))}
              </dl>
            </div>
          )}
          {node.details.tags.length > 0 && (
            <div className="space-y-1">
              <Section className="text-sm font-medium">
                {VISITOR_TAGS_LABEL}
              </Section>
              <ul className="flex flex-wrap gap-2 text-sm">
                {node.details.tags.map((tag) => (
                  <li key={tag} className="rounded-sm border px-2 py-0.5">
                    {tag}
                  </li>
                ))}
              </ul>
            </div>
          )}
          {node.relations.length > 0 && (
            <div className="space-y-1">
              <Section className="text-sm font-medium">
                {VISITOR_RELATIONS_LABEL}
              </Section>
              <ul className="list-disc space-y-0.5 pl-4 text-sm">
                {node.relations.map((relation) => (
                  <li key={relation.id}>{relation.text}</li>
                ))}
              </ul>
            </div>
          )}
        </>
      )}
      <Button type="button" variant="secondary" size="sm" onClick={onClose}>
        {VISITOR_INSPECTOR_CLOSE_LABEL}
      </Button>
    </section>
  );
}
