import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it } from "vitest";
import { axe } from "vitest-axe";
import { aReceipt, aTag, TAG_ID } from "@/test/graphFixtures";
import { csrfTokenHandler, problem, sessionHandler } from "@/test/msw/handlers";
import { server } from "@/test/msw/server";
import { renderRoute } from "@/test/renderRoute";
import type { TagItem } from "./tagsData";

function serveTags(tags: TagItem[]) {
  server.use(http.get("/api/tags", () => HttpResponse.json({ data: tags, page: { nextCursor: null } })));
}

describe("tags page", () => {
  beforeEach(() => {
    server.use(sessionHandler(() => ({ email: "ada@example.test" })), csrfTokenHandler().handler);
  });

  it("lists the Tags by name with how many Nodes each is on", async () => {
    serveTags([aTag({ id: "t2", name: "projeto", nodeCount: 1 }), aTag({ name: "leitura", nodeCount: 3 })]);
    renderRoute("/tags");

    const list = await screen.findByRole("list", { name: "Tags" });
    const rows = within(list).getAllByRole("listitem");
    expect(rows[0]).toHaveTextContent("#leitura · 3 Nodes");
    expect(rows[1]).toHaveTextContent("#projeto · 1 Node");
  });

  it("reads every page of Tags", async () => {
    server.use(
      http.get("/api/tags", ({ request }) =>
        new URL(request.url).searchParams.get("cursor") === "next"
          ? HttpResponse.json({ data: [aTag({ id: "t2", name: "projeto" })], page: { nextCursor: null } })
          : HttpResponse.json({ data: [aTag()], page: { nextCursor: "next" } }),
      ),
    );
    renderRoute("/tags");

    const list = await screen.findByRole("list", { name: "Tags" });
    expect(within(list).getAllByRole("listitem")).toHaveLength(2);
  });

  it("shows a create hint when there are no Tags", async () => {
    serveTags([]);
    renderRoute("/tags");

    expect(await screen.findByText(/Nenhuma Tag ainda/)).toBeInTheDocument();
  });

  it("offers a retry when the list fails", async () => {
    let calls = 0;
    server.use(
      http.get("/api/tags", () => {
        calls += 1;
        return calls === 1 ? problem(500, "unexpected") : HttpResponse.json({ data: [aTag()], page: { nextCursor: null } });
      }),
    );
    renderRoute("/tags");

    await userEvent.click(await screen.findByRole("button", { name: "Tentar de novo" }));

    expect(await screen.findByRole("list", { name: "Tags" })).toBeInTheDocument();
  });

  it("creates a Tag with the name normalized as the server stores it", async () => {
    serveTags([]);
    let sent: unknown;
    server.use(
      http.post("/api/tags", async ({ request }) => {
        sent = await request.json();
        return HttpResponse.json(aReceipt(), { status: 201 });
      }),
    );
    renderRoute("/tags");

    await userEvent.type(await screen.findByRole("textbox", { name: "Nova Tag" }), "  #leitura   lenta ");
    await userEvent.click(screen.getByRole("button", { name: "Criar Tag" }));

    await expect.poll(() => sent).toEqual({ name: "leitura lenta" });
  });

  it("refuses a # inside the name before sending it", async () => {
    serveTags([]);
    renderRoute("/tags");

    await userEvent.type(await screen.findByRole("textbox", { name: "Nova Tag" }), "a#b");
    await userEvent.click(screen.getByRole("button", { name: "Criar Tag" }));

    expect(await screen.findByText("Use # só no início.")).toBeInTheDocument();
  });

  it("says when the name is already taken", async () => {
    serveTags([]);
    server.use(http.post("/api/tags", () => problem(409, "graph.tag_name_taken")));
    renderRoute("/tags");

    await userEvent.type(await screen.findByRole("textbox", { name: "Nova Tag" }), "Leitura");
    await userEvent.click(screen.getByRole("button", { name: "Criar Tag" }));

    expect(await screen.findByText("Já existe uma Tag com esse nome.")).toBeInTheDocument();
  });

  it("renames a Tag", async () => {
    serveTags([aTag()]);
    let sent: unknown;
    server.use(
      http.patch(`/api/tags/${TAG_ID}`, async ({ request }) => {
        sent = await request.json();
        return HttpResponse.json(aReceipt());
      }),
    );
    renderRoute("/tags");

    await userEvent.click(await screen.findByRole("button", { name: "Renomear leitura" }));
    const field = screen.getByRole("textbox", { name: "Novo nome de leitura" });
    await userEvent.clear(field);
    await userEvent.type(field, "leituras");
    await userEvent.click(screen.getByRole("button", { name: "Salvar" }));

    await expect.poll(() => sent).toEqual({ name: "leituras" });
  });

  it("asks before deleting a Tag and says how many Nodes lose it", async () => {
    serveTags([aTag({ nodeCount: 2 })]);
    let deleted = false;
    server.use(
      http.delete(`/api/tags/${TAG_ID}`, () => {
        deleted = true;
        return HttpResponse.json(aReceipt());
      }),
    );
    renderRoute("/tags");

    await userEvent.click(await screen.findByRole("button", { name: "Apagar leitura" }));
    const dialog = await screen.findByRole("dialog", { name: "Apagar a Tag leitura?" });
    expect(dialog).toHaveTextContent("Desvincula de 2 Nodes; desfazível");
    expect(await axe(dialog)).toHaveNoViolations();

    await userEvent.click(within(dialog).getByRole("button", { name: "Apagar Tag" }));

    await expect.poll(() => deleted).toBe(true);
    await expect.poll(() => screen.queryByRole("dialog")).toBeNull();
  });

  it("keeps the Tag when the deletion is cancelled", async () => {
    serveTags([aTag()]);
    renderRoute("/tags");

    await userEvent.click(await screen.findByRole("button", { name: "Apagar leitura" }));
    await userEvent.click(within(await screen.findByRole("dialog")).getByRole("button", { name: "Cancelar" }));

    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(screen.getByText("#leitura")).toBeInTheDocument();
  });

  it("has no accessibility violations", async () => {
    serveTags([aTag()]);
    const { container } = renderRoute("/tags");
    await screen.findByRole("list", { name: "Tags" });

    expect(await axe(container)).toHaveNoViolations();
  });
});
