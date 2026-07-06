import { useEffect, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import type { TodoItem, Comment, FilterState } from './types'
import { fetchTodos, createTodo, updateTodo, deleteTodo, moveTodo, reorderTodo, type DropPosition } from './api/todos'
import { fetchComments, fetchCommentCounts, createComment, deleteComment, updateComment } from './api/comments'
import { fetchActiveTodoIds, startSession, endSession } from './api/taskSessions'
import { ancestorIds } from './utils/ancestorIds'
import AddTodoForm from './components/AddTodoForm'
import TodoList from './components/TodoList'
import CommentsPanel from './components/CommentsPanel'
import SearchPanel from './components/SearchPanel'
import ManifestPanel from './components/ManifestPanel'
import { fetchActivity } from './api/activity'

// All possible status values — must match STATUS_OPTIONS in TodoList/TodoItem
const STATUS_VALUES = ['', 'in-process', 'on_hold', 'done', 'failed']

const BOOKMARK_COLORS = [
  '#6366f1', '#3b82f6', '#06b6d4', '#22c55e',
  '#eab308', '#f97316', '#ef4444', '#ec4899', '#8b5cf6',
]
const STATUS_LABELS: Record<string, string> = {
  '': '(Blank)', 'in-process': 'In Process', 'on_hold': 'On Hold', 'done': 'Done', 'failed': 'Failed',
}

const ACTIVITY_TYPES = ['created', 'modified', 'commented']

const DEFAULT_FILTERS: FilterState = {
  nameFilter: '',
  listFilter: new Set<number>(),
  statusFilter: new Set(STATUS_VALUES),
  prioritaExcluded: new Set<string>(),
  relatedFilter: '',
  detailRelatedFilter: '',
  dateFrom: '',
  dateTo: '',
  activityFrom: '',
  activityTo: '',
  activityTypes: new Set(ACTIVITY_TYPES),
}

// Stored in DB — array fields are JSON strings (e.g. "[1,2]", '["done"]')
interface FilterBookmark {
  id: number
  name: string
  color: string
  nameFilter: string
  listFilter: string
  statusFilter: string
  prioritaExcluded: string
  relatedFilter: string
  detailRelatedFilter: string
  dateFrom: string
  dateTo: string
  collapsedIds: string  // JSON int[] of collapsed todo ids; "" = view state not captured
}

function parseBookmarkArrays(b: FilterBookmark) {
  return {
    listFilter:       JSON.parse(b.listFilter       || '[]') as number[],
    statusFilter:     JSON.parse(b.statusFilter     || '[]') as string[],
    prioritaExcluded: JSON.parse(b.prioritaExcluded || '[]') as string[],
  }
}

// Plánovač ("GPS pro tasky") je skrytý, ale funkční: route /now a ManifestPanel
// zůstávají v kódu a fungují. Přepni na true pro zobrazení tlačítek 🧭 Teď / 📄 Manifest.
const SHOW_PLANNER = false

// Live usage vs the free-tier limits (from GET /api/usage) — how close this environment
// is to where paid billing would start.
interface UsageInfo {
  db: { provider: string; usedBytes: number; limitBytes: number }
  uploads: { usedBytes: number; fileCount: number }
  ghcr: { usedBytes: number; limitBytes: number } | null
  generatedAt: string
}

export default function App() {
  const [todos, setTodos] = useState<TodoItem[]>([])
  const [error, setError] = useState<string | null>(null)
  const [openCommentsTodoId, setOpenCommentsTodoId] = useState<number | null>(null)
  // When a search hit inside an attachment is clicked, this asks CommentsPanel to
  // open that file's viewer and jump to where `query` matches. Cleared once consumed.
  const [docJump, setDocJump] = useState<{ path: string; query: string; page?: number } | null>(null)
  const [commentsByTodo, setCommentsByTodo] = useState<Record<number, Comment[]>>({})
  const [commentCounts, setCommentCounts] = useState<Record<number, number>>({})
  const [activeTodoIds, setActiveTodoIds] = useState<Set<number>>(new Set())
  // Incremented after any todo mutation so CommentsPanel Log tab auto-refreshes
  const [logRefreshKey, setLogRefreshKey] = useState(0)

  // Session-end comment modal
  const [sessionEndId, setSessionEndId] = useState<number | null>(null)
  const [sessionEndComment, setSessionEndComment] = useState('')

  // --- filter state (lifted so bookmark button in header can access it) ---
  const [filters, setFilters] = useState<FilterState>(DEFAULT_FILTERS)
  function updateFilters(patch: Partial<FilterState>) {
    setFilters(prev => ({ ...prev, ...patch }))
  }

  // --- activity-date filter (resolved server-side into a set of matching ids) ---
  const [activityOpen, setActivityOpen] = useState(false)
  // null = filter inactive (no date set); otherwise the set of todo ids to show.
  const [activityMatchIds, setActivityMatchIds] = useState<Set<number> | null>(null)
  const activityActive = !!(filters.activityFrom || filters.activityTo)

  useEffect(() => {
    if (!activityActive) { setActivityMatchIds(null); return }
    const types = [...filters.activityTypes]
    // If only one bound is filled, treat it as a single-day filter by mirroring
    // the filled date into the empty bound.
    const from = filters.activityFrom || filters.activityTo
    const to = filters.activityTo || filters.activityFrom
    const handle = setTimeout(() => {
      fetchActivity(from, to, types)
        .then(ids => setActivityMatchIds(new Set(ids)))
        .catch(() => setActivityMatchIds(new Set())) // on error show nothing rather than everything
    }, 300)
    return () => clearTimeout(handle)
  }, [activityActive, filters.activityFrom, filters.activityTo, filters.activityTypes])

  function toggleActivityType(type: string) {
    const next = new Set(filters.activityTypes)
    if (next.has(type)) next.delete(type); else next.add(type)
    updateFilters({ activityTypes: next })
  }

  function clearActivityFilter() {
    updateFilters({ activityFrom: '', activityTo: '', activityTypes: new Set(ACTIVITY_TYPES) })
  }

  // --- collapse/expand state (lifted so bookmarks can capture & restore it) ---
  // Set of collapsed todo ids; everything not listed is expanded. Persisted to
  // localStorage so the tree view survives reloads.
  const [collapsed, setCollapsed] = useState<Set<number>>(() => {
    try {
      const saved = localStorage.getItem('todo-collapsed')
      return saved ? new Set<number>(JSON.parse(saved)) : new Set()
    } catch { return new Set() }
  })
  useEffect(() => {
    localStorage.setItem('todo-collapsed', JSON.stringify([...collapsed]))
  }, [collapsed])

  // --- scroll-to-top button (shows once you've scrolled down a bit) ---
  const [showScrollTop, setShowScrollTop] = useState(false)
  useEffect(() => {
    const onScroll = () => setShowScrollTop(window.scrollY > 300)
    window.addEventListener('scroll', onScroll, { passive: true })
    onScroll()
    return () => window.removeEventListener('scroll', onScroll)
  }, [])

  // --- bookmarks (stored in DB, shared across all origins) ---
  const [bookmarks, setBookmarks] = useState<FilterBookmark[]>([])
  const [bookmarkOpen, setBookmarkOpen] = useState(false)
  const [bookmarkName, setBookmarkName] = useState('')
  const [bookmarkDraftColor, setBookmarkDraftColor] = useState(BOOKMARK_COLORS[0])

  // Load from DB on mount; also migrate any localStorage bookmarks (one-time)
  useEffect(() => {
    fetch('/api/bookmarks')
      .then(r => r.json() as Promise<FilterBookmark[]>)
      .then(async dbBookmarks => {
        setBookmarks(dbBookmarks)
        // One-time migration: if DB is empty, import any saved in localStorage
        const stored = localStorage.getItem('todo-filter-bookmarks')
        if (dbBookmarks.length === 0 && stored) {
          try {
            const local = JSON.parse(stored) as Array<{
              name: string; color: string; nameFilter: string;
              listFilter: number[]; statusFilter: string[]; prioritaExcluded: string[];
              relatedFilter: string; detailRelatedFilter: string; dateFrom: string; dateTo: string;
            }>
            for (const b of local) {
              await fetch('/api/bookmarks', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                  name: b.name, color: b.color,
                  nameFilter: b.nameFilter,
                  listFilter: JSON.stringify(b.listFilter ?? []),
                  statusFilter: JSON.stringify(b.statusFilter ?? []),
                  prioritaExcluded: JSON.stringify(b.prioritaExcluded ?? []),
                  relatedFilter: b.relatedFilter, detailRelatedFilter: b.detailRelatedFilter,
                  dateFrom: b.dateFrom, dateTo: b.dateTo,
                  collapsedIds: '',
                }),
              })
            }
            localStorage.removeItem('todo-filter-bookmarks')
            const refreshed = await fetch('/api/bookmarks').then(r => r.json() as Promise<FilterBookmark[]>)
            setBookmarks(refreshed)
          } catch { /* ignore migration errors */ }
        }
      })
      .catch(() => {})
  }, [])

  async function saveBookmark() {
    const name = bookmarkName.trim()
    if (!name) return
    try {
      const res = await fetch('/api/bookmarks', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          name, color: bookmarkDraftColor,
          nameFilter: filters.nameFilter,
          listFilter: JSON.stringify([...filters.listFilter]),
          statusFilter: JSON.stringify([...filters.statusFilter]),
          prioritaExcluded: JSON.stringify([...filters.prioritaExcluded]),
          relatedFilter: filters.relatedFilter,
          detailRelatedFilter: filters.detailRelatedFilter,
          dateFrom: filters.dateFrom,
          dateTo: filters.dateTo,
          collapsedIds: JSON.stringify([...collapsed]),
        }),
      })
      const created: FilterBookmark = await res.json()
      setBookmarks(prev => [...prev, created])
      setBookmarkName('')
    } catch { /* ignore */ }
  }

  function applyBookmark(b: FilterBookmark) {
    const { listFilter, statusFilter, prioritaExcluded } = parseBookmarkArrays(b)
    setFilters({
      nameFilter: b.nameFilter,
      listFilter: new Set(listFilter),
      statusFilter: new Set(statusFilter),
      prioritaExcluded: new Set(prioritaExcluded),
      relatedFilter: b.relatedFilter,
      detailRelatedFilter: b.detailRelatedFilter,
      dateFrom: b.dateFrom,
      dateTo: b.dateTo,
      // Activity filter is session-only (not persisted in bookmarks); reset to default.
      activityFrom: '',
      activityTo: '',
      activityTypes: new Set(ACTIVITY_TYPES),
    })
    // Restore the exact expand/collapse state captured at save time. Legacy bookmarks
    // (collapsedIds === "") predate this feature — leave the current view untouched.
    if (b.collapsedIds) {
      try { setCollapsed(new Set<number>(JSON.parse(b.collapsedIds))) } catch { /* ignore */ }
    }
    setBookmarkOpen(false)
  }

  async function deleteBookmark(id: number) {
    try {
      await fetch(`/api/bookmarks/${id}`, { method: 'DELETE' })
      setBookmarks(prev => prev.filter(b => b.id !== id))
    } catch { /* ignore */ }
  }

  function getFilterSummary(b: FilterBookmark): string {
    const { listFilter, statusFilter, prioritaExcluded } = parseBookmarkArrays(b)
    const parts: string[] = []
    if (b.nameFilter) parts.push(`Název: "${b.nameFilter}"`)
    if (listFilter.length > 0) {
      const names = listFilter.map(id => todos.find(t => t.id === id)?.title ?? `#${id}`).join(', ')
      parts.push(`List: ${names}`)
    }
    if (statusFilter.length < STATUS_VALUES.length)
      parts.push(`Status: ${statusFilter.map(s => STATUS_LABELS[s] ?? s).join(', ')}`)
    if (prioritaExcluded.length > 0) parts.push(`−Priorita: ${prioritaExcluded.join(', ')}`)
    if (b.relatedFilter) parts.push(`Related: "${b.relatedFilter}"`)
    if (b.detailRelatedFilter) parts.push(`Detail: "${b.detailRelatedFilter}"`)
    if (b.dateFrom || b.dateTo) parts.push(`Datum: ${b.dateFrom || '?'}–${b.dateTo || '?'}`)
    if (b.collapsedIds) parts.push('uložené zobrazení')
    return parts.length > 0 ? parts.join(' · ') : 'Vše'
  }

  useEffect(() => {
    fetchTodos()
      .then(setTodos)
      .catch(() => setError('Could not connect to the server.'))
    fetchCommentCounts()
      .then(setCommentCounts)
      .catch(() => {})
    fetchActiveTodoIds()
      .then(ids => setActiveTodoIds(new Set(ids)))
      .catch(() => {})
  }, [])

  async function handleAdd(title: string, dueDate: string | null, status: string) {
    try {
      const item = await createTodo(title, dueDate, status)
      setTodos(prev => [...prev, item])
    } catch {
      setError('Failed to add todo.')
    }
  }

  async function handleAddSubtask({ title, parentId, dueDate }: { title: string; parentId: number; dueDate: string | null }) {
    try {
      const item = await createTodo(title, dueDate, '', parentId)
      setTodos(prev => [...prev, item])
      setLogRefreshKey(k => k + 1)
    } catch {
      setError('Failed to add subtask.')
    }
  }

  async function handleUpdate(updated: TodoItem) {
    setTodos(prev => prev.map(t => (t.id === updated.id ? { ...t, ...updated } : t)))
    try {
      const item = await updateTodo(updated)
      setTodos(prev => prev.map(t => (t.id === item.id ? item : t)))
      setLogRefreshKey(k => k + 1)
    } catch {
      setError('Failed to update todo.')
    }
  }

  async function handleMove(id: number, direction: 'up' | 'down') {
    try {
      const updated = await moveTodo(id, direction)
      setTodos(prev => prev.map(t => {
        const u = updated.find(x => x.id === t.id)
        return u ? { ...t, sortOrder: u.sortOrder } : t
      }))
    } catch {
      setError('Failed to move todo.')
    }
  }

  async function handleReorder(id: number, targetId: number, position: DropPosition) {
    try {
      const updated = await reorderTodo(id, targetId, position)
      // The endpoint returns every todo with fresh parentId/sortOrder; merge both.
      setTodos(prev => prev.map(t => {
        const u = updated.find(x => x.id === t.id)
        return u ? { ...t, parentId: u.parentId, sortOrder: u.sortOrder } : t
      }))
      setLogRefreshKey(k => k + 1)
    } catch {
      setError('Failed to reorder todo.')
    }
  }

  // Make a (possibly hidden) todo visible and flash it. A search hit can sit
  // inside a collapsed parent or be hidden by an active filter — in both cases
  // the row isn't in the DOM, so we first expand every ancestor and clear
  // filters, then wait for the re-render before scrolling to it.
  function handleRevealTodo(id: number) {
    const ancestors = ancestorIds(todos, id)
    if (ancestors.length > 0) {
      setCollapsed(prev => {
        const next = new Set(prev)
        ancestors.forEach(a => next.delete(a))
        return next
      })
    }
    setFilters(DEFAULT_FILTERS)

    // Two rAFs: let React commit the expand/filter state change and lay out the
    // newly-rendered rows before we look the element up.
    requestAnimationFrame(() => requestAnimationFrame(() => {
      const el = document.querySelector<HTMLElement>(`[data-todo-id="${id}"]`)
      if (!el) return
      el.scrollIntoView({ behavior: 'smooth', block: 'center' })
      el.classList.add('search-highlight')
      setTimeout(() => el.classList.remove('search-highlight'), 1500)
    }))
  }

  async function handleToggleActive(id: number) {
    try {
      if (activeTodoIds.has(id)) {
        // Show comment modal before ending — actual end happens in confirmSessionEnd
        setSessionEndId(id)
        setSessionEndComment('')
      } else {
        await startSession(id)
        setActiveTodoIds(prev => new Set([...prev, id]))
      }
    } catch {
      setError('Failed to update active session.')
    }
  }

  async function confirmSessionEnd(comment?: string) {
    if (sessionEndId === null) return
    const id = sessionEndId
    setSessionEndId(null)
    setSessionEndComment('')
    try {
      await endSession(id, comment)
      setActiveTodoIds(prev => { const next = new Set(prev); next.delete(id); return next })
    } catch {
      setError('Failed to end session.')
    }
  }

  async function handleDelete(id: number) {
    try {
      await deleteTodo(id)
      setTodos(prev => prev.filter(t => t.id !== id))
      setLogRefreshKey(k => k + 1)
    } catch {
      setError('Failed to delete todo.')
    }
  }

  async function handleOpenComments(id: number) {
    setDocJump(null) // a plain open shouldn't carry over a stale attachment jump
    setOpenCommentsTodoId(id)
    if (!commentsByTodo[id]) {
      try {
        const comments = await fetchComments(id)
        setCommentsByTodo(prev => ({ ...prev, [id]: comments }))
      } catch {
        setCommentsByTodo(prev => ({ ...prev, [id]: [] }))
      }
    }
  }

  // From a search hit inside an attachment: open the todo's comments and tell the
  // panel which file to open and what term to jump to.
  async function handleOpenAttachment(todoId: number, attachmentPath: string, query: string, page?: number | null) {
    setDocJump({ path: attachmentPath, query, page: page ?? undefined })
    setOpenCommentsTodoId(todoId)
    if (!commentsByTodo[todoId]) {
      try {
        const comments = await fetchComments(todoId)
        setCommentsByTodo(prev => ({ ...prev, [todoId]: comments }))
      } catch {
        setCommentsByTodo(prev => ({ ...prev, [todoId]: [] }))
      }
    }
  }

  async function handleDeleteComment(commentId: number) {
    try {
      await deleteComment(commentId)
      setCommentsByTodo(prev => {
        const updated: Record<number, Comment[]> = {}
        for (const [tid, cs] of Object.entries(prev)) {
          updated[Number(tid)] = cs.filter(c => c.id !== commentId)
        }
        return updated
      })
      setCommentCounts(prev => {
        const todoId = Object.entries(commentsByTodo).find(([, cs]) => cs.some(c => c.id === commentId))?.[0]
        if (!todoId) return prev
        const next = Math.max(0, (prev[Number(todoId)] ?? 1) - 1)
        return { ...prev, [todoId]: next }
      })
    } catch {
      setError('Failed to delete comment.')
    }
  }

  async function handleEditComment(commentId: number, text: string) {
    try {
      const updated = await updateComment(commentId, text)
      setCommentsByTodo(prev => {
        const next: Record<number, Comment[]> = {}
        for (const [tid, cs] of Object.entries(prev)) {
          next[Number(tid)] = cs.map(c => c.id === commentId ? { ...c, text: updated.text } : c)
        }
        return next
      })
    } catch {
      setError('Failed to update comment.')
    }
  }

  async function handleAddComment(todoId: number, text: string, files?: File[], previews?: (File | undefined)[]) {
    try {
      const comment = await createComment(todoId, text, files ?? [], previews ?? [])
      setCommentsByTodo(prev => ({
        ...prev,
        [todoId]: [...(prev[todoId] ?? []), comment],
      }))
      setCommentCounts(prev => ({ ...prev, [todoId]: (prev[todoId] ?? 0) + 1 }))
    } catch {
      setError('Failed to add comment.')
    }
  }

  const [searchOpen, setSearchOpen] = useState(false)
  const [manifestOpen, setManifestOpen] = useState(false)

  const [exportIncludeFiles, setExportIncludeFiles] = useState(true)
  const [exportOpen, setExportOpen] = useState(false)
  const [importOpen, setImportOpen] = useState(false)
  const [backupPassword, setBackupPassword] = useState('')
  const [backupBusy, setBackupBusy] = useState(false)
  const [backupError, setBackupError] = useState('')
  const importFileRef = useRef<HTMLInputElement>(null)
  const [importFile, setImportFile] = useState<File | null>(null)
  const [importMode, setImportMode] = useState<'replace' | 'merge' | 'addonly' | 'time'>('replace')
  const [importFormat, setImportFormat] = useState<'backup' | 'csv'>('backup')
  const [csvInfoOpen, setCsvInfoOpen] = useState(false)
  const [csvCopied, setCsvCopied] = useState(false)

  // Free-tier usage panel
  const [limitsOpen, setLimitsOpen] = useState(false)
  const [usage, setUsage] = useState<UsageInfo | null>(null)
  const [usageLoading, setUsageLoading] = useState(false)

  async function fetchUsage() {
    setUsageLoading(true)
    try {
      const res = await fetch('/api/usage')
      if (res.ok) setUsage(await res.json() as UsageInfo)
    } catch { /* ignore — panel shows a dash */ }
    finally { setUsageLoading(false) }
  }
  function openLimits() { setLimitsOpen(true); fetchUsage() }

  async function handleExport() {
    if (!backupPassword) { setBackupError('Zadej heslo.'); return }
    setBackupBusy(true); setBackupError('')
    try {
      const res = await fetch('/api/export/export', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ password: backupPassword, includeFiles: exportIncludeFiles }),
      })
      if (!res.ok) throw new Error(await res.text())
      const blob = await res.blob()
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url; a.download = 'todolist.backup'; a.click()
      URL.revokeObjectURL(url)
      setExportOpen(false); setBackupPassword('')
    } catch (e: unknown) {
      setBackupError(e instanceof Error ? e.message : 'Export selhal.')
    } finally { setBackupBusy(false) }
  }

  async function handleImport() {
    if (!importFile) { setBackupError('Vyber soubor.'); return }
    if (importFormat === 'backup' && !backupPassword) { setBackupError('Zadej heslo.'); return }
    setBackupBusy(true); setBackupError('')
    try {
      const fd = new FormData()
      fd.append('file', importFile)
      let url: string
      if (importFormat === 'csv') {
        url = '/api/export/import-csv'
      } else if (importMode === 'time') {
        url = '/api/export/import-time'
      } else {
        fd.append('password', backupPassword)
        fd.append('mode', importMode)
        url = '/api/export/import'
      }
      const res = await fetch(url, { method: 'POST', body: fd })
      if (!res.ok) throw new Error(await res.text())
      const refreshed = await fetchTodos()
      setTodos(refreshed)
      const counts = await fetchCommentCounts()
      setCommentCounts(counts)
      setImportOpen(false); setBackupPassword(''); setImportFile(null); setImportMode('replace'); setImportFormat('backup')
    } catch (e: unknown) {
      setBackupError(e instanceof Error ? e.message : 'Import selhal.')
    } finally { setBackupBusy(false) }
  }

  async function handleExportTime() {
    try {
      const res = await fetch('/api/export/export-time')
      if (!res.ok) throw new Error(await res.text())
      const blob = await res.blob()
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url; a.download = `todolist-time-${new Date().toISOString().slice(0,10)}.zip`; a.click()
      URL.revokeObjectURL(url)
    } catch {
      setError('Export časování selhal.')
    }
  }

  function openExport() { setExportOpen(true); setImportOpen(false); setBackupPassword(''); setBackupError(''); setExportIncludeFiles(true) }
  function openImport() { setImportOpen(true); setExportOpen(false); setBackupPassword(''); setBackupError(''); setImportFile(null); setImportMode('replace'); setImportFormat('backup') }
  function closeBackup() { setExportOpen(false); setImportOpen(false); setBackupPassword(''); setBackupError(''); setImportMode('replace' as const); setImportFormat('backup'); setCsvInfoOpen(false) }

  const openTodo = openCommentsTodoId !== null ? todos.find(t => t.id === openCommentsTodoId) : undefined

  return (
    <div className={`app${openCommentsTodoId !== null ? ' panel-open' : ''}`}>
      <div className="app-header">
        <h1>Todo List</h1>
        <div className="backup-btns">
          <button className="backup-btn search-open-btn" onClick={() => setSearchOpen(true)} aria-label="Hledat">
            <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="2.2" width="15" height="15">
              <circle cx="8.5" cy="8.5" r="5.5" />
              <line x1="13" y1="13" x2="18" y2="18" />
            </svg>
          </button>
          {SHOW_PLANNER && (
            <>
              <Link className="backup-btn now-link" to="/now">🧭 Teď</Link>
              <button className="backup-btn" onClick={() => setManifestOpen(true)}>📄 Manifest</button>
            </>
          )}
          <button className="backup-btn" onClick={openExport}>↓ Export</button>
          <button className="backup-btn" onClick={openImport}>↑ Import</button>
          <button className="backup-btn" onClick={handleExportTime}>↓ Časování</button>
          <button className="backup-btn" onClick={openLimits} title="Blízkost placených služeb">📊 Limity</button>
        </div>
      </div>
      {(exportOpen || importOpen) && (
        <div className="backup-modal-overlay" onClick={closeBackup}>
          <div className="backup-modal" onClick={e => e.stopPropagation()}>
            <div className="backup-modal-title">{exportOpen ? 'Export zálohy' : 'Import zálohy'}</div>
            {exportOpen && (
              <label className="backup-merge-label">
                <input type="checkbox" checked={exportIncludeFiles} onChange={e => setExportIncludeFiles(e.target.checked)} />
                Zahrnout přílohy z komentářů
              </label>
            )}
            {importOpen && (
              <>
                <div className="backup-file-row">
                  <label className="backup-file-btn">
                    {importFile ? importFile.name : 'Vybrat soubor…'}
                    <input ref={importFileRef} type="file"
                      key={importFormat + importMode}
                      accept={importFormat === 'csv' ? '.csv' : importMode === 'time' ? '.zip' : '.backup'}
                      style={{ display: 'none' }}
                      onChange={e => setImportFile(e.target.files?.[0] ?? null)} />
                  </label>
                </div>
                <div className="backup-mode-group">
                  {([
                    { value: 'replace', label: 'Nahradit vše' },
                    { value: 'merge',   label: 'Přidat nové + odebrat chybějící' },
                    { value: 'addonly', label: 'Přidat nové + zachovat vše' },
                    { value: 'time',    label: 'Importovat časování (.zip)' },
                  ] as const).map(opt => (
                    <label key={opt.value} className="backup-merge-label">
                      <input type="radio" name="importMode" value={opt.value}
                        checked={importMode === opt.value}
                        onChange={() => { setImportMode(opt.value); if (opt.value !== 'addonly') setImportFormat('backup'); setImportFile(null) }} />
                      {opt.label}
                    </label>
                  ))}
                </div>
                {importMode === 'addonly' && (
                  <div className="backup-mode-group" style={{ marginTop: 4, paddingLeft: 16, borderLeft: '2px solid #e5e7eb' }}>
                    {([
                      { value: 'backup', label: 'Ze zálohy (.backup)' },
                      { value: 'csv',    label: 'Z CSV souboru (.csv)' },
                    ] as const).map(opt => (
                      <label key={opt.value} className="backup-merge-label">
                        <input type="radio" name="importFormat" value={opt.value}
                          checked={importFormat === opt.value}
                          onChange={() => { setImportFormat(opt.value); setImportFile(null) }} />
                        {opt.label}
                        {opt.value === 'csv' && (
                          <span className="csv-info-wrap">
                            <button
                              className="csv-info-btn"
                              type="button"
                              onClick={e => { e.preventDefault(); e.stopPropagation(); setCsvInfoOpen(o => !o) }}
                              aria-label="Formát CSV"
                            >i</button>
                            {csvInfoOpen && (
                              <div className="csv-info-popup" onClick={e => {
                                e.stopPropagation()
                                const text = `title;parent;status;priority;related;detailRelated;dueDate\nopalovací krém;seznam na dovolenou;;;;;`
                                navigator.clipboard.writeText(text).then(() => {
                                  setCsvCopied(true)
                                  setTimeout(() => setCsvCopied(false), 1500)
                                })
                              }}>
                                <pre className="csv-info-pre">{`title;parent;status;priority;related;detailRelated;dueDate\nopalovací krém;seznam na dovolenou;;;;;`}</pre>
                                <div className="csv-info-copy-hint">{csvCopied ? '✓ Zkopírováno!' : 'Klikni pro kopírování'}</div>
                              </div>
                            )}
                          </span>
                        )}
                      </label>
                    ))}
                  </div>
                )}
              </>
            )}
            {!(importOpen && (importFormat === 'csv' || importMode === 'time')) && (
              <input
                className="backup-password"
                type="password"
                placeholder="Heslo"
                value={backupPassword}
                onChange={e => setBackupPassword(e.target.value)}
                onKeyDown={e => e.key === 'Enter' && (exportOpen ? handleExport() : handleImport())}
                autoFocus
              />
            )}
            {backupError && <div className="backup-error">{backupError}</div>}
            <div className="backup-modal-actions">
              <button className="backup-cancel-btn" onClick={closeBackup}>Zrušit</button>
              <button className="backup-confirm-btn" disabled={backupBusy}
                onClick={exportOpen ? handleExport : handleImport}>
                {backupBusy ? '…' : exportOpen ? 'Exportovat' : 'Importovat'}
              </button>
            </div>
          </div>
        </div>
      )}
      {limitsOpen && (
        <div className="backup-modal-overlay" onClick={() => setLimitsOpen(false)}>
          <div className="backup-modal limits-modal" onClick={e => e.stopPropagation()}>
            <div className="backup-modal-title">Blízkost placených služeb</div>
            {usage ? (() => {
              const MB = (b: number) => b / 1048576
              const pct = Math.min(100, (usage.db.usedBytes / usage.db.limitBytes) * 100)
              const color = pct > 90 ? '#dc2626' : pct > 70 ? '#d97706' : '#16a34a'
              const env = typeof location !== 'undefined' && location.hostname.includes('todolist-dev') ? 'dev' : 'prod'
              return (
                <div className="limits-body">
                  <div className="limit-row">
                    <div className="limit-head">
                      <span>{usage.db.provider} — databáze</span>
                      <span>{MB(usage.db.usedBytes).toFixed(1)} / {MB(usage.db.limitBytes).toFixed(0)} MB · {pct.toFixed(1)}%</span>
                    </div>
                    <div className="limit-track"><div className="limit-fill" style={{ width: `${pct}%`, background: color }} /></div>
                    <div className="limit-note">Free do 0,5 GB / projekt. Nad limit → placený plán (~$19/měs).</div>
                  </div>
                  <div className="limit-row">
                    <div className="limit-head">
                      <span>Přílohy — Azure Files</span>
                      <span>{MB(usage.uploads.usedBytes).toFixed(1)} MB · {usage.uploads.fileCount} souborů</span>
                    </div>
                    <div className="limit-note">Měřené, ale haléře (~€0,05/GB). Bez ostrého limitu.</div>
                  </div>
                  {usage.ghcr && (() => {
                    const gpct = Math.min(100, (usage.ghcr.usedBytes / usage.ghcr.limitBytes) * 100)
                    const gcolor = gpct > 90 ? '#dc2626' : gpct > 70 ? '#d97706' : '#16a34a'
                    return (
                      <div className="limit-row">
                        <div className="limit-head">
                          <span>GHCR — image storage</span>
                          <span>{MB(usage.ghcr.usedBytes).toFixed(1)} / {MB(usage.ghcr.limitBytes).toFixed(0)} MB · {gpct.toFixed(1)}%</span>
                        </div>
                        <div className="limit-track"><div className="limit-fill" style={{ width: `${gpct}%`, background: gcolor }} /></div>
                        <div className="limit-note">Free do 500 MB (private package). Transfer 1 GB/měs se měří mimo appku.</div>
                      </div>
                    )
                  })()}
                  <div className="limit-static">
                    <div>Container Apps: prod teplá replika 9–22 ≈ €1,5/měs (compute, ne MB)</div>
                    <div>Actions (CI): public repo → zdarma neomezeně</div>
                    <div className="limit-note">Měřeno z prostředí <b>{env}</b> · {new Date(usage.generatedAt).toLocaleTimeString()}</div>
                  </div>
                </div>
              )
            })() : (
              <div className="limits-body"><p className="limit-note">{usageLoading ? 'Načítám…' : 'Data se nepodařilo načíst.'}</p></div>
            )}
            <div className="backup-modal-actions">
              <button className="backup-cancel-btn" onClick={() => setLimitsOpen(false)}>Zavřít</button>
              <button className="backup-confirm-btn" disabled={usageLoading} onClick={fetchUsage}>{usageLoading ? '…' : 'Aktualizovat'}</button>
            </div>
          </div>
        </div>
      )}
      {error && <p className="error">{error}</p>}
      <AddTodoForm onAdd={handleAdd} />

      {/* Bookmark bar — sits above the filter row */}
      <div className="bookmark-bar">
        {/* Activity-date filter — sits to the left of Záložky */}
        <div style={{ position: 'relative' }}>
          <button
            className={`backup-btn activity-toggle-btn${activityActive ? ' activity-toggle-btn--active' : ''}`}
            onClick={() => setActivityOpen(o => !o)}
            title="Filtrovat podle data aktivity (vytvoření / úpravy / komentáře)"
          >
            🕒 Aktivita{activityActive ? ' •' : ''}
          </button>
          {activityOpen && (
            <>
              <div className="bookmark-backdrop" onClick={() => setActivityOpen(false)} />
              <div className="activity-panel" onClick={e => e.stopPropagation()}>
                <div className="bookmark-panel-title">Filtr podle aktivity</div>
                <div className="activity-date-row">
                  <label className="date-filter-label">
                    Od
                    <input
                      type="date"
                      className="date-filter-input"
                      value={filters.activityFrom}
                      onChange={e => updateFilters({ activityFrom: e.target.value })}
                    />
                  </label>
                  <label className="date-filter-label">
                    Do
                    <input
                      type="date"
                      className="date-filter-input"
                      value={filters.activityTo}
                      onChange={e => updateFilters({ activityTo: e.target.value })}
                    />
                  </label>
                </div>
                <div className="activity-types">
                  {[['created', 'Vytvořeno'], ['modified', 'Upraveno'], ['commented', 'Komentáře']].map(([key, label]) => (
                    <label key={key} className="filter-option">
                      <input
                        type="checkbox"
                        checked={filters.activityTypes.has(key)}
                        onChange={() => toggleActivityType(key)}
                      />
                      {label}
                    </label>
                  ))}
                </div>
                {activityActive && (
                  <button className="date-filter-clear" onClick={clearActivityFilter}>✕ Vymazat</button>
                )}
              </div>
            </>
          )}
        </div>
        <div style={{ position: 'relative' }}>
          <button
            className={`backup-btn bookmark-toggle-btn${bookmarkOpen ? ' bookmark-toggle-btn--open' : ''}`}
            onClick={() => { setBookmarkOpen(o => !o); setBookmarkName('') }}
          >
            🔖 Záložky
          </button>
          {bookmarkOpen && (
            <>
              <div className="bookmark-backdrop" onClick={() => setBookmarkOpen(false)} />
              <div className="bookmark-panel" onClick={e => e.stopPropagation()}>
                <div className="bookmark-panel-title">Záložky filtrů</div>
                <div className="bookmark-add-row">
                  <input
                    className="bookmark-name-input"
                    placeholder="Název záložky…"
                    value={bookmarkName}
                    onChange={e => setBookmarkName(e.target.value)}
                    onKeyDown={e => { if (e.key === 'Enter') saveBookmark() }}
                    autoFocus
                  />
                  <button className="bookmark-save-btn" style={{ background: bookmarkDraftColor }} onClick={saveBookmark}>Uložit</button>
                </div>
                <div className="bookmark-color-row">
                  {BOOKMARK_COLORS.map(c => (
                    <button
                      key={c}
                      className={`bookmark-color-swatch${bookmarkDraftColor === c ? ' bookmark-color-swatch--active' : ''}`}
                      style={{ background: c }}
                      onClick={() => setBookmarkDraftColor(c)}
                      aria-label={c}
                    />
                  ))}
                </div>
                <div className="bookmark-list">
                  {bookmarks.length === 0 ? (
                    <p className="bookmark-empty">Žádné záložky</p>
                  ) : (
                    bookmarks.map(b => (
                      <div key={b.id} className="bookmark-item" onClick={() => applyBookmark(b)}>
                        <span className="bookmark-item-dot" style={{ background: b.color ?? BOOKMARK_COLORS[0] }} />
                        <div className="bookmark-item-content">
                          <span className="bookmark-item-name">{b.name}</span>
                          <span className="bookmark-item-summary">{getFilterSummary(b)}</span>
                        </div>
                        <button
                          className="bookmark-delete-btn"
                          onClick={e => { e.stopPropagation(); deleteBookmark(b.id) }}
                          aria-label="Smazat záložku"
                        >✕</button>
                      </div>
                    ))
                  )}
                </div>
              </div>
            </>
          )}
        </div>
        {/* Quick-access chips */}
        {bookmarks.map(b => (
          <button
            key={b.id}
            className="bookmark-chip"
            style={{ background: b.color ?? BOOKMARK_COLORS[0] }}
            onClick={() => applyBookmark(b)}
            title={getFilterSummary(b)}
          >
            {b.name}
          </button>
        ))}
      </div>

      <TodoList
        todos={todos}
        onUpdate={handleUpdate}
        onDelete={handleDelete}
        onAdd={handleAddSubtask}
        onOpenComments={handleOpenComments}
        commentCounts={commentCounts}
        onMove={handleMove}
        onReorder={handleReorder}
        activeTodoIds={activeTodoIds}
        onToggleActive={handleToggleActive}
        filters={filters}
        onFiltersChange={updateFilters}
        activityMatchIds={activityMatchIds}
        collapsed={collapsed}
        onCollapsedChange={setCollapsed}
      />
      {searchOpen && (
        <SearchPanel
          onClose={() => setSearchOpen(false)}
          onOpenComments={handleOpenComments}
          onReveal={handleRevealTodo}
          onOpenAttachment={handleOpenAttachment}
        />
      )}
      {manifestOpen && (
        <ManifestPanel
          onClose={() => setManifestOpen(false)}
          onSaved={() => fetchTodos().then(setTodos).catch(() => {})}
        />
      )}
      {sessionEndId !== null && (
        <div className="session-end-overlay" onClick={() => confirmSessionEnd()}>
          <div className="session-end-modal" onClick={e => e.stopPropagation()}>
            <div className="session-end-title">Ukončit sledování</div>
            <p className="session-end-hint">Volitelný komentář k tomuto sezení:</p>
            <textarea
              className="session-end-textarea"
              placeholder="Co jsem udělal/a…"
              value={sessionEndComment}
              onChange={e => setSessionEndComment(e.target.value)}
              onKeyDown={e => {
                if (e.key === 'Enter' && e.ctrlKey) confirmSessionEnd(sessionEndComment.trim() || undefined)
                if (e.key === 'Escape') confirmSessionEnd()
              }}
              autoFocus
            />
            <div className="session-end-actions">
              <button className="session-end-skip" onClick={() => confirmSessionEnd()}>Přeskočit</button>
              <button className="session-end-save" onClick={() => confirmSessionEnd(sessionEndComment.trim() || undefined)}>
                Uložit a ukončit
              </button>
            </div>
          </div>
        </div>
      )}

      {openTodo && (
        <CommentsPanel
          todoId={openTodo.id}
          todoTitle={openTodo.title}
          comments={commentsByTodo[openTodo.id] ?? []}
          onClose={() => setOpenCommentsTodoId(null)}
          onAddComment={handleAddComment}
          onDeleteComment={handleDeleteComment}
          onEditComment={handleEditComment}
          logRefreshKey={logRefreshKey}
          onReveal={handleRevealTodo}
          docJump={docJump}
          onDocJumpConsumed={() => setDocJump(null)}
        />
      )}

      {showScrollTop && (
        <button
          className="scroll-top-btn"
          aria-label="Nahoru"
          title="Posunout nahoru"
          onClick={() => window.scrollTo({ top: 0, behavior: 'smooth' })}
        >
          ↑
        </button>
      )}
    </div>
  )
}
