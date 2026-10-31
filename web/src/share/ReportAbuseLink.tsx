import { REPORT_ABUSE_LABEL, reportAbuseSubject } from "./visitorMessages";

/** The meta of visitor.html the Host fills with `Sharing:AbuseReportAddress` (DA-218). */
const ABUSE_REPORT_META = 'meta[name="lifegraph-abuse-report"]';

/** The operator's abuse address the page was served with; empty outside the Host (the Vite dev server). */
function abuseReportAddress(): string {
  return (
    document
      .querySelector<HTMLMetaElement>(ABUSE_REPORT_META)
      ?.content.trim() ?? ""
  );
}

/**
 * "Denunciar esta página" (DA-218): a `mailto:` to the operator naming the link by its public
 * reference, the one the operator's kill-switch takes (DA-217). Never the token, which this
 * component is never given. Without a reference yet (loading, or a link that does not open) the
 * report goes without one.
 */
export function ReportAbuseLink({
  publicRef,
}: {
  publicRef: string | undefined;
}) {
  const address = abuseReportAddress();
  if (address === "") {
    return null;
  }

  const subject = encodeURIComponent(reportAbuseSubject(publicRef));
  return (
    <a
      href={`mailto:${address}?subject=${subject}`}
      className="text-sm underline underline-offset-4"
    >
      {REPORT_ABUSE_LABEL}
    </a>
  );
}
