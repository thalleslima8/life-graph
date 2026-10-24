import { z } from "zod";
import { api, unwrap } from "@/api/client";
import type { components } from "@/api/schema.gen";
import { useGraphWrite } from "@/features/graph/graphData";

/**
 * Capture by paste (DA-062, DA-157): one link into the Inbox as a Resource, idempotent by its
 * canonical form on the server (DA-154, DA-156). The server is the authority on what a valid
 * link is (FE-020); the link never goes into a query key, a toast or a log (GEN-043).
 */

type Schemas = components["schemas"];
export type CaptureReceipt = Schemas["CaptureResourceResponse"];
export type CaptureResourceInput = { url: string; title?: string; source?: string };
/** What a capture did to the Inbox, as the person reads it. */
export type CaptureOutcome = "created" | "already-in-inbox" | "reentered";

// Mirrors GraphLimits.UrlValueMaxLength on the server, which always checks again (FE-020).
export const RESOURCE_URL_MAX_LENGTH = 2048;

/** Syntax only: something to send. Whether it is a link the server keeps is the server's call. */
export const captureUrlSchema = z
  .string()
  .trim()
  .min(1, "Cole um link.")
  .max(RESOURCE_URL_MAX_LENGTH, `Use no máximo ${RESOURCE_URL_MAX_LENGTH} caracteres.`);

export function useCaptureResource() {
  return useGraphWrite(async (input: CaptureResourceInput) => unwrap(await api.POST("/api/resources", { body: input })));
}

/**
 * Reads the receipt's flags. A link already in the Inbox wins over every other flag; one the
 * Account kept outside the Inbox (archived or deleted) came back; anything else was new. A
 * combination the server never sends falls into the same order instead of throwing.
 */
export function outcomeOf(receipt: CaptureReceipt): CaptureOutcome {
  if (receipt.alreadyInInbox) {
    return "already-in-inbox";
  }

  if (receipt.alreadyExists) {
    return "reentered";
  }

  return "created";
}
