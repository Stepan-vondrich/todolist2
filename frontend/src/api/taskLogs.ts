import type { TaskLog } from '../types'

export async function fetchLogs(todoId: number): Promise<TaskLog[]> {
  const res = await fetch(`/api/task-logs?todoId=${todoId}`)
  if (!res.ok) throw new Error('Failed to fetch logs')
  return res.json()
}
