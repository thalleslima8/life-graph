import { zodResolver } from "@hookform/resolvers/zod";
import { useMutation } from "@tanstack/react-query";
import { useForm } from "react-hook-form";
import { Link, useNavigate } from "react-router";
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

/** The page of the password recovery link: sets a new password and ends every open session (DA-097). */
export function ResetPasswordPage() {
  const linkToken = useLinkToken();
  const navigate = useNavigate();
  const resetPassword = useMutation({
    mutationFn: async ({ link, password }: { link: LinkToken; password: string }) => {
      unwrap(await api.POST("/api/password-resets/completion", { body: { userId: link.userId, token: link.token, newPassword: password } }));
    },
    // No automatic sign-in: the user proves the new password on the login page (DA-097).
    onSuccess: () => navigate("/login?notice=password_reset", { replace: true }),
  });
  const {
    register,
    handleSubmit,
    setError,
    formState: { errors },
  } = useForm<NewPasswordValues>({ resolver: zodResolver(newPasswordSchema), defaultValues: { password: "", confirmation: "" } });

  const isLinkInvalid = !linkToken || isApiError(resetPassword.error, INVALID_OR_EXPIRED_TOKEN_CODE);

  const onSubmit = handleSubmit(({ password }) => {
    if (!linkToken || resetPassword.isPending) {
      return;
    }

    resetPassword.mutate(
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
    <section aria-labelledby="reset-password-title" className="mx-auto max-w-sm space-y-6">
      <h1 id="reset-password-title" className="text-2xl font-semibold">
        Redefinir senha
      </h1>

      {isLinkInvalid ? (
        <p role="alert" className="text-sm">
          Este link é inválido ou expirou.{" "}
          <Link to="/forgot-password" className="underline underline-offset-4">
            Peça um novo link
          </Link>
          .
        </p>
      ) : (
        <form noValidate onSubmit={onSubmit} className="space-y-4">
          <p className="text-sm text-muted-foreground">Escolha a nova senha. Todas as sessões abertas da sua conta serão encerradas.</p>

          <div className="space-y-2">
            <Label htmlFor="reset-password-password">Nova senha</Label>
            <Input
              id="reset-password-password"
              type="password"
              autoComplete="new-password"
              maxLength={PASSWORD_MAX_LENGTH}
              aria-invalid={errors.password ? true : undefined}
              aria-describedby={errors.password ? "reset-password-password-error" : "reset-password-password-hint"}
              {...register("password")}
            />
            {errors.password ? (
              <p id="reset-password-password-error" role="alert" className="text-sm text-destructive">
                {errors.password.message}
              </p>
            ) : (
              <p id="reset-password-password-hint" className="text-sm text-muted-foreground">
                {NEW_PASSWORD_HINT}
              </p>
            )}
          </div>

          <div className="space-y-2">
            <Label htmlFor="reset-password-confirmation">Repita a senha</Label>
            <Input
              id="reset-password-confirmation"
              type="password"
              autoComplete="new-password"
              maxLength={PASSWORD_MAX_LENGTH}
              aria-invalid={errors.confirmation ? true : undefined}
              aria-describedby={errors.confirmation ? "reset-password-confirmation-error" : undefined}
              {...register("confirmation")}
            />
            {errors.confirmation && (
              <p id="reset-password-confirmation-error" role="alert" className="text-sm text-destructive">
                {errors.confirmation.message}
              </p>
            )}
          </div>

          {resetPassword.isError && !isApiError(resetPassword.error, PASSWORD_REJECTED_CODE) && (
            <p role="alert" className="text-sm text-destructive">
              {formErrorMessage(resetPassword.error)}
            </p>
          )}

          <Button type="submit" className="w-full" disabled={resetPassword.isPending}>
            {resetPassword.isPending ? "Salvando…" : "Redefinir senha"}
          </Button>
        </form>
      )}
    </section>
  );
}
