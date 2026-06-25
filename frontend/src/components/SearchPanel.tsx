import { useEffect, useRef, useState } from 'react'
import { search, type SearchResult } from '../api/search'

interface Props {
  onClose: () => void
  onOpenComments: (id: number) => void
  // Expand ancestors / clear filters so the todo is rendered, then scroll + flash it.
  onReveal: (id: number) => void
  // Open the comments panel for a todo AND jump its viewer to where `query`
  // matches inside the given attachment file (optionally on a known PDF page).
  onOpenAttachment: (todoId: number, attachmentPath: string, query: string, page?: number | null) => void
}

const SOURCE_LABEL: Record<string, string> = {
  related: 'Related',
  detailRelated: 'Detail related',
  priority: 'Priorita',
}

function truncate(text: string | null | undefined, max = 220): string {
  if (!text) return ''
  return text.length <= max ? text : text.slice(0, max) + '…'
}

export default function SearchPanel({ onClose, onOpenComments, onReveal, onOpenAttachment }: Props) {
  const [query, setQuery] = useState('')
  const [results, setResults] = useState<SearchResult[]>([])
  const [loading, setLoading] = useState(false)
  const inputRef = useRef<HTMLInputElement>(null)
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null)

  useEffect(() => { inputRef.current?.focus() }, [])

  useEffect(() => {
    if (timerRef.current) clearTimeout(timerRef.current)
    const q = query.trim()
    if (q.length < 2) { setResults([]); setLoading(false); return }
    setLoading(true)
    timerRef.current = setTimeout(async () => {
      try {
        const r = await search(q)
        setResults(r)
      } catch {
        setResults([])
      } finally {
        setLoading(false)
      }
    }, 300)
    return () => { if (timerRef.current) clearTimeout(timerRef.current) }
  }, [query])

  useEffect(() => {
    const handler = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose() }
    document.addEventListener('keydown', handler)
    return () => document.removeEventListener('keydown', handler)
  }, [onClose])

  function handleTodoClick(todoId: number) {
    // Close first so the overlay is gone before App expands + scrolls the tree.
    onClose()
    onReveal(todoId)
  }

  function handleCommentClick(e: React.MouseEvent, todoId: number) {
    e.stopPropagation()
    onClose()
    onOpenComments(todoId)
    // Also surface the task itself in the tree, not just its comments panel.
    onReveal(todoId)
  }

  function handleAttachmentClick(e: React.MouseEvent, todoId: number, attachmentPath?: string | null, page?: number | null) {
    e.stopPropagation()
    onClose()
    if (attachmentPath) {
      // Open the panel and jump the document viewer to the matching page/place.
      onOpenAttachment(todoId, attachmentPath, query.trim(), page)
    } else {
      onOpenComments(todoId)
    }
    onReveal(todoId)
  }

  const hasResults = results.length > 0
  const showEmpty = query.trim().length >= 2 && !loading && !hasResults

  return (
    <div className="search-overlay" onClick={onClose}>
      <div className="search-panel" onClick={e => e.stopPropagation()}>

        <div className="search-input-row">
          <svg className="search-icon-svg" viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="2">
            <circle cx="8.5" cy="8.5" r="5.5" />
            <line x1="13" y1="13" x2="18" y2="18" />
          </svg>
          <input
            ref={inputRef}
            className="search-input"
            placeholder="Hledat ve všech sloupcích a komentářích…"
            value={query}
            onChange={e => setQuery(e.target.value)}
          />
          {loading && <span className="search-spinner" />}
          <button className="search-close-btn" onClick={onClose} aria-label="Zavřít">✕</button>
        </div>

        <div className="search-results">
          {showEmpty && (
            <div className="search-empty">Nic nenalezeno</div>
          )}
          {results.map(result => {
            const isSubtask = !!result.parentId
            const nonTitleMatches = result.matches.filter(m => m.source !== 'title')
            return (
              <div
                key={result.todoId}
                className="search-result-group"
                onClick={() => handleTodoClick(result.todoId)}
                title="Přejít na úkol"
              >
                {/* Row 1: badge + title */}
                <div className="search-result-row1">
                  <span className={`search-result-badge ${isSubtask ? 'search-result-badge--sub' : 'search-result-badge--task'}`}>
                    {isSubtask ? 'subtask' : 'task'}
                  </span>
                  <span className="search-result-todo">{result.todoTitle}</span>
                </div>

                {/* Row 2: parent context (if subtask) */}
                {result.parentTitle && (
                  <div className="search-result-parent">
                    <span className="search-result-parent-arrow">↳</span>
                    {result.parentTitle}
                  </div>
                )}

                {/* Rows 3+: non-title matches */}
                {nonTitleMatches.map((m, i) =>
                  m.source === 'comment' ? (
                    <div
                      key={i}
                      className="search-result-comment"
                      onClick={e => handleCommentClick(e, result.todoId)}
                      title="Otevřít komentáře"
                    >
                      <span className="search-result-comment-label">💬 komentář</span>
                      <span className="search-result-comment-text">{truncate(m.text)}</span>
                    </div>
                  ) : m.source === 'attachment' ? (
                    <div
                      key={i}
                      className="search-result-comment"
                      onClick={e => handleAttachmentClick(e, result.todoId, m.attachmentPath, m.pageNumber)}
                      title="Otevřít dokument na místě shody"
                    >
                      <span className="search-result-comment-label">📎 {m.fileName || 'příloha'}</span>
                      {m.displayName && (
                        <span className="search-result-attachment-name">{m.displayName}</span>
                      )}
                      <span className="search-result-comment-text">{truncate(m.text)}</span>
                    </div>
                  ) : (
                    <div key={i} className="search-result-field">
                      <span className="search-result-field-name">{SOURCE_LABEL[m.source]}:</span>
                      <span className="search-result-field-val">{m.text}</span>
                    </div>
                  )
                )}
              </div>
            )
          })}
        </div>

      </div>
    </div>
  )
}
