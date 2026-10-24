import { zodResolver } from "@hookform/resolvers/zod";
import { useMutation } from "@tanstack/react-query";
import { useForm } from "react-hook-form";
import { useNavigate } from "react-router";
import { api, isApiError, unwrap } from "@/api/client";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { useLinkToken, type LinkToken } from "@/features/session/loaders";
import {
  INVALID_OR_EXPIRED_TOKEN_CODE,
  NEW_PASSWORD_HINT,
  PASSWORD_REJECTED_CODE,
  PASSWORD_REJECTED_MESSAGE,
  formErrorMessage,
} from "./messages";
import { PASSWORD_MAX_LENGTH, newPasswordSchema, type NewPasswordValues } from "./passwordSchema";

/** The page of the link e-mailed when the owner creates the account: confirms the e-mail and sets the password (DA-095). */
export function ConfirmEmailPage() {
  const linkToken = useLinkToken();
  const navigate = useNavigate();
  const confirm = useMutation({
    mutationFn: async ({ link, password }: { link: LinkToken; password: string }) => {
      unwrap(await api.POST("/api/email-confirmations", { body: { userId: link.userId, token: link.token, password } }));
    },
    // No automatic sign-in: the user proves the new password on the login page (DA-097).
    onSuccess: () => navigate("/login?notice=password_set", { replace: true }),
  });
  const {
    register,
    handleSubmit,
    setError,
    formState: { errors },
  } = useForm<NewPasswordValues>({ resolver: zodResolver(newPasswordSchema), defaultValues: { password: "", confirmation: "" } });

  const isLinkInvalid = !linkToken || isApiError(confirm.error, INVALID_OR_EXPIRED_TOKEN_CODE);

  const onSubmit = handleSubmit(({ password }) => {
    if (!linkToken || confirm.isPending) {
      return;
    }

    confirm.mutate(
      { link: linkToken, password },
      {
        onError: (error) => {
          if (isApiError(error, PASSWORD_REJECTED_CODE)) {
            setError("password", { message: PASSWORD_REJECTED_MESSAGE });
          }
        },
      },
    );
  });

  return (
    <section aria-labelledby="confirm-email-title" className="mx-auto max-w-sm space-y-6">
      <h1 id="confirm-email-title" className="text-2xl font-semibold">
        Definir senha
      </h1>

      {isLinkInvalid ? (
        <p role="alert" className="text-sm">
          Este link é inválido ou expirou. Peça a quem criou sua conta que envie um novo link.
        </p>
      ) : (
        <form noValidate onSubmit={onSubmit} className="space-y-4">
          <p className="text-sm text-muted-foreground">Escolha a senha da sua conta. Isso também confirma o seu e-mail.</p>

          <div className="space-y-2">
            <Label htmlFor="confirm-email-password">Nova senha</Label>
            <Input
              id="confirm-email-password"
              type="password"
              autoComplete="new-password"
              maxLength={PASSWORD_MAX_LENGTH}
              aria-invalid={errors.password ? true : undefined}
              aria-describedby={errors.password ? "confirm-email-password-error" : "confirm-email-password-hint"}
              {...register("password")}
            />
            {errors.password ? (
              <p id="confirm-email-password-error" role="alert" className="text-sm text-destructive">
                {errors.password.message}
              </p>
            ) : (
              <p id="confirm-email-password-hint" className="text-sm text-muted-foreground">
                {NEW_PASSWORD_HINT}
              </p>
            )}
          </div>

          <div className="space-y-2">
            <Label htmlFor="confirm-email-confirmation">Repita a senha</Label>
            <Input
              id="confirm-email-confirmation"
              type="password"
              autoComplete="new-password"
              maxLength={PASSWORD_MAX_LENGTH}
              aria-invalid={errors.confirmation ? true : undefined}
              aria-describedby={errors.confirmation ? "confirm-email-confirmation-error" : undefined}
              {...register("confirmation")}
            />
            {errors.confirmation && (
              <p id="confirm-email-confirmation-error" role="alert" className="text-sm text-destructive">
                {errors.confirmation.message}
              </p>
            )}
          </div>

          {confirm.isError && !isApiError(confirm.error, PASSWORD_REJECTED_CODE) && (
            <p role="alert" className="text-sm text-destructive">
              {formErrorMessage(confirm.error)}
            </p>
          )}

          <Button type="submit" className="w-full" disabled={confirm.isPending}>
            {confirm.isPending ? "Salvando…" : "Definir senha"}
          </Button>
        </form>
      )}
    </section>
  );
}
