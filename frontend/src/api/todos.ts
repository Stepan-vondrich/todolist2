import type { TodoItem } from '../types'

const BASE = '/api/todos'

export async function fetchTodos(): Promise<TodoItem[]> {
  const res = await fetch(BASE)
  if (!res.ok) throw new Error('Failed to fetch todos')
  return res.json()
}

export async function createTodo(
  title: string,
  dueDate: string | null,
  status = '',
  parentId: number | null = null,
): Promise<TodoItem> {
  const res = await fetch(BASE, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ title, dueDate, isCompleted: false, status, parentId }),
  })
  if (!res.ok) throw new Error('Failed to create todo')
  return res.json()
}

export async function updateTodo(item: TodoItem): Promise<TodoItem> {
  const res = await fetch(`${BASE}/${item.id}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(item),
  })
  if (!res.ok) throw new Error('Failed to update todo')
  return res.json()
}

export async function deleteTodo(id: number): Promise<void> {
  const res = await fetch(`${BASE}/${id}`, { method: 'DELETE' })
  if (!res.ok) throw new Error('Failed to delete todo')
}

export async function moveTodo(id: number, direction: 'up' | 'down'): Promise<TodoItem[]> {
  const res = await fetch(`${BASE}/${id}/move`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ direction }),
  })
  if (!res.ok) throw new Error('Failed to move todo')
  return res.json()
}

export type DropPosition = 'before' | 'after' | 'inside'

// Drag-and-drop reorder: place `id` relative to `targetId`.
//   before/after → sibling of target; inside → last child of target.
export async function reorderTodo(id: number, targetId: number, position: DropPosition): Promise<TodoItem[]> {
  const res = await fetch(`${BASE}/${id}/reorder`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ targetId, position }),
  })
  if (!res.ok) throw new Error('Failed to reorder todo')
  return res.json()
}
