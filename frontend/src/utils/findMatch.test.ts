import { describe, it, expect } from 'vitest'
import { normalizeForSearch, findMatchIndex } from './findMatch'

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
