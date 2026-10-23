import { useInfiniteQuery, useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { Link, useNavigate, useParams } from "react-router";
import { Button } from "@/components/ui/button";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { isNotFound, nodeQuery, propertyDefinitionsQuery, typesQuery, type NodeSummary } from "@/features/graph/graphData";
import { LOAD_ERROR_MESSAGE, writeErrorMessage } from "@/features/graph/graphMessages";
import { PropertyValueDisplay } from "@/features/graph/PropertyValueEditor";
import { useChangeSetProbe } from "@/features/graph/recentChanges";
import { tagsQuery } from "@/features/tags/tagsData";
import { CollectionForm } from "./CollectionForm";
import { DYNAMIC_EMPTY_MESSAGE, MANUAL_EMPTY_MESSAGE, NAV_FULL_HINT, NEEDS_REVIEW_LABEL, reviewText } from "./collectionMessages";
import {
  collectionMembersQuery,
  collectionQuery,
  collectionsQuery,
  criteriaFromWire,
  isManual,
  NAV_PINNED_LIMIT,
  useDeleteCollection,
  useRemoveFromCollection,
  useUpdateCollection,
  withPinned,
  type CollectionItem,
  type Criterion,
} from "./collectionsData";

/** One Collection: its criteria, read-only until edited, and its members, newest first (DA-164). */
export function CollectionPage() {
  const { collectionId = "" } = useParams();
  const collection = useQuery(collectionQuery(collectionId));

  if (collection.isPending) {
    return <p role="status">Carregando…</p>;
  }

  if (collection.isError) {
    return isNotFound(collection.error) ? (
      <section aria-labelledby="collection-title" className="space-y-4">
        <h1 id="collection-title" className="text-2xl font-semibold">
          Collection não encontrada
        </h1>
        <p>
          <Link to="/collections" className="underline underline-offset-4">
            Voltar para as Collections
          </Link>
        </p>
      </section>
    ) : (
      <div className="space-y-2">
        <p role="alert" className="text-sm text-destructive">
          {LOAD_ERROR_MESSAGE}
        </p>
        <Button type="button" variant="secondary" size="sm" onClick={() => void collection.refetch()}>
          Tentar de novo
        </Button>
      </div>
    );
  }

  return <ReadyCollection collection={collection.data} />;
}

function ReadyCollection({ collection }: { collection: CollectionItem }) {
  const navigate = useNavigate();
  const update = useUpdateCollection(collection.id);
  const remove = useDeleteCollection(collection.id);
  const collections = useQuery(collectionsQuery);
  const [editing, setEditing] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const review = collection.needsReview ?? [];
  const navFull = !collection.pinned && (collections.data ?? []).filter((other) => other.pinned).length >= NAV_PINNED_LIMIT;
  const initial = {
    name: collection.name,
    pinned: collection.pinned,
    criteria: collection.criteria ? criteriaFromWire(collection.criteria) : undefined,
  };

  return (
    <section aria-labelledby="collection-title" className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h1 id="collection-title" className="text-2xl font-semibold">
          {collection.name}
        </h1>
        <div className="flex flex-wrap gap-2">
          <Button
            type="button"
            variant="secondary"
            size="sm"
            aria-pressed={collection.pinned}
            aria-describedby={navFull ? "collection-pin-hint" : undefined}
            disabled={update.isPending}
            onClick={() => update.mutate(withPinned(collection, !collection.pinned))}
          >
            {collection.pinned ? "Desafixar do menu" : "Fixar no menu"}
          </Button>
          <Button type="button" variant="secondary" size="sm" aria-expanded={editing} onClick={() => setEditing((open) => !open)}>
            Editar
          </Button>
          <Button type="button" variant="ghost" size="sm" onClick={() => setConfirming(true)}>
            Apagar
          </Button>
        </div>
      </div>
      {navFull && (
        <p id="collection-pin-hint" className="text-sm text-muted-foreground">
          {NAV_FULL_HINT}
        </p>
      )}
      {update.isError && !editing && (
        <p role="alert" className="text-sm text-destructive">
          {writeErrorMessage(update.error)}
        </p>
      )}
      <p className="text-sm text-muted-foreground">{isManual(collection) ? "Collection manual" : "Collection dinâmica"}</p>
      {review.length > 0 && (
        <div role="note" className="rounded-md border border-destructive p-3 text-sm text-destructive">
          <p className="font-medium">
            <span aria-hidden="true">⚠ </span>
            {NEEDS_REVIEW_LABEL}: estes critérios não encontram nenhum Node até serem revisados.
          </p>
          <ul>
            {review.map((item) => (
              <li key={Number(item.criterionIndex)}>{reviewText(item)}</li>
            ))}
          </ul>
        </div>
      )}
      {editing ? (
        <CollectionForm
          idPrefix="edit-collection"
          label="Editar Collection"
          submitLabel="Salvar"
          initial={initial}
          kindFixed
          navFull={navFull}
          invalid={review}
          action={{
            isPending: update.isPending,
            error: update.error,
            submit: (input) => update.mutate(input, { onSuccess: () => setEditing(false) }),
          }}
        />
      ) : (
        collection.criteria && (
          <CriteriaSummary criteria={criteriaFromWire(collection.criteria).criteria} match={collection.criteria.match} />
        )
      )}
      <Members collection={collection} />
      <ConfirmDialog
        open={confirming}
        onOpenChange={(open) => {
          setConfirming(open);
          if (!open) {
            remove.reset();
          }
        }}
        title={`Apagar a Collection ${collection.name}?`}
        description="Os Nodes continuam no grafo; desfazível em Recent Changes."
        confirmLabel="Apagar Collection"
        pending={remove.isPending}
        error={remove.error ? writeErrorMessage(remove.error) : undefined}
        onConfirm={() => remove.mutate(undefined, { onSuccess: () => void navigate("/collections") })}
      />
    </section>
  );
}

/** The criteria in words, by the names the cache has; a missing reference reads as such. */
function CriteriaSummary({ criteria, match }: { criteria: Criterion[]; match: "all" | "any" }) {
  return (
    <section aria-labelledby="criteria-title" className="space-y-2">
      <h2 id="criteria-title" className="text-lg font-semibold">
        Critérios
      </h2>
      <p className="text-sm">
        {match === "all" ? "Um Node entra quando atende a todos os critérios:" : "Um Node entra quando atende a qualquer um dos critérios:"}
      </p>
      <ol className="list-decimal space-y-1 pl-6 text-sm">
        {criteria.map((criterion, index) => (
          <li key={index}>
            <CriterionText criterion={criterion} />
          </li>
        ))}
      </ol>
    </section>
  );
}

const MISSING = "(não existe mais)";

function CriterionText({ criterion }: { criterion: Criterion }) {
  const types = useQuery(typesQuery);
  const tags = useQuery(tagsQuery);
  const definitions = useQuery(propertyDefinitionsQuery);

  switch (criterion.type) {
    case "type":
      return <>Type é {types.data?.find((type) => type.id === criterion.typeId)?.name ?? MISSING}</>;
    case "tag": {
      const tag = tags.data?.find((candidate) => candidate.id === criterion.tagId);
      return <>Tem a Tag {tag ? `#${tag.name}` : MISSING}</>;
    }
    case "property": {
      const definition = definitions.data?.find((candidate) => candidate.id === criterion.propertyId);
      if (!definition) {
        return <>Propriedade {MISSING}</>;
      }

      if (criterion.op === "is_empty") {
        return <>{definition.name} está vazia</>;
      }

      const shown =
        criterion.op === "contains"
          ? { name: definition.name, valueKind: "select" as const, options: definition.options ?? [] }
          : definition;
      return (
        <>
          {definition.name} {criterion.op === "contains" ? "contém" : "é igual a"}{" "}
          <PropertyValueDisplay definition={shown} value={criterion.value} />
        </>
      );
    }
    case "relation":
      return <RelationText nodeId={criterion.nodeId} kind={criterion.relationKind} />;
  }
}

function RelationText({ nodeId, kind }: { nodeId: string; kind?: string }) {
  const node = useQuery(nodeQuery(nodeId));
  const title = node.data?.title ?? (node.isError ? MISSING : "…");
  return (
    <>
      Tem uma Relation{kind ? ` «${kind}»` : ""} com{" "}
      <Link to={`/nodes/${nodeId}`} className="underline underline-offset-4">
        {title}
      </Link>
    </>
  );
}

function Members({ collection }: { collection: CollectionItem }) {
  const members = useInfiniteQuery(collectionMembersQuery(collection.id));
  const nodes = members.data?.pages.flatMap((page) => page.data) ?? [];
  const manual = isManual(collection);
  // The probe reads the members again whenever it finds a change, so a Node an agent just made
  // shows up without a reload (DA-024).
  useChangeSetProbe();

  return (
    <section aria-labelledby="members-title" className="space-y-2">
      <h2 id="members-title" className="text-lg font-semibold">
        Nodes
      </h2>
      {members.isPending && <p role="status">Carregando…</p>}
      {members.isError && (
        <div className="space-y-2">
          <p role="alert" className="text-sm text-destructive">
            {LOAD_ERROR_MESSAGE}
          </p>
          <Button type="button" variant="secondary" size="sm" onClick={() => void members.refetch()}>
            Tentar de novo
          </Button>
        </div>
      )}
      {members.isSuccess &&
        (nodes.length === 0 ? (
          <p className="text-sm text-muted-foreground">{manual ? MANUAL_EMPTY_MESSAGE : DYNAMIC_EMPTY_MESSAGE}</p>
        ) : (
          <ul aria-label={`Nodes de ${collection.name}`} className="divide-y rounded-md border">
            {nodes.map((node) => (
              <MemberRow key={node.id} collectionId={collection.id} node={node} manual={manual} />
            ))}
          </ul>
        ))}
      {members.hasNextPage && (
        <Button type="button" variant="ghost" size="sm" onClick={() => void members.fetchNextPage()} disabled={members.isFetchingNextPage}>
          {members.isFetchingNextPage ? "Carregando…" : "Carregar mais"}
        </Button>
      )}
    </section>
  );
}

function MemberRow({ collectionId, node, manual }: { collectionId: string; node: NodeSummary; manual: boolean }) {
  const remove = useRemoveFromCollection(collectionId);

  return (
    <li className="flex flex-wrap items-center justify-between gap-2 p-3">
      <Link to={`/nodes/${node.id}`} className="underline-offset-4 hover:underline">
        {node.title}
      </Link>
      {manual && (
        <Button type="button" variant="ghost" size="sm" disabled={remove.isPending} onClick={() => remove.mutate(node.id)}>
          Remover <span className="sr-only">{node.title} da Collection</span>
        </Button>
      )}
      {remove.isError && (
        <p role="alert" className="w-full text-sm text-destructive">
          {writeErrorMessage(remove.error)}
        </p>
      )}
    </li>
  );
}
