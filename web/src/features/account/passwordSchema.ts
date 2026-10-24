import { z } from "zod";

// Mirrors the server policy (length over composition, NIST SP 800-63B); the server decides (FE-020).
export const PASSWORD_MIN_LENGTH = 12;
export const PASSWORD_MAX_LENGTH = 128;
export const EMAIL_MAX_LENGTH = 256;

export const emailSchema = z
  .string()
  .trim()
  .min(1, "Informe o e-mail.")
  .max(EMAIL_MAX_LENGTH, "E-mail longo demais.")
  .pipe(z.email("Informe um e-mail válido."));

export const newPasswordSchema = z
  .object({
    password: z
      .string()
      .min(PASSWORD_MIN_LENGTH, `Use pelo menos ${PASSWORD_MIN_LENGTH} caracteres.`)
      .max(PASSWORD_MAX_LENGTH, `Use no máximo ${PASSWORD_MAX_LENGTH} caracteres.`),
    confirmation: z.string(),
  })
  .refine((values) => values.password === values.confirmation, {
    path: ["confirmation"],
    message: "As senhas não conferem.",
  });

export type NewPasswordValues = z.infer<typeof newPasswordSchema>;
