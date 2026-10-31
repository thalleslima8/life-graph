import { zodResolver } from "@hookform/resolvers/zod";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { Link, useLocation, useSearchParams } from "react-router";
import { z } from "zod";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { useLogin } from "@/features/session/useLogin";
import { formatDateTime } from "@/features/graph/graphMessages";
import type { AccountDeletedNotice, AccountDeletionReceipt } from "./accountData";
import { DELETION_MESSAGES } from "./accountMessages";
import { formErrorMessage, serverFieldErrors } from "./messages";
import { EMAIL_MAX_LENGTH, PASSWORD_MAX_LENGTH, emailSchema } from "./passwordSchema";

const loginSchema = z.object({
  email: emailSchema,
  password: z.string().min(1, "Informe a senha.").max(PASSWORD_MAX_LENGTH, "Senha longa demais."),
});

type LoginValues = z.infer<typeof loginSchema>;

const NOTICES = new Map([
  ["password_set", "Senha definida. Entre com seu e-mail e a nova senha."],
  ["password_reset", "Senha redefinida. Entre com a nova senha."],
  ["session_expired", "Sua sessão expirou. Entre novamente."],
]);

const LOGIN_ERRORS = { "accounts.invalid_credentials": "E-mail ou senha incorretos." };

/** The deletion's notice handed over in the router state (DA-255), or null for anything else. */
function accountDeletedNotice(state: unknown): AccountDeletedNotice | null {
  if (typeof state !== "object" || state === null || !("notice" in state) || state.notice !== "account_deleted") {
    return null;
  }

  const receipt = "receipt" in state ? deletionReceipt(state.receipt) : undefined;
  return receipt === undefined ? { notice: "account_deleted" } : { notice: "account_deleted", receipt };
}

function deletionReceipt(value: unknown): AccountDeletionReceipt | undefined {
  if (typeof value !== "object" || value === null || !("reference" in value) || !("date" in value)) {
    return undefined;
  }

  const { reference, date } = value;
  return typeof reference === "string" && typeof date === "string" ? { reference, date } : undefined;
}

export function LoginPage() {
  const [searchParams] = useSearchParams();
  const location = useLocation();
  // Read once and never stored: losing it on a reload is accepted (DA-255).
  const [accountDeleted] = useState(() => accountDeletedNotice(location.state));
  const notice = NOTICES.get(searchParams.get("notice") ?? "");
  const login = useLogin();
  const {
    register,
    handleSubmit,
    setError,
    formState: { errors },
  } = useForm<LoginValues>({ resolver: zodResolver(loginSchema), defaultValues: { email: "", password: "" } });

  const onSubmit = handleSubmit((values) => {
    if (login.isPending) {
      return;
    }

    login.mutate(values, {
      onError: (error) => {
        for (const [field, message] of serverFieldErrors(error, ["email", "password"] as const)) {
          setError(field, { message });
        }
      },
    });
  });

  return (
    <section aria-labelledby="login-title" className="mx-auto max-w-sm space-y-6">
      <h1 id="login-title" className="text-2xl font-semibold">
        Entrar
      </h1>

      {accountDeleted && (
        <div role="status" className="space-y-1 rounded-md border px-3 py-2 text-sm">
          <p>{DELETION_MESSAGES.done}</p>
          {accountDeleted.receipt && (
            <p>
              Recibo de exclusão: {accountDeleted.receipt.reference}, em {formatDateTime(accountDeleted.receipt.date)}.
            </p>
          )}
        </div>
      )}

      {notice && (
        <p role="status" className="rounded-md border px-3 py-2 text-sm">
          {notice}
        </p>
      )}

      <form noValidate onSubmit={onSubmit} aria-describedby={login.isError ? "login-error" : undefined} className="space-y-4">
        <div className="space-y-2">
          <Label htmlFor="login-email">E-mail</Label>
          <Input
            id="login-email"
            type="email"
            autoComplete="username"
            maxLength={EMAIL_MAX_LENGTH}
            aria-invalid={errors.email ? true : undefined}
            aria-describedby={errors.email ? "login-email-error" : undefined}
            {...register("email")}
          />
          {errors.email && (
            <p id="login-email-error" role="alert" className="text-sm text-destructive">
              {errors.email.message}
            </p>
          )}
        </div>

        <div className="space-y-2">
          <Label htmlFor="login-password">Senha</Label>
          <Input
            id="login-password"
            type="password"
            autoComplete="current-password"
            maxLength={PASSWORD_MAX_LENGTH}
            aria-invalid={errors.password ? true : undefined}
            aria-describedby={errors.password ? "login-password-error" : undefined}
            {...register("password")}
          />
          {errors.password && (
            <p id="login-password-error" role="alert" className="text-sm text-destructive">
              {errors.password.message}
            </p>
          )}
        </div>

        {login.isError && (
          <p id="login-error" role="alert" className="text-sm text-destructive">
            {formErrorMessage(login.error, LOGIN_ERRORS)}
          </p>
        )}

        <Button type="submit" className="w-full" disabled={login.isPending}>
          {login.isPending ? "Entrando…" : "Entrar"}
        </Button>
      </form>

      <p className="text-sm">
        <Link to="/forgot-password" className="underline underline-offset-4">
          Esqueci minha senha
        </Link>
      </p>
    </section>
  );
}
