import { describe, it, expect } from 'vitest'
import { normalizeForSearch, findMatchIndex, containsIgnoringSpace, findAllMatchIndices, findAllMatchRangesIgnoringSpace } from './findMatch'

describe('normalizeForSearch', () => {
  it('lowercases and strips diacritics', () => {
    expect(normalizeForSearch('Příliš ŽLUŤoučký')).toBe('prilis zlutoucky')
  })
  it('leaves plain ascii unchanged (lowercased)', () => {
    expect(normalizeForSearch('Hello World')).toBe('hello world')
  })
})

describe('findMatchIndex', () => {
  it('returns -1 when the needle is absent', () => {
    expect(findMatchIndex('hello world', 'xyz')).toBe(-1)
  })

  it('finds a plain substring and returns its index in the ORIGINAL string', () => {
    expect(findMatchIndex('hello world', 'world')).toBe(6)
  })

  it('is case-insensitive', () => {
    expect(findMatchIndex('Hello World', 'world')).toBe(6)
  })

  it('is diacritics-insensitive and maps back to the original index', () => {
    // "Praha" sits after "Bydlím v " (9 chars) in the original; query without diacritics.
    const text = 'Bydlím v Praze a Brně'
    const idx = findMatchIndex(text, 'praze')
    expect(idx).toBe(9)
    expect(text.slice(idx, idx + 5)).toBe('Praze')
  })

  it('matches an accented needle against accented text', () => {
    const text = 'Žluťoučký kůň'
    const idx = findMatchIndex(text, 'kůň')
    expect(text.slice(idx, idx + 3)).toBe('kůň')
  })

  it('handles a diacritic before the match without drifting the index', () => {
    // The leading "é" decomposes to two codepoints in NFD; the mapped index must
    // still point at the original (composed) position of "needle".
    const text = 'café — needle here'
    const idx = findMatchIndex(text, 'needle')
    expect(text.slice(idx, idx + 6)).toBe('needle')
  })

  it('returns the first occurrence', () => {
    expect(findMatchIndex('aXaXa', 'x')).toBe(1)
  })
})

describe('containsIgnoringSpace', () => {
  it('matches a plain substring', () => {
    expect(containsIgnoringSpace('recept na celé kuře', 'celé kuře')).toBe(true)
  })

  it('matches across diacritics and case', () => {
    expect(containsIgnoringSpace('RECEPT na CELÉ KUŘE', 'celé kuře')).toBe(true)
  })

  // The real bug: a PDF page renders the phrase with extra whitespace between
  // words (double space, or a line break), so a single-space query missed it.
  it('matches when the haystack has extra spaces between words', () => {
    expect(containsIgnoringSpace('grilované  celé   kuře na pánvi', 'celé kuře')).toBe(true)
  })

  it('matches across a line break in the haystack', () => {
    expect(containsIgnoringSpace('grilované celé\nkuře na pánvi', 'celé kuře')).toBe(true)
  })

  it('matches even when words are fused (no space) in the haystack', () => {
    expect(containsIgnoringSpace('…celékuře…', 'celé kuře')).toBe(true)
  })

  it('matches when the query itself has odd spacing', () => {
    expect(containsIgnoringSpace('a celé kuře b', '  celé   kuře ')).toBe(true)
  })

  it('returns false when the words are genuinely absent', () => {
    expect(containsIgnoringSpace('recept na rybu', 'celé kuře')).toBe(false)
  })

  it('returns false for an empty needle', () => {
    expect(containsIgnoringSpace('anything', '   ')).toBe(false)
  })
})

describe('findAllMatchIndices', () => {
  it('returns [] when the needle is absent', () => {
    expect(findAllMatchIndices('hello world', 'xyz')).toEqual([])
  })

  it('finds every occurrence (non-overlapping), in order', () => {
    // "kuře" at 0, 9, 18
    expect(findAllMatchIndices('kuře a pak kuře a zas kuře', 'kuře')).toEqual([0, 11, 22])
  })

  it('is case- and diacritics-insensitive, mapping back to original indices', () => {
    const text = 'Praha, praha a PRAHA'
    const idxs = findAllMatchIndices(text, 'praha')
    expect(idxs).toEqual([0, 7, 15])
    for (const i of idxs) expect(text.slice(i, i + 5).toLowerCase()).toContain('raha')
  })

  it('does not overlap matches', () => {
    // "aa" in "aaaa" → positions 0 and 2, not 0,1,2
    expect(findAllMatchIndices('aaaa', 'aa')).toEqual([0, 2])
  })

  it('returns [] for an empty needle', () => {
    expect(findAllMatchIndices('anything', '')).toEqual([])
  })
})

describe('findAllMatchRangesIgnoringSpace', () => {
  it('returns [] when absent', () => {
    expect(findAllMatchRangesIgnoringSpace('hello world', 'xyz')).toEqual([])
  })

  it('returns a {start,end} range covering a single-word match', () => {
    const text = 'recept na kuře dnes'
    const ranges = findAllMatchRangesIgnoringSpace(text, 'kuře')
    expect(ranges).toEqual([{ start: 10, end: 14 }])
    expect(text.slice(ranges[0].start, ranges[0].end)).toBe('kuře')
  })

  it('matches a multi-word phrase, range spans from first to last word', () => {
    const text = 'přidej lžíce oliv navíc'
    const ranges = findAllMatchRangesIgnoringSpace(text, 'lžíce oliv')
    expect(ranges.length).toBe(1)
    expect(text.slice(ranges[0].start, ranges[0].end)).toBe('lžíce oliv')
  })

  it('matches a phrase even with extra spaces in the haystack', () => {
    const text = 'dej lžíce   oliv sem'
    const ranges = findAllMatchRangesIgnoringSpace(text, 'lžíce oliv')
    expect(ranges.length).toBe(1)
    expect(text.slice(ranges[0].start, ranges[0].end)).toBe('lžíce   oliv')
  })

  it('finds multiple occurrences', () => {
    const text = 'celé kuře a pak celé kuře'
    const ranges = findAllMatchRangesIgnoringSpace(text, 'celé kuře')
    expect(ranges.length).toBe(2)
    expect(text.slice(ranges[0].start, ranges[0].end)).toBe('celé kuře')
    expect(text.slice(ranges[1].start, ranges[1].end)).toBe('celé kuře')
  })

  it('returns [] for an empty needle', () => {
    expect(findAllMatchRangesIgnoringSpace('anything', '   ')).toEqual([])
  })
})
