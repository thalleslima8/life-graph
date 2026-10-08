import { zodResolver } from "@hookform/resolvers/zod";
import { useInfiniteQuery, useQuery, type UseQueryResult } from "@tanstack/react-query";
import { useForm } from "react-hook-form";
import { Link, useNavigate } from "react-router";
import { z } from "zod";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import {
  ANY_TYPE,
  NO_TYPE,
  TITLE_MAX_LENGTH,
  nodesQuery,
  typesQuery,
  useArchiveNode,
  useCreateNode,
  useNodeListFilter,
  type NodeSummary,
  type TypeItem,
} from "./graphData";
import {
  LOAD_ERROR_MESSAGE,
  NO_TYPE_LABEL,
  TYPES_LOAD_ERROR_MESSAGE,
  TYPE_LOADING_LABEL,
  TYPE_UNAVAILABLE_LABEL,
  writeErrorMessage,
} from "./graphMessages";

const createSchema = z.object({
  title: z.string().trim().min(1, "Informe um título.").max(TITLE_MAX_LENGTH, `Use no máximo ${TITLE_MAX_LENGTH} caracteres.`),
});

type CreateValues = z.infer<typeof createSchema>;

/** Nodes, filtered by Type ("Sem Type", DA-018) and by the Inbox (DA-019); the filter lives in the URL. */
export function NodeListPage() {
  const [filter, setFilter] = useNodeListFilter();
  const nodes = useInfiniteQuery(nodesQuery(filter));
  const types = useQuery(typesQuery);
  const title = filter.inbox ? "Inbox" : "Nodes";

  return (
    <section aria-labelledby="nodes-title" className="space-y-6">
      <h1 id="nodes-title" className="text-2xl font-semibold">
        {title}
      </h1>

      <CreateNodeForm inInbox={filter.inbox} />

      <div className="flex flex-wrap items-end gap-4" role="group" aria-label="Filtros">
        <div className="w-56 space-y-2">
          <Label htmlFor="filter-type">Type</Label>
          <Select value={filter.type} onValueChange={(type) => setFilter({ type })}>
            <SelectTrigger id="filter-type">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={ANY_TYPE}>Todos</SelectItem>
              <SelectItem value={NO_TYPE}>{NO_TYPE_LABEL}</SelectItem>
              {(types.data ?? []).map((type: TypeItem) => (
                <SelectItem key={type.id} value={type.id}>
                  {type.name}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          {types.isPending && (
            <p role="status" className="text-sm text-muted-foreground">
              Carregando Types…
            </p>
          )}
          {types.isError && (
            <p role="alert" className="text-sm text-destructive">
              {TYPES_LOAD_ERROR_MESSAGE}
            </p>
          )}
        </div>
        <label className="flex h-9 items-center gap-2 text-sm">
          <input type="checkbox" checked={filter.inbox} onChange={(event) => setFilter({ inbox: event.target.checked })} />
          Só a Inbox
        </label>
      </div>

      {nodes.isPending && <p role="status">Carregando…</p>}
      {nodes.isError && (
        <p role="alert" className="text-sm text-destructive">
          {LOAD_ERROR_MESSAGE}
        </p>
      )}
      {nodes.isSuccess && (
        <>
          {nodes.data.pages[0]?.data.length === 0 ? (
            <p className="text-muted-foreground">{filter.inbox ? "A Inbox está vazia." : "Nenhum Node com esse filtro."}</p>
          ) : (
            <ul className="divide-y rounded-md border" aria-label={title}>
              {nodes.data.pages.flatMap((page) =>
                page.data.map((node) => <NodeRow key={node.id} node={node} typeLabel={typeLabelOf(node.typeId, types)} />),
              )}
            </ul>
          )}
          {nodes.hasNextPage && (
            <Button type="button" variant="secondary" onClick={() => void nodes.fetchNextPage()} disabled={nodes.isFetchingNextPage}>
              {nodes.isFetchingNextPage ? "Carregando…" : "Carregar mais"}
            </Button>
          )}
        </>
      )}
    </section>
  );
}

/** "Sem Type" only for a Node without one; a Type still loading or that failed to load says so. */
function typeLabelOf(typeId: string | null, types: UseQueryResult<TypeItem[]>): string {
  if (typeId === null) {
    return NO_TYPE_LABEL;
  }

  if (types.isPending) {
    return TYPE_LOADING_LABEL;
  }

  return types.data?.find((type) => type.id === typeId)?.name ?? TYPE_UNAVAILABLE_LABEL;
}

function NodeRow({ node, typeLabel }: { node: NodeSummary; typeLabel: string }) {
  const archive = useArchiveNode(node.id);

  return (
    <li className="flex flex-wrap items-center justify-between gap-2 px-3 py-2">
      <div className="min-w-0 space-x-2">
        <Link to={`/nodes/${node.id}`} className="font-medium underline-offset-4 hover:underline">
          {node.title}
        </Link>
        <span className="text-sm text-muted-foreground">{typeLabel}</span>
        {node.inInbox && <span className="rounded border px-1.5 text-xs">Inbox</span>}
      </div>
      {node.inInbox && (
        <div className="flex items-center gap-2">
          {archive.isError && (
            <span role="alert" className="text-sm text-destructive">
              {writeErrorMessage(archive.error)}
            </span>
          )}
          <Button
            type="button"
            variant="secondary"
            size="sm"
            onClick={() => archive.mutate()}
            disabled={archive.isPending}
            aria-label={`Arquivar ${node.title}`}
          >
            Arquivar
          </Button>
        </div>
      )}
    </li>
  );
}

function CreateNodeForm({ inInbox }: { inInbox: boolean }) {
  const create = useCreateNode();
  const navigate = useNavigate();
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<CreateValues>({ resolver: zodResolver(createSchema), defaultValues: { title: "" } });

  const onSubmit = handleSubmit((values) => {
    if (create.isPending) {
      return;
    }

    create.mutate(
      { title: values.title, inInbox },
      {
        onSuccess: (receipt) => {
          const created = receipt.changes.find((change) => change.kind === "node");
          if (created) {
            void navigate(`/nodes/${created.entityId}`);
          }
        },
      },
    );
  });

  const describedBy = [errors.title && "new-node-title-error", create.isError && "new-node-error"].filter(Boolean).join(" ") || undefined;

  return (
    <form noValidate onSubmit={onSubmit} className="space-y-2" aria-label="Novo Node">
      <Label htmlFor="new-node-title">{inInbox ? "Capturar na Inbox" : "Novo Node"}</Label>
      <div className="flex gap-2">
        <Input
          id="new-node-title"
          maxLength={TITLE_MAX_LENGTH}
          placeholder="Título"
          aria-invalid={errors.title ? true : undefined}
          aria-describedby={describedBy}
          {...register("title")}
        />
        <Button type="submit" disabled={create.isPending}>
          {create.isPending ? "Criando…" : "Criar"}
        </Button>
      </div>
      {errors.title && (
        <p id="new-node-title-error" role="alert" className="text-sm text-destructive">
          {errors.title.message}
        </p>
      )}
      {create.isError && (
        <p id="new-node-error" role="alert" className="text-sm text-destructive">
          {writeErrorMessage(create.error)}
        </p>
      )}
    </form>
  );
}
