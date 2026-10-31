import { useInfiniteQuery } from "@tanstack/react-query";
import { useState } from "react";
import { Link } from "react-router";
import { Button } from "@/components/ui/button";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { formatDateTime } from "@/features/graph/graphMessages";
import {
  sharesListOptions,
  useRevokeAllShares,
  useRevokeShare,
  type SharedGraphViewSummary,
} from "./sharesData";
import { lastAccessText, SHARE_STATUS_LABELS } from "./sharingMessages";

const TITLE_ID = "shares-title";
const LOAD_ERROR_MESSAGE = "Não foi possível carregar os links.";
const REVOKE_ERROR_MESSAGE = "Não foi possível revogar. Tente de novo.";
const ROOT_GONE_LABEL = "Node raiz indisponível";

/**
 * The person's links (DA-078): when each was created, its status, the day of its last visit and
 * on how many UTC days it was visited (DA-239). Revoking one or all is immediate and final (DA-214), each behind its own
 * confirmation. A suspended link or one disabled by the platform still counts toward the limit,
 * so it stays listed.
 */
export function SharesPage() {
  const shares = useInfiniteQuery(sharesListOptions());
  const [announcement, setAnnouncement] = useState("");
  const [confirmingRevokeAll, setConfirmingRevokeAll] = useState(false);
  const revokeAll = useRevokeAllShares();
  const revocable = (shares.data ?? []).filter(
    (share) => share.status !== "revoked",
  );

  const confirmRevokeAll = () =>
    revokeAll.mutate(undefined, {
      onSuccess: () => {
        setConfirmingRevokeAll(false);
        setAnnouncement("Todos os links foram revogados.");
      },
    });

  return (
    <section aria-labelledby={TITLE_ID} className="space-y-6">
      <h1 id={TITLE_ID} className="text-2xl font-semibold">
        Links compartilhados
      </h1>
      <p role="status" className="sr-only">
        {announcement}
      </p>

      {shares.isPending ? (
        <p>Carregando…</p>
      ) : shares.isError ? (
        <div className="space-y-2">
          <p role="alert" className="text-sm text-destructive">
            {LOAD_ERROR_MESSAGE}
          </p>
          <Button
            type="button"
            variant="secondary"
            size="sm"
            onClick={() => void shares.refetch()}
          >
            Tentar de novo
          </Button>
        </div>
      ) : shares.data.length === 0 ? (
        <p className="rounded-md border p-4 text-sm">
          Nenhum link ainda. Para compartilhar, abra um Node e escolha
          &ldquo;Compartilhar&rdquo;.
        </p>
      ) : (
        <>
          {revocable.length > 0 && (
            <p>
              <Button
                type="button"
                variant="secondary"
                onClick={() => {
                  revokeAll.reset();
                  setConfirmingRevokeAll(true);
                }}
              >
                Revogar todos
              </Button>
            </p>
          )}
          <ul aria-labelledby={TITLE_ID} className="divide-y rounded-md border">
            {shares.data.map((share) => (
              <ShareRow
                key={share.id}
                share={share}
                onRevoked={(title) =>
                  setAnnouncement(`O link de ${title} foi revogado.`)
                }
              />
            ))}
          </ul>
          {shares.hasNextPage && (
            <Button
              type="button"
              variant="secondary"
              onClick={() => void shares.fetchNextPage()}
              disabled={shares.isFetchingNextPage}
            >
              Carregar mais
            </Button>
          )}
        </>
      )}

      <ConfirmDialog
        open={confirmingRevokeAll}
        onOpenChange={setConfirmingRevokeAll}
        title="Revogar todos os links?"
        description={`Os ${revocable.length} links que ainda não foram revogados deixam de abrir na hora, para todos que os receberam. Não dá para desfazer: para compartilhar de novo, crie links novos.`}
        confirmLabel="Revogar todos"
        pending={revokeAll.isPending}
        error={revokeAll.isError ? REVOKE_ERROR_MESSAGE : undefined}
        onConfirm={confirmRevokeAll}
      />
    </section>
  );
}

function ShareRow({
  share,
  onRevoked,
}: {
  share: SharedGraphViewSummary;
  onRevoked: (title: string) => void;
}) {
  const [confirming, setConfirming] = useState(false);
  const revoke = useRevokeShare();
  const title = share.rootTitle ?? ROOT_GONE_LABEL;

  const confirmRevoke = () =>
    revoke.mutate(share.id, {
      onSuccess: () => {
        setConfirming(false);
        onRevoked(title);
      },
    });

  return (
    <li aria-label={title} className="space-y-3 px-4 py-4">
      <h2 className="font-medium break-words">
        {share.rootNodeId !== null && share.rootTitle !== null ? (
          <Link
            to={`/nodes/${share.rootNodeId}`}
            className="underline-offset-4 hover:underline"
          >
            {share.rootTitle}
          </Link>
        ) : (
          title
        )}
      </h2>
      <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 text-sm">
        <dt className="text-muted-foreground">Situação</dt>
        <dd>{SHARE_STATUS_LABELS[share.status]}</dd>
        <dt className="text-muted-foreground">Nodes</dt>
        <dd>{share.nodeCount}</dd>
        <dt className="text-muted-foreground">Criado em</dt>
        <dd>{formatDateTime(share.createdAt)}</dd>
        <dt className="text-muted-foreground">Último acesso</dt>
        <dd>{lastAccessText(share.lastAccessedOn)}</dd>
        <dt className="text-muted-foreground">Dias com acesso</dt>
        <dd>{share.accessDayCount}</dd>
        {share.revokedAt !== null && (
          <>
            <dt className="text-muted-foreground">Revogado em</dt>
            <dd>{formatDateTime(share.revokedAt)}</dd>
          </>
        )}
      </dl>
      {share.status !== "revoked" && (
        <Button
          type="button"
          variant="secondary"
          size="sm"
          onClick={() => {
            revoke.reset();
            setConfirming(true);
          }}
        >
          Revogar
          <span className="sr-only">: {title}</span>
        </Button>
      )}
      <ConfirmDialog
        open={confirming}
        onOpenChange={setConfirming}
        title="Revogar este link?"
        description={`O link de ${title} deixa de abrir na hora para quem o recebeu. Não dá para desfazer: para compartilhar de novo, crie um link novo.`}
        confirmLabel="Revogar"
        pending={revoke.isPending}
        error={revoke.isError ? REVOKE_ERROR_MESSAGE : undefined}
        onConfirm={confirmRevoke}
      />
    </li>
  );
}
