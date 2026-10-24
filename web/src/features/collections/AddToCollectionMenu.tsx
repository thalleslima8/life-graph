import { useQuery } from "@tanstack/react-query";
import { useId, useState } from "react";
import { Link } from "react-router";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { LOAD_ERROR_MESSAGE, writeErrorMessage } from "@/features/graph/graphMessages";
import { collectionsQuery, isManual, useAddToCollection } from "./collectionsData";

/** Adds the Node to one of the person's manual Collections; a dynamic one follows its criteria (DA-164). */
export function AddToCollectionMenu({ nodeId }: { nodeId: string }) {
  const id = useId();
  const collections = useQuery(collectionsQuery);
  const [collectionId, setCollectionId] = useState("");
  const add = useAddToCollection(collectionId);
  const manual = (collections.data ?? []).filter(isManual);
  const added = add.isSuccess && add.variables === nodeId;

  if (collections.isPending) {
    return (
      <p role="status" className="text-sm text-muted-foreground">
        Carregando Collections…
      </p>
    );
  }

  if (collections.isError) {
    return (
      <div className="space-y-1">
        <p role="alert" className="text-sm text-destructive">
          {LOAD_ERROR_MESSAGE}
        </p>
        <Button type="button" variant="secondary" size="sm" onClick={() => void collections.refetch()}>
          Tentar de novo
        </Button>
      </div>
    );
  }

  if (manual.length === 0) {
    return (
      <p className="text-sm text-muted-foreground">
        Nenhuma Collection manual.{" "}
        <Link to="/collections" className="underline underline-offset-4">
          Criar em Collections
        </Link>
      </p>
    );
  }

  return (
    <form
      noValidate
      aria-label="Adicionar a uma Collection"
      className="flex flex-wrap items-end gap-2"
      onSubmit={(event) => {
        event.preventDefault();
        if (collectionId !== "" && !add.isPending) {
          add.mutate(nodeId);
        }
      }}
    >
      <div className="space-y-1">
        <Label htmlFor={id}>Collection manual</Label>
        <Select
          value={collectionId}
          onValueChange={(next) => {
            setCollectionId(next);
            add.reset();
          }}
        >
          <SelectTrigger id={id} className="w-56">
            <SelectValue placeholder="Escolha uma Collection" />
          </SelectTrigger>
          <SelectContent>
            {manual.map((collection) => (
              <SelectItem key={collection.id} value={collection.id}>
                {collection.name}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
      <Button type="submit" size="sm" disabled={collectionId === "" || add.isPending}>
        Adicionar
      </Button>
      {added && (
        <p role="status" className="w-full text-sm text-muted-foreground">
          Adicionado.
        </p>
      )}
      {add.isError && (
        <p role="alert" className="w-full text-sm text-destructive">
          {writeErrorMessage(add.error)}
        </p>
      )}
    </form>
  );
}
