import { describe, it, expect } from 'vitest'
import { ancestorIds } from './ancestorIds'
import type { TodoItem } from '../types'

function t(id: number, parentId: number | null): TodoItem {
  return {
    id, title: `t${id}`, isCompleted: false, status: '', dueDate: null,
    createdAt: '2026-01-01T00:00:00Z', parentId, priority: '', related: '', detailRelated: '',
  }
}

describe('ancestorIds', () => {
  it('returns an empty array for a root todo (no parent)', () => {
    const todos = [t(1, null)]
    expect(ancestorIds(todos, 1)).toEqual([])
  })

  it('returns the single parent for a direct child', () => {
    const todos = [t(1, null), t(2, 1)]
    expect(ancestorIds(todos, 2)).toEqual([1])
  })

  it('returns the full chain from nearest parent up to the root', () => {
    // 1 → 2 → 3 (3 is the deepest). Ancestors of 3 are [2, 1].
    const todos = [t(1, null), t(2, 1), t(3, 2)]
    expect(ancestorIds(todos, 3)).toEqual([2, 1])
  })

  it('returns an empty array when the id is not found', () => {
    const todos = [t(1, null)]
    expect(ancestorIds(todos, 999)).toEqual([])
  })

  it('does not loop forever if data contains a parent cycle', () => {
    // Malformed data: 1 → 2 → 1. Should terminate, not hang.
    const todos = [t(1, 2), t(2, 1)]
    const result = ancestorIds(todos, 2)
    // Visits 1, then would revisit 2 (the start) — stops there.
    expect(result).toEqual([1])
  })
})
