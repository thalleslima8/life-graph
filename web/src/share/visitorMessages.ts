/** The visible texts of a visitor's page (FE-062). */

export const VISITOR_LOADING_MESSAGE = "Carregando…";
/** Every failure of a link reads the same: invalid, revoked, suspended or gone (DA-079). */
export const LINK_UNAVAILABLE_TITLE = "Link indisponível";
export const LINK_UNAVAILABLE_MESSAGE =
  "Este link não está disponível. Ele pode ter sido revogado ou nunca ter existido.";
export const VISITOR_VIEW_MODE_LEGEND = "Visualização";
export const VISITOR_CANVAS_LABEL = "Desenho";
export const VISITOR_LIST_LABEL = "Lista";
export const VISITOR_DRAW_ERROR_MESSAGE =
  "Não foi possível desenhar o grafo; mostrando a lista.";
export const VISITOR_ZOOM_CONTROLS_LABEL = "Zoom";
export const VISITOR_ZOOM_IN_LABEL = "Aproximar";
export const VISITOR_ZOOM_OUT_LABEL = "Afastar";
export const VISITOR_ZOOM_RESET_LABEL = "Restaurar zoom";
export const VISITOR_INSPECTOR_CLOSE_LABEL = "Fechar";
export const VISITOR_NO_TYPE_LABEL = "Sem Type";
export const VISITOR_BODY_LABEL = "Conteúdo";
export const VISITOR_RESOURCE_LABEL = "Link do Resource";
export const VISITOR_PROPERTIES_LABEL = "Propriedades";
export const VISITOR_TAGS_LABEL = "Tags";
export const VISITOR_RELATIONS_LABEL = "Relations";
export const VISITOR_INSPECTOR_LABEL = "Inspector";
export const PROPERTY_TRUE_TEXT = "Sim";
export const PROPERTY_FALSE_TEXT = "Não";

/** The accessible name of the shared graph's drawing or list. */
export function sharedGraphLabel(rootTitle: string, nodeCount: number): string {
  return `Grafo compartilhado de ${rootTitle}, ${nodeCount === 1 ? "1 Node" : `${nodeCount} Nodes`}`;
}

export const REPORT_ABUSE_LABEL = "Denunciar esta página";

/** The report's subject: the link's public reference when there is one, never its token (DA-218). */
export function reportAbuseSubject(publicRef: string | undefined): string {
  return publicRef === undefined
    ? "Denúncia de página compartilhada"
    : `Denúncia de página compartilhada ${publicRef}`;
}
