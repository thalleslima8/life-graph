import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { axe } from "vitest-axe";
import { csrfTokenHandler, problem, sessionHandler } from "@/test/msw/handlers";
import { server } from "@/test/msw/server";
import { renderRoute } from "@/test/renderRoute";
import { EXPORT_MESSAGES } from "./accountMessages";
import {
  accountErrorCodes,
  EXPORT_POLL_INTERVAL_MS,
  exportFileName,
  exportStatusOptions,
  type AccountExportView,
} from "./accountData";

function aView(overrides: Partial<AccountExportView> = {}): AccountExportView {
  return {
    status: "none",
    failure: null,
    oversizedData: null,
    requestedAt: null,
    readyAt: null,
    expiresAt: null,
    sizeBytes: null,
    ...overrides,
  };
}

const READY = aView({
  status: "ready",
  requestedAt: "2026-10-30T10:00:00Z",
  readyAt: "2026-10-30T10:01:00Z",
  expiresAt: "2026-10-31T10:01:00Z",
  sizeBytes: 2048,
});

/** Serves the export's status, the request and the download, and records what was asked. */
function exportApi(
  initial: AccountExportView,
  onRequest: () => Response = () =>
    HttpResponse.json(aView({ status: "pending", requestedAt: "2026-10-30T10:00:00Z" }), { status: 202 }),
) {
  const state = { view: initial, requests: 0, downloads: 0 };
  server.use(
    sessionHandler(() => ({ email: "ada@example.test" })),
    csrfTokenHandler().handler,
    http.get("/api/account/export", () => HttpResponse.json(state.view)),
    http.post("/api/account/export", () => {
      state.requests += 1;
      return onRequest();
    }),
    http.get("/api/account/export/content", () => {
      state.downloads += 1;
      return new HttpResponse(new Blob(["zip"], { type: "application/zip" }), {
        headers: { "Content-Type": "application/zip" },
      });
    }),
  );
  return state;
}

describe("the account page", () => {
  const clicked: string[] = [];

  beforeEach(() => {
    clicked.length = 0;
    vi.spyOn(URL, "createObjectURL").mockReturnValue("blob:export");
    vi.spyOn(URL, "revokeObjectURL").mockImplementation(() => undefined);
    vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(function (this: HTMLAnchorElement) {
      clicked.push(this.download);
    });
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("offers the export before any request and shows it pending once asked", async () => {
    const api = exportApi(aView());
    const { container } = renderRoute("/conta");

    expect(await screen.findByText(EXPORT_MESSAGES.none)).toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 1, name: "Conta" })).toBeInTheDocument();
    expect(await axe(container)).toHaveNoViolations();

    await userEvent.click(screen.getByRole("button", { name: "Exportar meus dados" }));

    expect(await screen.findByText(EXPORT_MESSAGES.pending)).toBeInTheDocument();
    expect(api.requests).toBe(1);
    expect(screen.queryByRole("button", { name: "Exportar meus dados" })).not.toBeInTheDocument();
  });

  it("downloads a ready export through the client and saves it under a dated name", async () => {
    const api = exportApi(READY);
    renderRoute("/conta");

    await userEvent.click(await screen.findByRole("button", { name: "Baixar export" }));

    await waitFor(() => expect(clicked).toEqual(["lifegraph-export-2026-10-30.zip"]));
    expect(api.downloads).toBe(1);
    expect(screen.getByRole("button", { name: "Gerar de novo" })).toBeEnabled();
  });

  it("shows an actionable text naming the oversized data for a size_cap failure, with no blind retry", async () => {
    exportApi(
      aView({
        status: "failed",
        failure: "size_cap",
        oversizedData: "change_sets",
        requestedAt: "2026-10-30T10:00:00Z",
      }),
    );
    const { container } = renderRoute("/conta");

    expect(await screen.findByText(EXPORT_MESSAGES.sizeCap("change_sets"))).toBeInTheDocument();
    expect(screen.getByText(/ao incluir o histórico de alterações/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Tentar de novo" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Exportar meus dados" })).not.toBeInTheDocument();
    expect(await axe(container)).toHaveNoViolations();
  });

  it("offers a retry for an internal failure", async () => {
    const api = exportApi(
      aView({
        status: "failed",
        failure: "internal",
        requestedAt: "2026-10-30T10:00:00Z",
      }),
    );
    renderRoute("/conta");

    expect(await screen.findByText(EXPORT_MESSAGES.internal)).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Tentar de novo" }));

    await waitFor(() => expect(api.requests).toBe(1));
  });

  it("offers a new export once the last one expired", async () => {
    exportApi(aView({ status: "expired", requestedAt: "2026-10-29T10:00:00Z" }));
    renderRoute("/conta");

    expect(await screen.findByText(EXPORT_MESSAGES.expired)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Exportar meus dados" })).toBeEnabled();
  });

  it("announces a refused request and shows the export still being built", async () => {
    const api = exportApi(aView(), () => problem(409, accountErrorCodes.exportInProgress));
    renderRoute("/conta");

    const button = await screen.findByRole("button", {
      name: "Exportar meus dados",
    });
    api.view = aView({
      status: "pending",
      requestedAt: "2026-10-30T10:00:00Z",
    });
    await userEvent.click(button);

    expect(await screen.findByRole("alert")).toHaveTextContent(EXPORT_MESSAGES.startError);
    expect(await screen.findByText(EXPORT_MESSAGES.pending)).toBeInTheDocument();
  });

  it("announces a failed download and reads the status again", async () => {
    const api = exportApi(READY);
    server.use(http.get("/api/account/export/content", () => problem(404, accountErrorCodes.exportNotFound)));
    renderRoute("/conta");

    const button = await screen.findByRole("button", { name: "Baixar export" });
    api.view = aView({ status: "expired", requestedAt: READY.requestedAt });
    await userEvent.click(button);

    expect(await screen.findByRole("alert")).toHaveTextContent(EXPORT_MESSAGES.downloadError);
    expect(await screen.findByText(EXPORT_MESSAGES.expired)).toBeInTheDocument();
    expect(clicked).toEqual([]);
  });
});

describe("the export status", () => {
  const intervalOf = (view: AccountExportView | undefined) => {
    const { refetchInterval } = exportStatusOptions();
    if (typeof refetchInterval !== "function") {
      throw new Error("The export status is expected to poll by state.");
    }

    return refetchInterval({ state: { data: view } } as Parameters<typeof refetchInterval>[0]);
  };

  it("is polled only while pending", () => {
    expect(intervalOf(aView({ status: "pending" }))).toBe(EXPORT_POLL_INTERVAL_MS);
    for (const status of ["none", "ready", "failed", "expired"] as const) {
      expect(intervalOf(aView({ status }))).toBe(false);
    }
    expect(intervalOf(undefined)).toBe(false);
  });

  it("names the archive by the day it was ready", () => {
    expect(exportFileName("2026-10-30T10:01:00Z")).toBe("lifegraph-export-2026-10-30.zip");
    expect(exportFileName(null)).toBe("lifegraph-export.zip");
  });
});
