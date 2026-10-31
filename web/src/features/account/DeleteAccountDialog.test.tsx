import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { delay, http, HttpResponse } from "msw";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { axe } from "vitest-axe";
import { formatDateTime } from "@/features/graph/graphMessages";
import { csrfTokenHandler, problem, sessionHandler } from "@/test/msw/handlers";
import { server } from "@/test/msw/server";
import { renderRoute } from "@/test/renderRoute";
import { accountErrorCodes, type AccountExportView } from "./accountData";
import { DELETION_MESSAGES } from "./accountMessages";
import { TOO_MANY_ATTEMPTS_MESSAGE } from "./messages";

const EMAIL = "ada@example.test";
const PASSWORD = "correct horse battery staple";

const NO_EXPORT: AccountExportView = {
  status: "none",
  failure: null,
  oversizedData: null,
  requestedAt: null,
  readyAt: null,
  expiresAt: null,
  sizeBytes: null,
};

/** A signed-in Account whose deletion answers with `onDelete`; a success signs it out. */
function accountApi(onDelete: () => Response | Promise<Response>, exportView: AccountExportView = NO_EXPORT) {
  const state = { signedIn: true, deletions: [] as unknown[], exportRequests: 0, downloads: 0 };
  server.use(
    sessionHandler(() => (state.signedIn ? { email: EMAIL } : null)),
    csrfTokenHandler().handler,
    http.get("/api/account/export", () => HttpResponse.json(exportView)),
    http.post("/api/account/export", () => {
      state.exportRequests += 1;
      return HttpResponse.json({ ...NO_EXPORT, status: "pending" }, { status: 202 });
    }),
    http.get("/api/account/export/content", () => {
      state.downloads += 1;
      return new HttpResponse(new Blob(["zip"]), { headers: { "Content-Type": "application/zip" } });
    }),
    http.post("/api/account/deletion", async ({ request }) => {
      state.deletions.push(await request.json());
      const response = await onDelete();
      if (response.ok) {
        state.signedIn = false;
      }
      return response;
    }),
  );
  return state;
}

const RECEIPT = { reference: "3F9A-0C21-B7E4-58D6", date: "2026-10-31T12:00:00Z" };
const deleted = () => HttpResponse.json({ deletedAt: RECEIPT.date, receipt: RECEIPT });

async function openDialog() {
  await userEvent.click(await screen.findByRole("button", { name: "Excluir minha conta" }));
  return screen.findByRole("dialog", { name: "Excluir conta" });
}

async function fillAndSubmit(dialog: HTMLElement, password = PASSWORD, confirmation = EMAIL) {
  await userEvent.type(within(dialog).getByLabelText("Senha"), password);
  await userEvent.type(within(dialog).getByLabelText("E-mail da conta"), confirmation);
  await userEvent.click(within(dialog).getByRole("button", { name: "Excluir conta para sempre" }));
}

describe("the account deletion", () => {
  beforeEach(() => {
    vi.spyOn(URL, "createObjectURL").mockReturnValue("blob:export");
    vi.spyOn(URL, "revokeObjectURL").mockImplementation(() => undefined);
    vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(() => undefined);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("warns before anything and enables the deletion only once the account's e-mail is typed", async () => {
    accountApi(deleted);
    const { container } = renderRoute("/conta");

    expect(await screen.findByText(DELETION_MESSAGES.externalAgents)).toBeInTheDocument();
    const dialog = await openDialog();

    expect(dialog).toHaveAccessibleDescription(`${DELETION_MESSAGES.irreversible} ${DELETION_MESSAGES.externalAgents}`);
    const submit = within(dialog).getByRole("button", { name: "Excluir conta para sempre" });
    expect(submit).toBeDisabled();
    await userEvent.type(within(dialog).getByLabelText("Senha"), PASSWORD);
    await userEvent.type(within(dialog).getByLabelText("E-mail da conta"), "grace@example.test");
    expect(submit).toBeDisabled();
    await userEvent.clear(within(dialog).getByLabelText("E-mail da conta"));
    await userEvent.type(within(dialog).getByLabelText("E-mail da conta"), " ADA@example.test");
    expect(submit).toBeEnabled();
    expect(await axe(container)).toHaveNoViolations();
  });

  it("deletes with the password and the typed e-mail, then lands on the login with the notice and the receipt", async () => {
    const api = accountApi(deleted);
    const { router } = renderRoute("/conta");

    await fillAndSubmit(await openDialog());

    expect(await screen.findByText(DELETION_MESSAGES.done)).toBeInTheDocument();
    expect(router.state.location.pathname).toBe("/login");
    expect(api.deletions).toEqual([{ password: PASSWORD, confirmation: EMAIL }]);
    expect(screen.getByRole("heading", { name: "Entrar" })).toBeInTheDocument();
    expect(screen.getByText(new RegExp(RECEIPT.reference))).toBeInTheDocument();
    expect(router.state.location.state).toEqual({ notice: "account_deleted", receipt: RECEIPT });
  });

  it("keeps the person on the page with the dialog open and marks the password when it is wrong", async () => {
    accountApi(() => problem(422, accountErrorCodes.reauthenticationFailed));
    const { container, router } = renderRoute("/conta");
    const dialog = await openDialog();

    await fillAndSubmit(dialog, "wrong password");

    expect(await within(dialog).findByRole("alert")).toHaveTextContent(DELETION_MESSAGES.wrongPassword);
    const password = within(dialog).getByLabelText("Senha");
    expect(password).toHaveAttribute("aria-invalid", "true");
    expect(password).toHaveAccessibleDescription(DELETION_MESSAGES.wrongPassword);
    expect(within(dialog).getByLabelText("E-mail da conta")).not.toHaveAttribute("aria-invalid");
    expect(router.state.location.pathname).toBe("/conta");
    expect(await axe(container)).toHaveNoViolations();
  });

  it("marks the e-mail field when the server refuses the confirmation", async () => {
    accountApi(() =>
      problem(422, accountErrorCodes.deletionConfirmationMismatch, {
        errors: { confirmation: ["The typed text is not the Account's e-mail."] },
      }),
    );
    const { container } = renderRoute("/conta");
    const dialog = await openDialog();

    await fillAndSubmit(dialog);

    expect(await within(dialog).findByRole("alert")).toHaveTextContent(DELETION_MESSAGES.confirmationMismatch);
    const confirmation = within(dialog).getByLabelText("E-mail da conta");
    expect(confirmation).toHaveAttribute("aria-invalid", "true");
    expect(confirmation).toHaveAccessibleDescription(
      `${DELETION_MESSAGES.confirmationMismatch} ${DELETION_MESSAGES.confirmationHint(EMAIL)}`,
    );
    expect(within(dialog).getByLabelText("Senha")).not.toHaveAttribute("aria-invalid");
    expect(await axe(container)).toHaveNoViolations();
  });

  it("explains a run out of attempts for the whole form, with no field marked", async () => {
    let answer = () => problem(422, accountErrorCodes.deletionConfirmationMismatch);
    accountApi(() => answer());
    renderRoute("/conta");
    const dialog = await openDialog();

    await fillAndSubmit(dialog);
    expect(await within(dialog).findByRole("alert")).toHaveTextContent(DELETION_MESSAGES.confirmationMismatch);

    answer = () => problem(429, "accounts.too_many_attempts", {}, { "Retry-After": "900" });
    await userEvent.click(within(dialog).getByRole("button", { name: "Excluir conta para sempre" }));
    await waitFor(() => expect(within(dialog).getByRole("alert")).toHaveTextContent(TOO_MANY_ATTEMPTS_MESSAGE));
    expect(within(dialog).getByLabelText("E-mail da conta")).not.toHaveAttribute("aria-invalid");
    expect(within(dialog).getByLabelText("Senha")).not.toHaveAttribute("aria-invalid");
    expect(dialog.querySelector("form")).toHaveAccessibleDescription(TOO_MANY_ATTEMPTS_MESSAGE);
  });

  it("cannot be sent twice while the deletion is in flight", async () => {
    const api = accountApi(async () => {
      await delay(50);
      return deleted();
    });
    renderRoute("/conta");
    const dialog = await openDialog();

    await fillAndSubmit(dialog);

    expect(within(dialog).getByRole("button", { name: "Excluindo…" })).toBeDisabled();
    await screen.findByText(DELETION_MESSAGES.done);
    expect(api.deletions).toHaveLength(1);
  });

  it("forgets the password when the dialog closes", async () => {
    accountApi(deleted);
    renderRoute("/conta");
    let dialog = await openDialog();
    await userEvent.type(within(dialog).getByLabelText("Senha"), PASSWORD);

    await userEvent.click(within(dialog).getByRole("button", { name: "Cancelar" }));
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    await waitFor(() => expect(screen.getByRole("button", { name: "Excluir minha conta" })).toHaveFocus());

    dialog = await openDialog();
    expect(within(dialog).getByLabelText("Senha")).toHaveValue("");
    expect(within(dialog).getByLabelText("E-mail da conta")).toHaveValue("");
  });

  it("offers the ready export's download before deleting, and asks for one when there is none", async () => {
    const ready = { ...NO_EXPORT, status: "ready" as const, readyAt: "2026-10-30T10:01:00Z", expiresAt: "2026-10-31T10:01:00Z" };
    const api = accountApi(deleted, ready);
    renderRoute("/conta");

    await userEvent.click(within(await openDialog()).getByRole("button", { name: "Exportar antes" }));

    await waitFor(() => expect(api.downloads).toBe(1));
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(api.deletions).toHaveLength(0);
  });

  it("asks for a new export from the dialog when none is ready", async () => {
    const api = accountApi(deleted);
    renderRoute("/conta");

    await userEvent.click(within(await openDialog()).getByRole("button", { name: "Exportar antes" }));

    await waitFor(() => expect(api.exportRequests).toBe(1));
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });
});

describe("the login after a deletion", () => {
  it("shows the deletion notice and its receipt handed over in the router state", async () => {
    server.use(csrfTokenHandler().handler, sessionHandler(() => null));

    renderRoute({ pathname: "/login", state: { notice: "account_deleted", receipt: RECEIPT } });

    expect(await screen.findByText(DELETION_MESSAGES.done)).toBeInTheDocument();
    expect(screen.getByText(`Recibo de exclusão: ${RECEIPT.reference}, em ${formatDateTime(RECEIPT.date)}.`)).toBeInTheDocument();
  });

  it("ignores router state that is not a deletion notice", async () => {
    server.use(csrfTokenHandler().handler, sessionHandler(() => null));

    renderRoute({ pathname: "/login", state: { notice: "something_else" } });

    expect(await screen.findByRole("heading", { name: "Entrar" })).toBeInTheDocument();
    expect(screen.queryByText(DELETION_MESSAGES.done)).not.toBeInTheDocument();
  });
});
