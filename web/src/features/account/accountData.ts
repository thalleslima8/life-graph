import { queryOptions, useMutation, useQueryClient } from "@tanstack/react-query";
import { useNavigate } from "react-router";
import { api, isApiError, resetCsrfToken, unwrap } from "@/api/client";
import type { components } from "@/api/schema.gen";
import { sessionQueryKey } from "@/features/session/session";

/**
 * The person's Account (DA-255): the export of all their data (DA-247, DA-248). Asking for an
 * export answers at once; a job builds the archive, and the status is polled until it settles.
 * The archive is downloaded through the typed client, as a blob, never kept in the cache.
 */

type Schemas = components["schemas"];
export type AccountExportView = Schemas["AccountExportView"];
export type AccountExportStatus = AccountExportView["status"];
export type AccountExportFailure = NonNullable<AccountExportView["failure"]>;
export type AccountExportOversizedData = NonNullable<AccountExportView["oversizedData"]>;
export type DeleteAccountRequest = Schemas["DeleteAccountRequest"];
export type AccountDeletionResponse = Schemas["AccountDeletionResponse"];

export const accountErrorCodes = {
  exportInProgress: "graph.export_in_progress",
  exportNotFound: "graph.export_not_found",
  reauthenticationFailed: "accounts.reauthentication_failed",
  deletionConfirmationMismatch: "accounts.deletion_confirmation_mismatch",
} as const;

export const isExportInProgress = (error: unknown) => isApiError(error, accountErrorCodes.exportInProgress);

export const accountKeys = {
  all: ["account"] as const,
  exportStatus: ["account", "export"] as const,
};

// While the archive is being built the status is read again every few seconds; it stops once settled.
export const EXPORT_POLL_INTERVAL_MS = 3_000;

/** Whether the export stays where it is until the person asks again. */
export function isSettled(status: AccountExportStatus): boolean {
  return status !== "pending";
}

/** Where the person's export stands: none, pending, ready, failed (size_cap or internal) or expired. */
export const exportStatusOptions = () =>
  queryOptions({
    queryKey: accountKeys.exportStatus,
    queryFn: async ({ signal }) => unwrap(await api.GET("/api/account/export", { signal })),
    refetchInterval: (query) =>
      query.state.data && !isSettled(query.state.data.status) ? EXPORT_POLL_INTERVAL_MS : false,
  });

/** Asks for a new export; it replaces the previous one. */
export function useStartExport() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (): Promise<AccountExportView> => unwrap(await api.POST("/api/account/export")),
    onSuccess: (view) => queryClient.setQueryData(accountKeys.exportStatus, view),
    // A refusal (another export still building) shows the real state.
    onError: () =>
      void queryClient.invalidateQueries({
        queryKey: accountKeys.exportStatus,
      }),
  });
}

/** The name the archive is saved under. */
export function exportFileName(readyAt: string | null | undefined): string {
  return readyAt ? `lifegraph-export-${readyAt.slice(0, 10)}.zip` : "lifegraph-export.zip";
}

/** Downloads the ready archive: the browser saves the blob under {@link exportFileName}. */
export function useDownloadExport() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (readyAt: string | null | undefined): Promise<void> => {
      const archive = unwrap(await api.GET("/api/account/export/content", { parseAs: "blob" })) as Blob;
      saveBlob(archive, exportFileName(readyAt));
    },
    // Gone or expired since the status was read: show where it really stands.
    onError: () =>
      void queryClient.invalidateQueries({
        queryKey: accountKeys.exportStatus,
      }),
  });
}

/** The PII-free receipt of a deletion (DA-253): a random reference and the date, never the Account's id. */
export type AccountDeletionReceipt = { reference: string; date: string };

/** What the login page shows once after the Account is gone (DA-255): read from the router state, never stored. */
export type AccountDeletedNotice = { notice: "account_deleted"; receipt?: AccountDeletionReceipt };

const LOGIN_PATH = "/login";

/**
 * Deletes the Account for good, with the password typed again and the e-mail typed as
 * confirmation (DA-249, DA-251). Only a success drops the session and every cached response and
 * goes to the login page; a refusal keeps the person where they are (DA-255).
 */
export function useDeleteAccount() {
  const queryClient = useQueryClient();
  const navigate = useNavigate();

  return useMutation({
    mutationFn: async (request: DeleteAccountRequest): Promise<AccountDeletionResponse> =>
      unwrap(await api.POST("/api/account/deletion", { body: request })),
    onSuccess: async (deleted) => {
      await queryClient.cancelQueries();
      queryClient.clear();
      resetCsrfToken();
      queryClient.setQueryData(sessionQueryKey, null);
      const receipt = { reference: deleted.receipt.reference, date: deleted.receipt.date };
      const state: AccountDeletedNotice = { notice: "account_deleted", receipt };
      await navigate(LOGIN_PATH, { replace: true, state });
    },
  });
}

function saveBlob(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = fileName;
  document.body.append(link);
  link.click();
  link.remove();
  // Released once the browser has taken the download over.
  setTimeout(() => URL.revokeObjectURL(url), 0);
}
