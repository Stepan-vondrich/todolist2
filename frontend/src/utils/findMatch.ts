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
