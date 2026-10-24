import { useQuery } from "@tanstack/react-query";
import { useId, useState } from "react";
import { Button } from "@/components/ui/button";
import { Dialog, DialogClose, DialogContent } from "@/components/ui/dialog";
import { nameKeyOf } from "@/lib/utils";
import {
  ALLOWED_KIND_TRANSITIONS,
  propertyDefinitionsQuery,
  typesQuery,
  useAttachPropertyToType,
  type NodeDetail,
  type NodePropertyValue,
  type PropertyDefinitionItem,
  type TypeItem,
} from "./graphData";
import { VALUE_KIND_LABELS } from "./graphMessages";
import { RefusalOrError } from "./ontologyParts";

type PromoteDialogProps = {
  node: NodeDetail;
  /** A row of the Node's Outras propriedades. */
  property: NodePropertyValue;
};

/**
 * Promotes one of the Node's Outras propriedades: attaches its own definition to the Node's
 * Type, so every Node of that Type shows it (DA-066, DA-067). Before that, it lists the existing
 * definitions with the same name and a compatible kind, so the person does not end up with a
 * duplicate; mapping the value to one of them is not offered (DA-170).
 */
export function PromoteDialog({ node, property }: PromoteDialogProps) {
  const definitions = useQuery(propertyDefinitionsQuery);
  const types = useQuery(typesQuery);
  const [open, setOpen] = useState(false);
  const reasonId = useId();

  const definition = definitions.data?.find((item) => item.id === property.propertyDefinitionId);
  const type = types.data?.find((item) => item.id === node.typeId);
  const blocked = promotionBlockOf(node, definition);
  const ready = definition !== undefined && type !== undefined && blocked === null;

  return (
    <div className="flex flex-wrap items-center gap-2">
      <Button
        type="button"
        variant="secondary"
        size="sm"
        aria-label={`Promover ${property.name}`}
        aria-describedby={blocked ? reasonId : undefined}
        disabled={!ready}
        onClick={() => setOpen(true)}
      >
        Promover
      </Button>
      {blocked && (
        <span id={reasonId} className="text-xs text-muted-foreground">
          {blocked}
        </span>
      )}
      {open && ready && (
        <PromoteConfirmation
          definition={definition}
          type={type}
          candidates={promotionCandidatesOf(definition, definitions.data ?? [])}
          onOpenChange={setOpen}
        />
      )}
    </div>
  );
}

/** Why this row cannot be promoted, or null when it can. */
function promotionBlockOf(node: NodeDetail, definition: PropertyDefinitionItem | undefined): string | null {
  if (node.typeId === null || node.typeId === undefined) {
    return "Sem Type: escolha um Type para o Node antes de promover.";
  }

  if (definition?.isSystem) {
    return "Propriedade do sistema: fica só no Type do sistema.";
  }

  if (definition?.isArchived) {
    return "Propriedade arquivada: restaure-a em Ontologia para promover.";
  }

  return null;
}

/**
 * The other definitions the person may have meant: the same name ignoring case and accents, a
 * kind the value could take (DA-065) and not archived (DA-167).
 */
function promotionCandidatesOf(definition: PropertyDefinitionItem, definitions: PropertyDefinitionItem[]): PropertyDefinitionItem[] {
  const key = nameKeyOf(definition.name);
  const kinds = [definition.valueKind, ...ALLOWED_KIND_TRANSITIONS[definition.valueKind]];
  return definitions.filter(
    (candidate) =>
      candidate.id !== definition.id && !candidate.isArchived && nameKeyOf(candidate.name) === key && kinds.includes(candidate.valueKind),
  );
}

function PromoteConfirmation({
  definition,
  type,
  candidates,
  onOpenChange,
}: {
  definition: PropertyDefinitionItem;
  type: TypeItem;
  candidates: PropertyDefinitionItem[];
  onOpenChange: (open: boolean) => void;
}) {
  const attach = useAttachPropertyToType(type.id);

  const confirm = () => {
    if (!attach.isPending) {
      attach.mutate(definition.id, { onSuccess: () => onOpenChange(false) });
    }
  };

  return (
    <Dialog open onOpenChange={onOpenChange}>
      <DialogContent
        title={`Promover ${definition.name} para o Type ${type.name}?`}
        description={`A propriedade passa a fazer parte do Type ${type.name}: todos os Nodes desse Type a mostram, e os que guardam um valor nela voltam a vê-lo. Dá para desfazer depois.`}
      >
        {candidates.length > 0 && (
          <div className="space-y-1 text-sm">
            <p>Já existem propriedades parecidas:</p>
            <ul className="list-disc pl-5">
              {candidates.map((candidate) => (
                <li key={candidate.id}>
                  {candidate.name} ({VALUE_KIND_LABELS[candidate.valueKind]}){" "}
                  {type.properties.some((attached) => attached.propertyDefinitionId === candidate.id)
                    ? `— já está no Type ${type.name}`
                    : ""}
                </li>
              ))}
            </ul>
            <p className="text-muted-foreground">
              Mover o valor para uma delas ainda não é possível: promover anexa {definition.name} ao Type.
            </p>
          </div>
        )}
        <RefusalOrError error={attach.error} />
        <div className="flex justify-end gap-2">
          <DialogClose asChild>
            <Button type="button" variant="ghost">
              Cancelar
            </Button>
          </DialogClose>
          <Button type="button" onClick={confirm} disabled={attach.isPending}>
            Promover
          </Button>
        </div>
      </DialogContent>
    </Dialog>
  );
}
