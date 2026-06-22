import type { Comment } from '../types'

const BASE = '/api/comments'

export async function fetchComments(todoId: number): Promise<Comment[]> {
  const res = await fetch(`${BASE}?todoId=${todoId}`)
  if (!res.ok) throw new Error('Failed to fetch comments')
  return res.json()
}

export async function fetchCommentCounts(): Promise<Record<number, number>> {
  const res = await fetch(`${BASE}/counts`)
  if (!res.ok) throw new Error('Failed to fetch comment counts')
  return res.json()
}

export async function updateComment(id: number, text: string): Promise<Comment> {
  const res = await fetch(`${BASE}/${id}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ text }),
  })
  if (!res.ok) throw new Error('Failed to update comment')
  return res.json()
}

export async function deleteComment(id: number): Promise<void> {
  const res = await fetch(`${BASE}/${id}`, { method: 'DELETE' })
  if (!res.ok) throw new Error('Failed to delete comment')
}

export async function createComment(
  todoId: number,
  text: string,
  files: File[] = [],
  previews: (File | undefined)[] = [],
): Promise<Comment> {
  const form = new FormData()
  form.append('todoId', String(todoId))
  form.append('text', text)
  files.forEach((file, i) => {
    form.append(`file_${i}`, file)
    const preview = previews[i]
    if (preview) form.append(`preview_${i}`, preview)
  })

  const res = await fetch(BASE, { method: 'POST', body: form })
  if (!res.ok) throw new Error('Failed to create comment')
  return res.json()
}
