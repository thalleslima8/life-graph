import { useQuery, type UseQueryResult } from "@tanstack/react-query";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { formatDateTime } from "@/features/graph/graphMessages";
import { useSession } from "@/features/session/session";
import { exportStatusOptions, useDownloadExport, useStartExport, type AccountExportView } from "./accountData";
import { DELETION_MESSAGES, EXPORT_MESSAGES } from "./accountMessages";
import { DeleteAccountDialog } from "./DeleteAccountDialog";

const TITLE_ID = "account-title";
const EXPORT_TITLE_ID = "account-export-title";
const DELETION_TITLE_ID = "account-deletion-title";

/** The person's Account (DA-255): the export of all their data (DA-085, DA-247) and the deletion of the Account (DA-082). */
export function AccountPage() {
  const session = useSession();
  const status = useQuery(exportStatusOptions());
  const start = useStartExport();
  const download = useDownloadExport();

  return (
    <section aria-labelledby={TITLE_ID} className="space-y-8">
      <div className="space-y-1">
        <h1 id={TITLE_ID} className="text-2xl font-semibold">
          Conta
        </h1>
        {session && <p className="text-sm text-muted-foreground">{session.email}</p>}
      </div>
      <ExportSection status={status} start={start} download={download} />
      {session && (
        <DeletionSection
          email={session.email}
          // A ready export is offered as the download; otherwise a new one is asked for (DA-255).
          onExportFirst={() => {
            if (status.data?.status === "ready") {
              download.mutate(status.data.readyAt);
            } else if (status.data?.status !== "pending") {
              start.mutate();
            }
          }}
        />
      )}
    </section>
  );
}

interface ExportSectionProps {
  status: UseQueryResult<AccountExportView>;
  start: ReturnType<typeof useStartExport>;
  download: ReturnType<typeof useDownloadExport>;
}

function ExportSection({ status, start, download }: ExportSectionProps) {
  return (
    <section aria-labelledby={EXPORT_TITLE_ID} className="space-y-4 rounded-md border p-4">
      <h2 id={EXPORT_TITLE_ID} className="text-lg font-semibold">
        Exportar meus dados
      </h2>
      <p className="text-sm">{EXPORT_MESSAGES.intro}</p>

      {status.isPending ? (
        <p>Carregando…</p>
      ) : status.isError ? (
        <div className="space-y-2">
          <p role="alert" className="text-sm text-destructive">
            {EXPORT_MESSAGES.loadError}
          </p>
          <Button type="button" variant="secondary" size="sm" onClick={() => void status.refetch()}>
            Tentar de novo
          </Button>
        </div>
      ) : (
        <ExportState
          view={status.data}
          starting={start.isPending}
          downloading={download.isPending}
          onStart={() => start.mutate()}
          onDownload={() => download.mutate(status.data.readyAt)}
        />
      )}

      {start.isError && (
        <p role="alert" className="text-sm text-destructive">
          {EXPORT_MESSAGES.startError}
        </p>
      )}
      {download.isError && (
        <p role="alert" className="text-sm text-destructive">
          {EXPORT_MESSAGES.downloadError}
        </p>
      )}
    </section>
  );
}

interface ExportStateProps {
  view: AccountExportView;
  starting: boolean;
  downloading: boolean;
  onStart: () => void;
  onDownload: () => void;
}

function ExportState({ view, starting, downloading, onStart, onDownload }: ExportStateProps) {
  const startButton = (label: string) => (
    <Button type="button" onClick={onStart} disabled={starting}>
      {label}
    </Button>
  );

  switch (view.status) {
    case "pending":
      return (
        <p role="status" className="text-sm">
          {EXPORT_MESSAGES.pending}
        </p>
      );
    case "ready":
      return (
        <div className="space-y-3">
          <p role="status" className="text-sm">
            Export pronto
            {view.readyAt && ` em ${formatDateTime(view.readyAt)}`}
            {view.expiresAt && `; disponível até ${formatDateTime(view.expiresAt)}`}.
          </p>
          <div className="flex flex-wrap gap-3">
            <Button type="button" onClick={onDownload} disabled={downloading}>
              Baixar export
            </Button>
            <Button type="button" variant="secondary" onClick={onStart} disabled={starting}>
              Gerar de novo
            </Button>
          </div>
        </div>
      );
    case "failed":
      // A size_cap failure would fail the same way again: no blind retry (DA-248, DA-255).
      return view.failure === "size_cap" ? (
        <p role="status" className="text-sm">
          {EXPORT_MESSAGES.sizeCap(view.oversizedData)}
        </p>
      ) : (
        <div className="space-y-3">
          <p role="status" className="text-sm">
            {EXPORT_MESSAGES.internal}
          </p>
          {startButton("Tentar de novo")}
        </div>
      );
    case "expired":
      return (
        <div className="space-y-3">
          <p className="text-sm">{EXPORT_MESSAGES.expired}</p>
          {startButton("Exportar meus dados")}
        </div>
      );
    default:
      return (
        <div className="space-y-3">
          <p className="text-sm">{EXPORT_MESSAGES.none}</p>
          {startButton("Exportar meus dados")}
        </div>
      );
  }
}

interface DeletionSectionProps {
  email: string;
  onExportFirst: () => void;
}

function DeletionSection({ email, onExportFirst }: DeletionSectionProps) {
  const [open, setOpen] = useState(false);

  return (
    <section aria-labelledby={DELETION_TITLE_ID} className="space-y-4 rounded-md border p-4">
      <h2 id={DELETION_TITLE_ID} className="text-lg font-semibold">
        Excluir conta
      </h2>
      <p className="text-sm">{DELETION_MESSAGES.intro}</p>
      <p className="text-sm">{DELETION_MESSAGES.externalAgents}</p>
      <Button type="button" variant="secondary" onClick={() => setOpen(true)}>
        Excluir minha conta
      </Button>
      <DeleteAccountDialog
        open={open}
        onOpenChange={setOpen}
        email={email}
        onExportFirst={() => {
          // The export section shows what happens next: the download or the new export's progress.
          setOpen(false);
          onExportFirst();
        }}
      />
    </section>
  );
}
