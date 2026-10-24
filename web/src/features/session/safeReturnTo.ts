const FALLBACK = "/";

/** Only a path on this site: "/…", never "//host" or "/\host" (which browsers treat as another origin). */
export function safeReturnTo(raw: string | null | undefined): string {
  if (!raw || !raw.startsWith("/") || raw.startsWith("//") || raw.startsWith("/\\")) {
    return FALLBACK;
  }

  return raw;
}
