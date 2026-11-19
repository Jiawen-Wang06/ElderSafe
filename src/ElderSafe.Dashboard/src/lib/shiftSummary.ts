/** A piece of summary text: plain words, or a citation of one stored alert. */
export type SummarySegment =
  | { kind: "text"; text: string }
  | { kind: "citation"; ref: string };

/**
 * Splits a shift summary into plain text and its `[A1]`-style citations, so
 * each citation can be shown as a link to the stored alert it names
 * (REQ-AI-01).
 *
 * A bare `A1` counts too. The model sometimes drops the brackets, and the
 * server's validator already treats both forms as a citation.
 *
 * Only references that belong to this summary's alerts become citations.
 * The server has already rejected any summary citing an unknown one, so an
 * unknown reference reaching here would mean the two disagree, and it is
 * left as plain text rather than linked to nothing.
 */
export function splitCitations(text: string, knownRefs: readonly string[]): SummarySegment[] {
  const known = new Set(knownRefs);
  const segments: SummarySegment[] = [];
  const pattern = /\[(A\d+)\]|\b(A\d+)\b/g;
  let last = 0;

  for (const match of text.matchAll(pattern)) {
    const ref = match[1] ?? match[2];
    const start = match.index;

    if (!known.has(ref)) {
      continue;
    }

    if (start > last) {
      segments.push({ kind: "text", text: text.slice(last, start) });
    }

    segments.push({ kind: "citation", ref });
    last = start + match[0].length;
  }

  if (last < text.length) {
    segments.push({ kind: "text", text: text.slice(last) });
  }

  return segments;
}

/** The browser's IANA time zone, so summary times read as the caregiver's clock. */
export function browserTimeZone(): string {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone || "UTC";
  } catch {
    return "UTC";
  }
}
