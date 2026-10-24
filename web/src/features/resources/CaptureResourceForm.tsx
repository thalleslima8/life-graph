import { zodResolver } from "@hookform/resolvers/zod";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { Link } from "react-router";
import { z } from "zod";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  CAPTURE_LINK_LABEL,
  CAPTURE_LINK_PLACEHOLDER,
  CAPTURE_OPEN_LABEL,
  CAPTURE_OUTCOME_MESSAGES,
  CAPTURE_PENDING_LABEL,
  CAPTURE_SUBMIT_LABEL,
  captureErrorMessage,
} from "@/features/graph/graphMessages";
import { RESOURCE_URL_MAX_LENGTH, captureUrlSchema, outcomeOf, useCaptureResource, type CaptureOutcome } from "./resourceCapture";

const captureSchema = z.object({ url: captureUrlSchema });

type CaptureValues = z.infer<typeof captureSchema>;

type CaptureStatus = { outcome: CaptureOutcome; nodeId: string | null | undefined };

/**
 * Pastes a link into the Inbox (DA-062, DA-157). Only on the Inbox, also when it is empty. The
 * page stays put after each capture so several links go in one after the other.
 */
export function CaptureResourceForm({ inInbox }: { inInbox: boolean }) {
  const capture = useCaptureResource();
  const [status, setStatus] = useState<CaptureStatus | null>(null);
  const {
    register,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<CaptureValues>({ resolver: zodResolver(captureSchema), defaultValues: { url: "" } });

  if (!inInbox) {
    return null;
  }

  const onSubmit = handleSubmit((values) => {
    if (capture.isPending) {
      return;
    }

    setStatus(null);
    capture.mutate(
      { url: values.url },
      {
        onSuccess: (receipt) => {
          reset();
          setStatus({ outcome: outcomeOf(receipt), nodeId: receipt.nodeId });
        },
      },
    );
  });

  const describedBy = [errors.url && "capture-url-error", capture.isError && "capture-error"].filter(Boolean).join(" ") || undefined;
  const invalid = errors.url !== undefined || capture.error?.status === 422;

  return (
    <form noValidate onSubmit={onSubmit} className="space-y-2" aria-label="Capturar link">
      <Label htmlFor="capture-url">{CAPTURE_LINK_LABEL}</Label>
      <div className="flex gap-2">
        <Input
          id="capture-url"
          type="url"
          inputMode="url"
          autoComplete="off"
          maxLength={RESOURCE_URL_MAX_LENGTH}
          placeholder={CAPTURE_LINK_PLACEHOLDER}
          aria-invalid={invalid ? true : undefined}
          aria-describedby={describedBy}
          {...register("url")}
        />
        <Button type="submit" disabled={capture.isPending}>
          {capture.isPending ? CAPTURE_PENDING_LABEL : CAPTURE_SUBMIT_LABEL}
        </Button>
      </div>
      {errors.url && (
        <p id="capture-url-error" role="alert" className="text-sm text-destructive">
          {errors.url.message}
        </p>
      )}
      {capture.isError && (
        <p id="capture-error" role="alert" className="text-sm text-destructive">
          {captureErrorMessage(capture.error)}
        </p>
      )}
      {status && (
        <p role="status" className="text-sm text-muted-foreground">
          {CAPTURE_OUTCOME_MESSAGES[status.outcome]}{" "}
          {status.nodeId && (
            <Link to={`/nodes/${status.nodeId}`} className="underline underline-offset-4">
              {CAPTURE_OPEN_LABEL}
            </Link>
          )}
        </p>
      )}
    </form>
  );
}
