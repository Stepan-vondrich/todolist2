import { describe, it, expect } from 'vitest'
import { serializeFilters, deserializeFilters } from './filterStorage'
import type { FilterState } from '../types'

function defaults(): FilterState {
  return {
    nameFilter: '',
    listFilter: new Set<number>(),
    statusFilter: new Set(['', 'in-process', 'on_hold', 'done', 'failed']),
    prioritaExcluded: new Set<string>(),
    relatedFilter: '',
    detailRelatedFilter: '',
    dateFrom: '',
    dateTo: '',
    activityFrom: '',
    activityTo: '',
    activityTypes: new Set(['created', 'modified', 'commented']),
  }
}

describe('filterStorage', () => {
  it('round-trips a filter state, preserving Set fields as Sets', () => {
    const f = defaults()
    f.nameFilter = 'krém'
    f.listFilter = new Set([3, 7])
    f.statusFilter = new Set(['done'])
    f.prioritaExcluded = new Set(['1'])
    f.dateFrom = '2026-01-01'

    const restored = deserializeFilters(serializeFilters(f), defaults())

    expect(restored.nameFilter).toBe('krém')
    expect(restored.dateFrom).toBe('2026-01-01')
    expect(restored.listFilter).toBeInstanceOf(Set)
    expect([...restored.listFilter].sort()).toEqual([3, 7])
    expect([...restored.statusFilter]).toEqual(['done'])
    expect([...restored.prioritaExcluded]).toEqual(['1'])
  })

  it('returns a fresh copy of the defaults when raw is null', () => {
    const d = defaults()
    const restored = deserializeFilters(null, d)
    expect(restored).toEqual(d)
    // must not share Set references with the passed-in defaults (mutation safety)
    expect(restored.statusFilter).not.toBe(d.statusFilter)
  })

  it('returns the defaults when raw is malformed JSON', () => {
    const d = defaults()
    expect(deserializeFilters('{not json', d)).toEqual(d)
  })

  it('fills keys missing from an older stored blob using the defaults', () => {
    // A blob saved before activityTypes existed — only a couple of fields present.
    const raw = JSON.stringify({ nameFilter: 'x', listFilter: [5] })
    const restored = deserializeFilters(raw, defaults())

    expect(restored.nameFilter).toBe('x')
    expect([...restored.listFilter]).toEqual([5])
    // missing Set field falls back to the default set, still a Set
    expect(restored.activityTypes).toBeInstanceOf(Set)
    expect([...restored.activityTypes]).toEqual(['created', 'modified', 'commented'])
    // missing string field falls back to default
    expect(restored.statusFilter).toBeInstanceOf(Set)
  })

  it('does not mutate the defaults when the stored blob omits a Set field', () => {
    const d = defaults()
    const restored = deserializeFilters(JSON.stringify({ nameFilter: 'x' }), d)
    // restored gets its own Set, defaults stay intact
    restored.statusFilter.add('__probe__')
    expect(d.statusFilter.has('__probe__')).toBe(false)
  })
})
