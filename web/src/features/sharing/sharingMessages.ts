import type { SharedGraphViewStatus } from "./sharesData";

/** The visible texts of the shared links page that more than the page reads (FE-062). */

/** Every status said in words, never by colour alone (FE-034). */
export const SHARE_STATUS_LABELS: Record<SharedGraphViewStatus, string> = {
  active: "Ativo",
  suspended: "Suspenso: o Node raiz foi excluído ou ocultado",
  disabled_by_platform: "Desativado pela plataforma",
  revoked: "Revogado",
};

/** The last visit is kept as a UTC day only (DA-220), never the moment. */
export function lastAccessText(lastAccessedOn: string | null): string {
  if (lastAccessedOn === null) {
    return "Nunca acessado";
  }

  const [year, month, day] = lastAccessedOn.split("-");
  return `${day}/${month}/${year} (UTC)`;
}
