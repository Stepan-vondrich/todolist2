import type { TodoItem } from '../types'

/**
 * Walk up the parent chain of `id`, returning ancestor ids nearest-first
 * (immediate parent, then grandparent, … up to the root).
 *
 * Used to reveal a hidden todo: expanding every ancestor guarantees the todo
 * is actually rendered before we try to scroll to it.
 *
 * Returns [] if the todo has no parent or isn't found. Guards against malformed
 * parent cycles so it always terminates.
 */
export function ancestorIds(todos: TodoItem[], id: number): number[] {
  const byId = new Map(todos.map(t => [t.id, t]))
  const chain: number[] = []
  const seen = new Set<number>([id])

  let parentId = byId.get(id)?.parentId ?? null
  while (parentId !== null && !seen.has(parentId)) {
    chain.push(parentId)
    seen.add(parentId)
    parentId = byId.get(parentId)?.parentId ?? null
  }
  return chain
}
