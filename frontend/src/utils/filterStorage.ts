import type { FilterState } from '../types'

// Which FilterState fields are Sets — stored as arrays in JSON, rebuilt as Sets on load.
const SET_KEYS = ['listFilter', 'statusFilter', 'prioritaExcluded', 'activityTypes'] as const

/**
 * Serialize the filter state to a JSON string for localStorage. Set fields are
 * written as plain arrays (JSON can't represent a Set); everything else is copied
 * as-is. Pairs with `deserializeFilters`.
 */
export function serializeFilters(filters: FilterState): string {
  const plain: Record<string, unknown> = {}
  for (const [key, value] of Object.entries(filters)) {
    plain[key] = value instanceof Set ? [...value] : value
  }
  return JSON.stringify(plain)
}

/**
 * Rebuild a FilterState from a localStorage string, layered on top of `defaults`
 * so the result is always complete and self-consistent:
 *  - `raw` null or malformed  → a fresh copy of the defaults
 *  - keys missing from `raw`   → filled from the defaults
 *  - Set-typed keys            → arrays turned back into real Sets
 * The returned object never shares Set references with `defaults`, so mutating it
 * (add/delete) can't corrupt the caller's default object.
 */
export function deserializeFilters(raw: string | null, defaults: FilterState): FilterState {
  // Start from a deep-enough copy of the defaults (fresh Sets for the Set fields).
  const result: FilterState = { ...defaults }
  for (const key of SET_KEYS) {
    result[key] = new Set(defaults[key] as Set<never>) as never
  }
  if (!raw) return result

  let parsed: Record<string, unknown>
  try {
    parsed = JSON.parse(raw)
  } catch {
    return result
  }
  if (!parsed || typeof parsed !== 'object') return result

  for (const key of Object.keys(defaults) as (keyof FilterState)[]) {
    if (!(key in parsed)) continue
    const value = parsed[key]
    if ((SET_KEYS as readonly string[]).includes(key)) {
      if (Array.isArray(value)) result[key] = new Set(value) as never
    } else {
      result[key] = value as never
    }
  }
  return result
}
