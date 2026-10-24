import { zodResolver } from "@hookform/resolvers/zod";
import { useForm } from "react-hook-form";
import { z, type ZodType } from "zod";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { NAME_MAX_LENGTH } from "./graphData";
import { writeErrorMessage } from "./graphMessages";

/** A Type or Property Definition name; the server checks again (FE-020). */
const entityNameSchema = z.string().trim().min(1, "Informe um nome.").max(NAME_MAX_LENGTH, `Use no máximo ${NAME_MAX_LENGTH} caracteres.`);

export type NameSubmitter = {
  /** Gets the name as `schema` outputs it. */
  submit(name: string, onDone: () => void): void;
  isPending: boolean;
  error: unknown;
};

type NameFormProps = {
  label: string;
  idPrefix: string;
  initialName?: string;
  submitLabel?: string;
  action: NameSubmitter;
  /** The rules of the name; a Type's by default. */
  schema?: ZodType<string, string>;
  maxLength?: number;
};

/** One name field and a button: create something with a name, or rename it. */
export function NameForm({
  label,
  idPrefix,
  initialName = "",
  submitLabel = "Criar",
  action,
  schema = entityNameSchema,
  maxLength = NAME_MAX_LENGTH,
}: NameFormProps) {
  const {
    register,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<{ name: string }, unknown, { name: string }>({ resolver: zodResolver(z.object({ name: schema })), defaultValues: { name: initialName } });
  const fieldId = `${idPrefix}-name`;

  const onSubmit = handleSubmit(({ name }) => {
    if (!action.isPending) {
      action.submit(name, () => reset({ name: initialName ? name : "" }));
    }
  });

  return (
    <form noValidate onSubmit={onSubmit} aria-label={label} className="space-y-2">
      <Label htmlFor={fieldId}>{label}</Label>
      <div className="flex gap-2">
        <Input
          id={fieldId}
          maxLength={maxLength}
          aria-invalid={errors.name ? true : undefined}
          aria-describedby={errors.name ? `${fieldId}-error` : undefined}
          {...register("name")}
        />
        <Button type="submit" variant="secondary" disabled={action.isPending}>
          {submitLabel}
        </Button>
      </div>
      {errors.name && (
        <p id={`${fieldId}-error`} role="alert" className="text-sm text-destructive">
          {errors.name.message}
        </p>
      )}
      {action.error !== null && action.error !== undefined && (
        <p role="alert" className="text-sm text-destructive">
          {writeErrorMessage(action.error)}
        </p>
      )}
    </form>
  );
}
