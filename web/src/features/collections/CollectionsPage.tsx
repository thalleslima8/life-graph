import { useQuery } from "@tanstack/react-query";
import { Link, useNavigate } from "react-router";
import { Button } from "@/components/ui/button";
import { LOAD_ERROR_MESSAGE, writeErrorMessage } from "@/features/graph/graphMessages";
import { CollectionForm } from "./CollectionForm";
import { NEEDS_REVIEW_LABEL, OUTSIDE_NAV_NOTE, reviewText } from "./collectionMessages";
import {
  collectionPath,
  collectionsQuery,
  createdCollectionIdOf,
  isManual,
  isOutsideNav,
  NAV_PINNED_LIMIT,
  useCreateCollection,
  useUpdateCollection,
  withPinned,
  type CollectionItem,
} from "./collectionsData";

/** The person's Collections: create one, see which need review, pin them to the menu (DA-163, DA-173). */
export function CollectionsPage() {
  const collections = useQuery(collectionsQuery);
  const create = useCreateCollection();
  const navigate = useNavigate();
  const navFull = (collections.data ?? []).filter((collection) => collection.pinned).length >= NAV_PINNED_LIMIT;

  return (
    <section aria-labelledby="collections-title" className="space-y-6">
      <h1 id="collections-title" className="text-2xl font-semibold">
        Collections
      </h1>
      <CollectionForm
        idPrefix="new-collection"
        label="Nova Collection"
        submitLabel="Criar Collection"
        navFull={navFull}
        action={{
          isPending: create.isPending,
          error: create.error,
          submit: (input, onDone) =>
            create.mutate(input, {
              onSuccess: (receipt) => {
                onDone();
                const id = createdCollectionIdOf(receipt);
                if (id) {
                  void navigate(collectionPath(id));
                }
              },
            }),
        }}
      />
      {collections.isPending && <p role="status">Carregando…</p>}
      {collections.isError && (
        <div className="space-y-2">
          <p role="alert" className="text-sm text-destructive">
            {LOAD_ERROR_MESSAGE}
          </p>
          <Button type="button" variant="secondary" size="sm" onClick={() => void collections.refetch()}>
            Tentar de novo
          </Button>
        </div>
      )}
      {collections.isSuccess &&
        (collections.data.length === 0 ? (
          <p className="text-sm text-muted-foreground">Nenhuma Collection ainda. Crie uma acima.</p>
        ) : (
          <ul aria-label="Collections" className="divide-y rounded-md border">
            {collections.data.map((collection) => (
              <CollectionRow key={collection.id} collection={collection} outsideNav={isOutsideNav(collections.data, collection.id)} />
            ))}
          </ul>
        ))}
    </section>
  );
}

function CollectionRow({ collection, outsideNav }: { collection: CollectionItem; outsideNav: boolean }) {
  const update = useUpdateCollection(collection.id);
  const review = collection.needsReview ?? [];

  return (
    <li className="space-y-1 p-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p>
          <Link to={collectionPath(collection.id)} className="font-medium underline-offset-4 hover:underline">
            {collection.name}
          </Link>{" "}
          <span className="text-sm text-muted-foreground">
            · {isManual(collection) ? "Manual" : "Dinâmica"}
            {collection.pinned && ` · Fixada${outsideNav ? ` (${OUTSIDE_NAV_NOTE})` : ""}`}
          </span>
        </p>
        <Button
          type="button"
          variant="ghost"
          size="sm"
          aria-pressed={collection.pinned}
          disabled={update.isPending}
          onClick={() => update.mutate(withPinned(collection, !collection.pinned))}
        >
          {collection.pinned ? "Desafixar" : "Fixar"} <span className="sr-only">{collection.name} no menu</span>
        </Button>
      </div>
      {review.length > 0 && (
        <div className="text-sm text-destructive">
          <p>
            <span aria-hidden="true">⚠ </span>
            {NEEDS_REVIEW_LABEL}
          </p>
          <ul>
            {review.map((item) => (
              <li key={Number(item.criterionIndex)}>{reviewText(item)}</li>
            ))}
          </ul>
        </div>
      )}
      {update.isError && (
        <p role="alert" className="text-sm text-destructive">
          {writeErrorMessage(update.error)}
        </p>
      )}
    </li>
  );
}
