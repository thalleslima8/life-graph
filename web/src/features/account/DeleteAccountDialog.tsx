import { useId, useRef, useState, type FormEvent } from "react";
import { isApiError } from "@/api/client";
import { Button } from "@/components/ui/button";
import { Dialog, DialogClose, DialogContent } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { accountErrorCodes, useDeleteAccount } from "./accountData";
import { DELETION_MESSAGES } from "./accountMessages";
import { formErrorMessage } from "./messages";
import { EMAIL_MAX_LENGTH, PASSWORD_MAX_LENGTH } from "./passwordSchema";

/** The deletion's own refusals, each shown under the field that fixes it (FE-021). */
function fieldErrorOf(error: unknown, code: string, message: string): string | undefined {
  return isApiError(error, code) ? message : undefined;
}

/** The server compares the same way (DA-251); here it only unlocks the button (FE-020). */
function confirms(typed: string, email: string): boolean {
  return typed.trim().toLowerCase() === email.trim().toLowerCase();
}

interface DeleteAccountDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** The Account's e-mail: what the person types to confirm. */
  email: string;
  /** Offers the export before the deletion; the deletion never requires it (DA-082). */
  onExportFirst?: () => void;
}

/**
 * Deletes the Account (DA-082, DA-255): the irreversibility and the external agents' warning
 * up front, the password typed again and the e-mail typed as confirmation. The password lives
 * only in the field and goes away when the dialog closes.
 */
export function DeleteAccountDialog({ open, onOpenChange, email, onExportFirst }: DeleteAccountDialogProps) {
  const deletion = useDeleteAccount();
  const [confirmation, setConfirmation] = useState("");
  const passwordId = useId();
  const confirmationId = useId();
  const hintId = useId();
  const passwordErrorId = useId();
  const confirmationErrorId = useId();
  const formErrorId = useId();
  // Opened without a Radix trigger, so focus goes back by hand to whatever opened it (FE-032).
  const opener = useRef<HTMLElement | null>(null);

  const passwordError = fieldErrorOf(
    deletion.error,
    accountErrorCodes.reauthenticationFailed,
    DELETION_MESSAGES.wrongPassword,
  );
  const confirmationError = fieldErrorOf(
    deletion.error,
    accountErrorCodes.deletionConfirmationMismatch,
    DELETION_MESSAGES.confirmationMismatch,
  );
  // 429, 5xx and network failures: no field explains them.
  const formError =
    deletion.isError && passwordError === undefined && confirmationError === undefined
      ? formErrorMessage(deletion.error)
      : undefined;

  const changeOpen = (next: boolean) => {
    if (!next) {
      setConfirmation("");
      deletion.reset();
    }
    onOpenChange(next);
  };

  const onSubmit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (deletion.isPending || !confirms(confirmation, email)) {
      return;
    }

    const password = new FormData(event.currentTarget).get("password");
    deletion.mutate({ password: typeof password === "string" ? password : "", confirmation });
  };

  return (
    <Dialog open={open} onOpenChange={changeOpen}>
      <DialogContent
        title="Excluir conta"
        description={`${DELETION_MESSAGES.irreversible} ${DELETION_MESSAGES.externalAgents}`}
        onOpenAutoFocus={() => {
          opener.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
        }}
        onCloseAutoFocus={(event) => {
          if (opener.current?.isConnected) {
            event.preventDefault();
            opener.current.focus();
          }
        }}
      >
        {onExportFirst && (
          <div className="space-y-2 rounded-md border p-3">
            <p className="text-sm">{DELETION_MESSAGES.exportFirst}</p>
            <Button type="button" variant="secondary" size="sm" onClick={onExportFirst}>
              Exportar antes
            </Button>
          </div>
        )}

        <form
          noValidate
          onSubmit={onSubmit}
          aria-describedby={formError === undefined ? undefined : formErrorId}
          className="space-y-4"
        >
          <div className="space-y-2">
            <Label htmlFor={passwordId}>Senha</Label>
            <Input
              id={passwordId}
              name="password"
              type="password"
              autoComplete="current-password"
              required
              maxLength={PASSWORD_MAX_LENGTH}
              aria-invalid={passwordError === undefined ? undefined : true}
              aria-describedby={passwordError === undefined ? undefined : passwordErrorId}
            />
            {passwordError !== undefined && (
              <p id={passwordErrorId} role="alert" className="text-sm text-destructive">
                {passwordError}
              </p>
            )}
          </div>

          <div className="space-y-2">
            <Label htmlFor={confirmationId}>E-mail da conta</Label>
            <Input
              id={confirmationId}
              name="confirmation"
              type="email"
              autoComplete="off"
              required
              maxLength={EMAIL_MAX_LENGTH}
              aria-invalid={confirmationError === undefined ? undefined : true}
              aria-describedby={confirmationError === undefined ? hintId : `${confirmationErrorId} ${hintId}`}
              value={confirmation}
              onChange={(event) => setConfirmation(event.target.value)}
            />
            <p id={hintId} className="text-sm text-muted-foreground">
              {DELETION_MESSAGES.confirmationHint(email)}
            </p>
            {confirmationError !== undefined && (
              <p id={confirmationErrorId} role="alert" className="text-sm text-destructive">
                {confirmationError}
              </p>
            )}
          </div>

          {formError !== undefined && (
            <p id={formErrorId} role="alert" className="text-sm text-destructive">
              {formError}
            </p>
          )}

          <div className="flex flex-wrap justify-end gap-3">
            <DialogClose asChild>
              <Button type="button" variant="secondary">
                Cancelar
              </Button>
            </DialogClose>
            <Button type="submit" disabled={deletion.isPending || !confirms(confirmation, email)}>
              {deletion.isPending ? "Excluindo…" : "Excluir conta para sempre"}
            </Button>
          </div>
        </form>
      </DialogContent>
    </Dialog>
  );
}
