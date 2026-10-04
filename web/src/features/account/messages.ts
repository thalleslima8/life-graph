import { isApiError } from "@/api/client";
import { PASSWORD_MIN_LENGTH } from "./passwordSchema";

export const TOO_MANY_ATTEMPTS_CODE = "too_many_attempts";
export const INVALID_OR_EXPIRED_TOKEN_CODE = "invalid_or_expired_token";
export const PASSWORD_REJECTED_CODE = "password_rejected";

// Generic on purpose: the same text whether or not the e-mail has an account (DA-097).
export const TOO_MANY_ATTEMPTS_MESSAGE = "Muitas tentativas. Aguarde alguns minutos e tente novamente.";
export const UNEXPECTED_ERROR_MESSAGE = "Não foi possível concluir agora. Tente novamente em instantes.";
export const PASSWORD_REJECTED_MESSAGE = `Essa senha não atende à política. Use pelo menos ${PASSWORD_MIN_LENGTH} caracteres.`;
export const NEW_PASSWORD_HINT = `Pelo menos ${PASSWORD_MIN_LENGTH} caracteres. Uma frase longa é mais forte que símbolos.`;

/** The form-level message for an error no field explains. */
export function formErrorMessage(error: unknown, messagesByCode: Readonly<Record<string, string>> = {}): string {
  if (isApiError(error, TOO_MANY_ATTEMPTS_CODE)) {
    return TOO_MANY_ATTEMPTS_MESSAGE;
  }

  if (isApiError(error) && error.code !== undefined && Object.hasOwn(messagesByCode, error.code)) {
    return messagesByCode[error.code] ?? UNEXPECTED_ERROR_MESSAGE;
  }

  return UNEXPECTED_ERROR_MESSAGE;
}

/** Server field errors (validation_failed) for the fields a form shows, keyed like the form. */
export function serverFieldErrors<TField extends string>(error: unknown, fields: readonly TField[]): [TField, string][] {
  if (!isApiError(error)) {
    return [];
  }

  return fields.flatMap((field) => {
    const message = error.fieldErrors[field]?.[0];
    return message ? [[field, message] as [TField, string]] : [];
  });
}
