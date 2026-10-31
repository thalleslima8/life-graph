import { zodResolver } from "@hookform/resolvers/zod";
import { useQuery } from "@tanstack/react-query";
import { useEffect, useId, useRef, useState } from "react";
import { useForm, useWatch } from "react-hook-form";
import { Link, useNavigate, useSearchParams } from "react-router";
import { isApiError } from "@/api/client";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { isNotFound } from "@/features/graph/graphData";
import {
  LOAD_ERROR_MESSAGE,
  LOADING_MESSAGE,
  NO_TYPE_LABEL,
  RETRY_LABEL,
  writeErrorMessage,
} from "@/features/graph/graphMessages";
import {
  createShareSchema,
  isRootNotShareable,
  isShareLimitReached,
  SHARE_DEPTHS,
  shareDepthOf,
  shareProposalOptions,
  sharingErrorCodes,
  useCreateShare,
  type CreateShareFormInput,
  type CreateShareInput,
  type ShareDepth,
  type SharedGraphViewProposal,
} from "./sharesData";

const TITLE_ID = "new-share-title";
const NO_ROOT_MESSAGE = "Escolha no Inspector o Node que será a raiz do link.";
const ROOT_NOT_FOUND_MESSAGE = "Este Node não foi encontrado.";
export const ROOT_NOT_SHAREABLE_MESSAGE =
  "A Raiz da sua conta nunca é compartilhada. Escolha outro Node como raiz do link.";
export const NOTHING_ELIGIBLE_MESSAGE =
  "Nenhum Node pode entrar neste link: a raiz está oculta para agentes. Um Node oculto nunca é compartilhado; desoculte-o antes, se quiser compartilhá-lo.";
export const LIMIT_REACHED_MESSAGE =
  "Você já tem 3 links. Links suspensos ou desativados pela plataforma também contam: revogue um para criar outro.";
const PROPOSAL_CHANGED_MESSAGE =
  "O grafo mudou desde a proposta. Volte e revise a lista de novo.";
export const LEGAL_NOTICE_TEXT =
  "O link fica visível para qualquer pessoa que o tiver, sem entrar numa conta, e pode ser repassado. Você tem o direito de compartilhar os dados de outras pessoas que estão nestes Nodes?";
export const LEGAL_NOTICE_ACK_LABEL =
  "Li o aviso e tenho o direito de compartilhar estes dados.";
export const TAGS_WARNING =
  "Com as Tags ligadas, as Tags que você adicionar depois a estes Nodes também aparecem no link.";
export const RESOURCE_URL_WARNING =
  "Título, conteúdo, Type e a URL de cada Resource sempre aparecem. Se uma URL for um link assinado ou privado, quem tiver o link compartilhado também poderá abri-la.";
export const LEAVE_WARNING =
  "Este link aparece só agora. Se você sair desta página sem copiá-lo, ele não pode ser mostrado de novo.";
export const COPIED_MESSAGE = "Link copiado.";
const COPY_FAILED_MESSAGE =
  "Não foi possível copiar. Selecione o link e copie-o.";

type Step = "review" | "notice" | "link";

const STEP_TITLES: Readonly<Record<Step, string>> = {
  review: "1. Revise os Nodes",
  notice: "2. Aviso",
  link: "3. Seu link",
};

/**
 * Creating a SharedGraphView (DA-075, DA-222): the proposed Nodes from a root, the person's
 * review, the legal notice, then the link shown once. The page owns its states; a 401 is the
 * app shell's.
 */
export function NewSharePage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const rootNodeId = searchParams.get("rootNodeId");
  const depth = shareDepthOf(searchParams.get("depth"));

  const changeDepth = (next: ShareDepth) =>
    setSearchParams(
      (current) => {
        const params = new URLSearchParams(current);
        params.set("depth", String(next));
        return params;
      },
      { replace: true },
    );

  return (
    <section aria-labelledby={TITLE_ID} className="space-y-6">
      <h1 id={TITLE_ID} className="text-2xl font-semibold">
        Compartilhar por link
      </h1>
      {rootNodeId ? (
        <Proposal
          rootNodeId={rootNodeId}
          depth={depth}
          onDepthChange={changeDepth}
        />
      ) : (
        <p>{NO_ROOT_MESSAGE}</p>
      )}
    </section>
  );
}

function Proposal({
  rootNodeId,
  depth,
  onDepthChange,
}: {
  rootNodeId: string;
  depth: ShareDepth;
  onDepthChange: (depth: ShareDepth) => void;
}) {
  const proposal = useQuery(shareProposalOptions(rootNodeId, depth));

  // Data first: a failed background read never takes the flow (and a link on screen) away.
  if (proposal.data) {
    return proposal.data.nodes.length === 0 ? (
      <p role="alert">{NOTHING_ELIGIBLE_MESSAGE}</p>
    ) : (
      <ShareFlow
        key={`${rootNodeId}-${depth}`}
        proposal={proposal.data}
        depth={depth}
        onDepthChange={onDepthChange}
      />
    );
  }

  if (proposal.isPending) {
    return <p>{LOADING_MESSAGE}</p>;
  }

  if (isRootNotShareable(proposal.error)) {
    return <p role="alert">{ROOT_NOT_SHAREABLE_MESSAGE}</p>;
  }

  if (isNotFound(proposal.error)) {
    return (
      <div className="space-y-2">
        <p role="alert">{ROOT_NOT_FOUND_MESSAGE}</p>
        <Link to="/nodes" className="underline underline-offset-4">
          Voltar para os Nodes
        </Link>
      </div>
    );
  }

  return (
    <div className="space-y-2">
      <p role="alert" className="text-sm text-destructive">
        {LOAD_ERROR_MESSAGE}
      </p>
      <Button
        type="button"
        variant="secondary"
        size="sm"
        onClick={() => void proposal.refetch()}
      >
        {RETRY_LABEL}
      </Button>
    </div>
  );
}

function createErrorMessage(error: unknown): string {
  if (isShareLimitReached(error)) {
    return LIMIT_REACHED_MESSAGE;
  }

  if (isRootNotShareable(error)) {
    return ROOT_NOT_SHAREABLE_MESSAGE;
  }

  if (
    isApiError(error, sharingErrorCodes.nodeNotEligible) ||
    isApiError(error, sharingErrorCodes.propertyNotEligible)
  ) {
    return PROPOSAL_CHANGED_MESSAGE;
  }

  return writeErrorMessage(error);
}

function ShareFlow({
  proposal,
  depth,
  onDepthChange,
}: {
  proposal: SharedGraphViewProposal;
  depth: ShareDepth;
  onDepthChange: (depth: ShareDepth) => void;
}) {
  const [step, setStep] = useState<Step>("review");
  const [moved, setMoved] = useState(false);
  const stepHeading = useRef<HTMLHeadingElement>(null);
  const create = useCreateShare();
  const ids = useId();
  const form = useForm<CreateShareFormInput, unknown, CreateShareInput>({
    resolver: zodResolver(createShareSchema),
    defaultValues: {
      rootNodeId: proposal.rootNodeId,
      maxDepth: depth,
      nodeIds: proposal.nodes.map((node) => node.id),
      propertyIds: [],
      includeTags: false,
      legalNoticeAccepted: false,
    },
  });
  const includeTags = useWatch({ control: form.control, name: "includeTags" });
  const nodeIds = useWatch({ control: form.control, name: "nodeIds" });
  const { errors } = form.formState;

  const toggleNode = (nodeId: string, kept: boolean) =>
    form.setValue(
      "nodeIds",
      kept ? [...nodeIds, nodeId] : nodeIds.filter((id) => id !== nodeId),
    );

  // Each step starts at its heading (FE-032); the first one keeps the page's natural focus.
  useEffect(() => {
    if (moved) {
      stepHeading.current?.focus();
    }
  }, [moved, step]);

  const goTo = (next: Step) => {
    setMoved(true);
    setStep(next);
  };

  const toNotice = async () => {
    if (await form.trigger("nodeIds")) {
      goTo("notice");
    }
  };

  const submit = form.handleSubmit((input) =>
    create.mutate(input, { onSuccess: () => goTo("link") }),
  );

  return (
    <div className="space-y-4">
      <h2 ref={stepHeading} tabIndex={-1} className="text-xl font-semibold">
        {STEP_TITLES[step]}
      </h2>

      {step === "link" && create.data ? (
        <ShareLink
          shareUrl={create.data.shareUrl}
          rootNodeId={proposal.rootNodeId}
        />
      ) : (
        <form
          noValidate
          onSubmit={(event) => void submit(event)}
          className="space-y-4"
        >
          <div hidden={step !== "review"} className="space-y-4">
            <fieldset className="space-y-1">
              <legend className="text-sm font-medium">Profundidade</legend>
              <div className="flex gap-4">
                {SHARE_DEPTHS.map((option) => (
                  <label
                    key={option}
                    className="flex items-center gap-2 text-sm"
                  >
                    <input
                      type="radio"
                      name={`${ids}-depth`}
                      checked={option === depth}
                      onChange={() => onDepthChange(option)}
                    />
                    {option === 1
                      ? "Vizinhos diretos"
                      : "Até 2 Relations de distância"}
                  </label>
                ))}
              </div>
            </fieldset>

            <fieldset
              className="space-y-1"
              aria-describedby={`${ids}-nodes-hint`}
            >
              <legend className="text-sm font-medium">Nodes no link</legend>
              <p
                id={`${ids}-nodes-hint`}
                className="text-sm text-muted-foreground"
              >
                {proposal.truncated
                  ? `${proposal.nodes.length} de ${proposal.eligibleCount} elegíveis: um link tem no máximo 25 Nodes, os mais próximos da raiz.`
                  : `${proposal.nodes.length} de ${proposal.eligibleCount} elegíveis. Desmarque o que não quer compartilhar.`}
              </p>
              <ul className="space-y-1">
                {proposal.nodes.map((node) => (
                  <li key={node.id}>
                    <label className="flex items-center gap-2 text-sm">
                      {/* The root is always in the link (DA-075). */}
                      <input
                        type="checkbox"
                        checked={nodeIds.includes(node.id)}
                        disabled={node.distance === 0}
                        onChange={(event) =>
                          toggleNode(node.id, event.target.checked)
                        }
                      />
                      <span className="break-words">{node.title}</span>
                      <span className="text-muted-foreground">
                        {node.typeName ?? NO_TYPE_LABEL}
                        {node.distance === 0 ? " · raiz" : ""}
                      </span>
                    </label>
                  </li>
                ))}
              </ul>
            </fieldset>

            {proposal.properties.length > 0 && (
              <fieldset className="space-y-1">
                <legend className="text-sm font-medium">
                  Propriedades visíveis (nenhuma por padrão)
                </legend>
                {proposal.properties.map((property) => (
                  <label
                    key={property.id}
                    className="flex items-center gap-2 text-sm"
                  >
                    <input
                      type="checkbox"
                      value={property.id}
                      {...form.register("propertyIds")}
                    />
                    {property.name}
                  </label>
                ))}
              </fieldset>
            )}

            <div className="space-y-1">
              <label className="flex items-center gap-2 text-sm">
                <input
                  type="checkbox"
                  aria-describedby={
                    includeTags ? `${ids}-tags-warning` : undefined
                  }
                  {...form.register("includeTags")}
                />
                Mostrar as Tags
              </label>
              {includeTags && (
                <p
                  id={`${ids}-tags-warning`}
                  className="text-sm text-muted-foreground"
                >
                  {TAGS_WARNING}
                </p>
              )}
            </div>

            <p className="text-sm text-muted-foreground">
              {RESOURCE_URL_WARNING}
            </p>

            <Button type="button" onClick={() => void toNotice()}>
              Continuar
            </Button>
          </div>

          <div hidden={step !== "notice"} className="space-y-4">
            <p id={`${ids}-notice`}>{LEGAL_NOTICE_TEXT}</p>
            <div className="space-y-1">
              <label className="flex items-center gap-2 text-sm">
                <input
                  type="checkbox"
                  aria-describedby={`${ids}-notice`}
                  aria-invalid={errors.legalNoticeAccepted ? true : undefined}
                  {...form.register("legalNoticeAccepted")}
                />
                {LEGAL_NOTICE_ACK_LABEL}
              </label>
              {errors.legalNoticeAccepted && (
                <p role="alert" className="text-sm text-destructive">
                  {errors.legalNoticeAccepted.message}
                </p>
              )}
            </div>
            {create.isError && (
              <p role="alert" className="text-sm text-destructive">
                {createErrorMessage(create.error)}
              </p>
            )}
            <div className="flex gap-2">
              <Button
                type="button"
                variant="secondary"
                onClick={() => goTo("review")}
              >
                Voltar
              </Button>
              <Button
                type="submit"
                disabled={create.isPending || isShareLimitReached(create.error)}
              >
                {create.isPending ? "Criando…" : "Criar link"}
              </Button>
            </div>
          </div>
        </form>
      )}
    </div>
  );
}

function ShareLink({
  shareUrl,
  rootNodeId,
}: {
  shareUrl: string;
  rootNodeId: string;
}) {
  const navigate = useNavigate();
  const [announcement, setAnnouncement] = useState("");
  const ids = useId();

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(shareUrl);
      setAnnouncement(COPIED_MESSAGE);
    } catch {
      setAnnouncement(COPY_FAILED_MESSAGE);
    }
  };

  return (
    <div className="space-y-4">
      <p role="alert" className="text-sm font-medium">
        {LEAVE_WARNING}
      </p>
      <div className="space-y-1">
        <label htmlFor={`${ids}-url`} className="text-sm font-medium">
          Link compartilhado
        </label>
        <Input
          id={`${ids}-url`}
          readOnly
          value={shareUrl}
          onFocus={(event) => event.currentTarget.select()}
        />
      </div>
      <p role="status" className="text-sm">
        {announcement}
      </p>
      <div className="flex gap-2">
        <Button type="button" variant="secondary" onClick={() => void copy()}>
          Copiar link
        </Button>
        <Button
          type="button"
          onClick={() => void navigate(`/nodes/${rootNodeId}`)}
        >
          Já copiei
        </Button>
      </div>
    </div>
  );
}
