import { zodResolver } from "@hookform/resolvers/zod";
import { useForm } from "react-hook-form";
import { Link, useSearchParams } from "react-router";
import { z } from "zod";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { useLogin } from "@/features/session/useLogin";
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

const LOGIN_ERRORS = { invalid_credentials: "E-mail ou senha incorretos." };

export function LoginPage() {
  const [searchParams] = useSearchParams();
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
