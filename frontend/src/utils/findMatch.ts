// Diacritics- and case-insensitive text search that can map a match position in
// the normalized text back to an index in the ORIGINAL string. Used to jump a
// document viewer to where a search hit actually appears.
//
// Mirrors the backend's Normalize(): NFD-decompose, drop combining marks, lowercase.

/** Normalize a whole string for comparison (NFD, strip marks, lowercase). */
export function normalizeForSearch(s: string): string {
  return s.normalize('NFD').replace(/\p{Mn}/gu, '').toLowerCase()
}

/**
 * Find `needle` inside `haystack`, ignoring case and diacritics, and return the
 * index in the ORIGINAL `haystack` where the match starts (or -1 if absent).
 *
 * We normalize character-by-character and remember which original index each
 * normalized character came from, so the normalized match index can be mapped
 * back even though diacritics change string length under NFD.
 */
export function findMatchIndex(haystack: string, needle: string): number {
  const normNeedle = normalizeForSearch(needle)
  if (!normNeedle) return -1

  let normalized = ''
  // originIndex[i] = index in `haystack` that produced normalized char i.
  const originIndex: number[] = []

  for (let i = 0; i < haystack.length; i++) {
    const normChar = normalizeForSearch(haystack[i])
    // A single original char can normalize to 0 chars (a bare combining mark) or
    // several; map each resulting char back to this original position.
    for (let k = 0; k < normChar.length; k++) {
      normalized += normChar[k]
      originIndex.push(i)
    }
  }

  const hit = normalized.indexOf(normNeedle)
  if (hit < 0) return -1
  return originIndex[hit]
}

/**
 * Like findMatchIndex but returns the original-string start index of EVERY
 * (non-overlapping) occurrence, in order. Used to highlight all matches in a
 * document and let the user step between them.
 */
export function findAllMatchIndices(haystack: string, needle: string): number[] {
  const normNeedle = normalizeForSearch(needle)
  if (!normNeedle) return []

  let normalized = ''
  const originIndex: number[] = []
  for (let i = 0; i < haystack.length; i++) {
    const normChar = normalizeForSearch(haystack[i])
    for (let k = 0; k < normChar.length; k++) {
      normalized += normChar[k]
      originIndex.push(i)
    }
  }

  const out: number[] = []
  let from = 0
  for (;;) {
    const hit = normalized.indexOf(normNeedle, from)
    if (hit < 0) break
    out.push(originIndex[hit])
    from = hit + normNeedle.length // non-overlapping
  }
  return out
}

/**
 * Whitespace-insensitive "does haystack contain needle?" (also case/diacritics
 * insensitive). The same phrase can carry different whitespace in different text
 * sources — e.g. a PDF page rendered via pdf.js may put a double space or a line
 * break between words where the indexed text had a single space, or fuse words
 * with no space at all. Dropping all whitespace from both sides makes the match
 * robust. Used to pick which PDF page a multi-word search hit lives on.
 */
export function containsIgnoringSpace(haystack: string, needle: string): boolean {
  const strip = (s: string) => normalizeForSearch(s).replace(/\s+/g, '')
  const n = strip(needle)
  if (!n) return false
  return strip(haystack).includes(n)
}

/**
 * Find every occurrence of `needle` in `haystack` ignoring case, diacritics AND
 * whitespace, returning {start,end} ranges into the ORIGINAL string (end is one
 * past the last matched char). A multi-word phrase therefore matches even when
 * the words are split by line breaks or extra spaces — the returned range spans
 * from the first matched word to the last. Used to highlight phrases that cross
 * PDF text-layer span boundaries.
 */
export function findAllMatchRangesIgnoringSpace(
  haystack: string, needle: string,
): { start: number; end: number }[] {
  const normNeedle = normalizeForSearch(needle).replace(/\s+/g, '')
  if (!normNeedle) return []

  // Build the space-stripped normalized text, remembering each kept char's
  // original index so a match position maps back to the source string.
  let stripped = ''
  const originIndex: number[] = []
  for (let i = 0; i < haystack.length; i++) {
    const normChar = normalizeForSearch(haystack[i])
    for (const ch of normChar) {
      if (/\s/.test(ch)) continue
      stripped += ch
      originIndex.push(i)
    }
  }

  const out: { start: number; end: number }[] = []
  let from = 0
  for (;;) {
    const hit = stripped.indexOf(normNeedle, from)
    if (hit < 0) break
    const start = originIndex[hit]
    const end = originIndex[hit + normNeedle.length - 1] + 1
    out.push({ start, end })
    from = hit + normNeedle.length // non-overlapping
  }
  return out
}
