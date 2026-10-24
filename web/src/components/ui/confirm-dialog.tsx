import type { ReactNode } from "react";
import { Button } from "./button";
import { Dialog, DialogClose, DialogContent } from "./dialog";

type ConfirmDialogProps = {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: ReactNode;
  description: ReactNode;
  confirmLabel: string;
  /** The confirmed action is running: the confirm button waits. */
  pending: boolean;
  /** Why the confirmed action failed, shown inside the dialog. */
  error?: ReactNode;
  onConfirm: () => void;
};

/** Asks before an action the person should not take by accident; the caller runs it and closes the dialog. */
export function ConfirmDialog({ open, onOpenChange, title, description, confirmLabel, pending, error, onConfirm }: ConfirmDialogProps) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent title={title} description={description}>
        {error !== undefined && error !== null && (
          <p role="alert" className="text-sm text-destructive">
            {error}
          </p>
        )}
        <div className="flex justify-end gap-2">
          <DialogClose asChild>
            <Button type="button" variant="ghost">
              Cancelar
            </Button>
          </DialogClose>
          <Button type="button" onClick={onConfirm} disabled={pending}>
            {confirmLabel}
          </Button>
        </div>
      </DialogContent>
    </Dialog>
  );
}
