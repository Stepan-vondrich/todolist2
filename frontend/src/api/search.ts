export interface SearchMatch {
  source: 'title' | 'related' | 'detailRelated' | 'priority' | 'comment' | 'attachment'
  text: string
  commentId?: number | null
  fileName?: string | null      // attachment matches: a short type hint, e.g. "DOCX"
  displayName?: string | null   // attachment matches: original file name to show as a heading
  attachmentPath?: string | null // attachment matches: served URL of the file (/uploads/…)
  pageNumber?: number | null    // attachment matches (PDF): 1-based page of the first hit
}

export interface SearchResult {
  todoId: number
  todoTitle: string
  parentId?: number | null
  parentTitle?: string | null
  matches: SearchMatch[]
}

export async function search(q: string): Promise<SearchResult[]> {
  const res = await fetch(`/api/search?q=${encodeURIComponent(q)}`)
  if (!res.ok) throw new Error('Search failed')
  return res.json()
}
