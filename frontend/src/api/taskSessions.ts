import type { TaskSession } from '../types'

const BASE = '/api/task-sessions'

export async function fetchSessions(todoId: number): Promise<TaskSession[]> {
  const res = await fetch(`${BASE}?todoId=${todoId}`)
  if (!res.ok) throw new Error('Failed to fetch sessions')
  return res.json()
}

export async function fetchActiveTodoIds(): Promise<number[]> {
  const res = await fetch(`${BASE}/active`)
  if (!res.ok) throw new Error('Failed to fetch active sessions')
  return res.json()
}

export async function startSession(todoId: number): Promise<TaskSession> {
  const res = await fetch(`${BASE}/start/${todoId}`, { method: 'POST' })
  if (!res.ok) throw new Error('Failed to start session')
  return res.json()
}

export async function deleteSession(id: number): Promise<void> {
  const res = await fetch(`${BASE}/${id}`, { method: 'DELETE' })
  if (!res.ok) throw new Error('Failed to delete session')
}

export async function updateSession(
  id: number,
  data: { startedAt: string; endedAt: string | null; comment: string | null },
): Promise<TaskSession> {
  const res = await fetch(`${BASE}/${id}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(data),
  })
  if (!res.ok) throw new Error('Failed to update session')
  return res.json()
}

export async function endSession(todoId: number, comment?: string): Promise<TaskSession> {
  const res = await fetch(`${BASE}/end/${todoId}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ comment: comment ?? null }),
  })
  if (!res.ok) throw new Error('Failed to end session')
  return res.json()
}
