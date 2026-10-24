import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it } from "vitest";
import { axe } from "vitest-axe";
import { NODE_ID, aType, ontologyHandlers } from "@/test/graphFixtures";
import { csrfTokenHandler, problem, sessionHandler } from "@/test/msw/handlers";
import { server } from "@/test/msw/server";
import { renderRoute } from "@/test/renderRoute";
import type { CaptureReceipt } from "./resourceCapture";

const LINK = "https://example.test/article?id=7";

/** An empty Inbox and a capture endpoint that answers `answer` and records each body. */
function captureApi(answer: () => Response | Promise<Response>) {
  const captured: unknown[] = [];
  server.use(
    sessionHandler(() => ({ email: "ada@example.test" })),
    csrfTokenHandler().handler,
    ...ontologyHandlers({ types: [aType()] }),
    http.get("/api/nodes", () => HttpResponse.json({ data: [], page: { nextCursor: null } })),
    http.post("/api/resources", async ({ request }) => {
      captured.push(await request.json());
      return answer();
    }),
  );
  return captured;
}

function receipt(overrides: Partial<CaptureReceipt> = {}): CaptureReceipt {
  return { nodeId: NODE_ID, created: true, alreadyExists: false, alreadyInInbox: false, changeSetId: null, ...overrides };
}

async function pasteAndCapture(link = LINK) {
  await userEvent.type(await screen.findByLabelText("Colar um link na Inbox"), link);
  await userEvent.click(screen.getByRole("button", { name: "Capturar" }));
}

describe("capture by paste", () => {
  beforeEach(() => {
    window.history.replaceState(null, "", "/");
  });

  it("is on the Inbox, also when it is empty", async () => {
    captureApi(() => HttpResponse.json(receipt(), { status: 201 }));
    renderRoute("/nodes?inbox=1");

    expect(await screen.findByText("A Inbox está vazia.")).toBeInTheDocument();
    expect(screen.getByLabelText("Colar um link na Inbox")).toHaveAttribute("type", "url");
  });

  it("is not on the list of every Node", async () => {
    captureApi(() => HttpResponse.json(receipt(), { status: 201 }));
    renderRoute("/nodes");

    expect(await screen.findByText("Nenhum Node com esse filtro.")).toBeInTheDocument();
    expect(screen.queryByLabelText("Colar um link na Inbox")).not.toBeInTheDocument();
  });

  it("captures a link, clears the field and links to the Resource", async () => {
    const captured = captureApi(() => HttpResponse.json(receipt(), { status: 201 }));
    renderRoute("/nodes?inbox=1");

    await pasteAndCapture();

    expect(await screen.findByRole("status")).toHaveTextContent("Link capturado.");
    expect(screen.getByRole("link", { name: "Abrir o Resource" })).toHaveAttribute("href", `/nodes/${NODE_ID}`);
    expect(screen.getByLabelText("Colar um link na Inbox")).toHaveValue("");
    expect(captured).toEqual([{ url: LINK }]);
  });

  it.each([
    [{ created: false, alreadyExists: true, alreadyInInbox: true }, "Já está na sua Inbox."],
    // An archived Resource, or a deleted one the capture restored (DA-154).
    [{ created: false, alreadyExists: true, alreadyInInbox: false }, "Estava fora da Inbox e voltou."],
  ])("says what a known link did: %o", async (flags, message) => {
    captureApi(() => HttpResponse.json(receipt(flags)));
    renderRoute("/nodes?inbox=1");

    await pasteAndCapture();

    expect(await screen.findByRole("status")).toHaveTextContent(message);
  });

  it("links to nothing when the answer names no Resource", async () => {
    captureApi(() => HttpResponse.json(receipt({ nodeId: null, created: false, alreadyExists: true, alreadyInInbox: true })));
    renderRoute("/nodes?inbox=1");

    await pasteAndCapture();

    expect(await screen.findByRole("status")).toHaveTextContent("Já está na sua Inbox.");
    expect(screen.queryByRole("link", { name: "Abrir o Resource" })).not.toBeInTheDocument();
  });

  it("sends one capture while one is pending", async () => {
    let release: () => void = () => undefined;
    const answered = new Promise<void>((resolve) => {
      release = resolve;
    });
    const captured = captureApi(async () => {
      await answered;
      return HttpResponse.json(receipt(), { status: 201 });
    });
    renderRoute("/nodes?inbox=1");

    await pasteAndCapture();
    const pending = screen.getByRole("button", { name: "Capturando…" });
    expect(pending).toBeDisabled();
    await userEvent.click(pending);

    release();
    expect(await screen.findByRole("status")).toHaveTextContent("Link capturado.");
    expect(captured).toHaveLength(1);
  });

  it("marks the field when the server refuses the link, without echoing it", async () => {
    captureApi(() => problem(422, "graph.resource_url_invalid", { errors: { url: ["The link has credentials."] } }));
    renderRoute("/nodes?inbox=1");

    await pasteAndCapture("https://ada:secret@example.test/private");

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Use um link http ou https completo, sem usuário e senha.");
    expect(alert).not.toHaveTextContent("secret");
    const field = screen.getByLabelText("Colar um link na Inbox");
    expect(field).toHaveAttribute("aria-invalid", "true");
    expect(field).toHaveAttribute("aria-describedby", alert.id);
    expect(field).toHaveValue("https://ada:secret@example.test/private");
  });

  it("asks for a link before sending anything", async () => {
    const captured = captureApi(() => HttpResponse.json(receipt(), { status: 201 }));
    renderRoute("/nodes?inbox=1");

    await userEvent.click(await screen.findByRole("button", { name: "Capturar" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Cole um link.");
    expect(captured).toEqual([]);
  });

  it("has its own message when the capture budget is spent, and keeps the link", async () => {
    captureApi(() => problem(429, "too_many_requests", {}, { "Retry-After": "30" }));
    renderRoute("/nodes?inbox=1");

    await pasteAndCapture();

    expect(await screen.findByRole("alert")).toHaveTextContent("Muitas capturas em pouco tempo, tente de novo em instantes.");
    expect(screen.getByLabelText("Colar um link na Inbox")).toHaveValue(LINK);
  });

  it("says the server failed on any other refusal", async () => {
    captureApi(() => problem(500, "unexpected_error"));
    renderRoute("/nodes?inbox=1");

    await pasteAndCapture();

    expect(await screen.findByRole("alert")).toHaveTextContent("Não foi possível concluir agora. Tente novamente em instantes.");
  });

  it("has no accessibility violations after a capture", async () => {
    captureApi(() => HttpResponse.json(receipt(), { status: 201 }));
    const { container } = renderRoute("/nodes?inbox=1");
    await pasteAndCapture();
    await screen.findByRole("status");

    expect(await axe(container)).toHaveNoViolations();
  });
});
