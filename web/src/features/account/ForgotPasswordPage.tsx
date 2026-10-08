import { zodResolver } from "@hookform/resolvers/zod";
import { useMutation } from "@tanstack/react-query";
import { useForm } from "react-hook-form";
import { Link } from "react-router";
import { z } from "zod";
import { api, unwrap } from "@/api/client";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { formErrorMessage, serverFieldErrors } from "./messages";
import { EMAIL_MAX_LENGTH, emailSchema } from "./passwordSchema";

const forgotPasswordSchema = z.object({ email: emailSchema });

type ForgotPasswordValues = z.infer<typeof forgotPasswordSchema>;

export function ForgotPasswordPage() {
  const requestReset = useMutation({
    mutationFn: async ({ email }: ForgotPasswordValues) => {
      unwrap(await api.POST("/api/password-resets", { body: { email } }));
    },
  });
  const {
    register,
    handleSubmit,
    setError,
    formState: { errors },
  } = useForm<ForgotPasswordValues>({ resolver: zodResolver(forgotPasswordSchema), defaultValues: { email: "" } });

  const onSubmit = handleSubmit((values) => {
    if (requestReset.isPending) {
      return;
    }

    requestReset.mutate(values, {
      onError: (error) => {
        for (const [field, message] of serverFieldErrors(error, ["email"] as const)) {
          setError(field, { message });
        }
      },
    });
  });

  return (
    <section aria-labelledby="forgot-password-title" className="mx-auto max-w-sm space-y-6">
      <h1 id="forgot-password-title" className="text-2xl font-semibold">
        Esqueci minha senha
      </h1>

      {requestReset.isSuccess ? (
        // The same answer whether or not the e-mail has an account (DA-096).
        <p role="status" className="rounded-md border px-3 py-2 text-sm">
          Se houver uma conta ativa com esse e-mail, enviamos um link para redefinir a senha. O link vale por 2
          horas.
        </p>
      ) : (
        <form noValidate onSubmit={onSubmit} className="space-y-4">
          <p className="text-sm text-muted-foreground">Informe o e-mail da sua conta para receber um link de redefinição.</p>
          <div className="space-y-2">
            <Label htmlFor="forgot-password-email">E-mail</Label>
            <Input
              id="forgot-password-email"
              type="email"
              autoComplete="email"
              maxLength={EMAIL_MAX_LENGTH}
              aria-invalid={errors.email ? true : undefined}
              aria-describedby={errors.email ? "forgot-password-email-error" : undefined}
              {...register("email")}
            />
            {errors.email && (
              <p id="forgot-password-email-error" role="alert" className="text-sm text-destructive">
                {errors.email.message}
              </p>
            )}
          </div>

          {requestReset.isError && (
            <p role="alert" className="text-sm text-destructive">
              {formErrorMessage(requestReset.error)}
            </p>
          )}

          <Button type="submit" className="w-full" disabled={requestReset.isPending}>
            {requestReset.isPending ? "Enviando…" : "Enviar link"}
          </Button>
        </form>
      )}

      <p className="text-sm">
        <Link to="/login" className="underline underline-offset-4">
          Voltar para o login
        </Link>
      </p>
    </section>
  );
}
