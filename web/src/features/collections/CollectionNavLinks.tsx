import { Link } from "react-router";
import { ALL_COLLECTIONS_LABEL, NEEDS_REVIEW_LABEL } from "./collectionMessages";
import { useCollectionNav } from "./collectionsData";

/**
 * The pinned Collections as menu items (DA-173): the first 8 by creation, then "Todas as
 * Collections". While the list loads or fails, only that link; the rest of the menu is static.
 */
export function CollectionNavLinks() {
  const nav = useCollectionNav();

  return (
    <>
      {nav.items.map(({ to, label, needsReview }) => (
        <li key={to}>
          <Link to={to} className="underline-offset-4 hover:underline">
            {label}
            {needsReview && (
              <>
                {" "}
                <span aria-hidden="true">⚠</span>
                <span className="sr-only">({NEEDS_REVIEW_LABEL})</span>
              </>
            )}
          </Link>
        </li>
      ))}
      <li>
        <Link to="/collections" className="underline-offset-4 hover:underline">
          {ALL_COLLECTIONS_LABEL}
        </Link>
      </li>
    </>
  );
}
