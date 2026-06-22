import React, { Fragment, useMemo, useState } from 'react'
import type { TodoItem as Todo, FilterState } from '../types'
import type { DropPosition } from '../api/todos'
import TodoItem from './TodoItem'

const STATUS_OPTIONS = [
  { value: '',           label: '(Blank)' },
  { value: 'in-process', label: 'In Process' },
  { value: 'on_hold',    label: 'On Hold' },
  { value: 'done',       label: 'Done' },
  { value: 'failed',     label: 'Failed' },
]

const ALL_STATUSES = new Set(STATUS_OPTIONS.map(o => o.value))

interface Props {
  todos: Todo[]
  onUpdate: (todo: Todo) => void
  onDelete: (id: number) => void
  onAdd?: (params: { title: string; parentId: number; dueDate: string | null }) => void
  onOpenComments?: (id: number) => void
  commentCounts?: Record<number, number>
  onMove?: (id: number, direction: 'up' | 'down') => void
  onReorder?: (id: number, targetId: number, position: DropPosition) => void
  activeTodoIds?: Set<number>
  onToggleActive?: (id: number) => void
  filters: FilterState
  onFiltersChange: (patch: Partial<FilterState>) => void
  // Pre-resolved set of todo ids matching the activity-date filter; null = filter off.
  activityMatchIds?: Set<number> | null
  collapsed: Set<number>
  onCollapsedChange: (next: Set<number>) => void
}

export default function TodoList({ todos, onUpdate, onDelete, onAdd, onOpenComments, commentCounts = {}, onMove, onReorder, activeTodoIds = new Set(), onToggleActive, filters, onFiltersChange, activityMatchIds = null, collapsed, onCollapsedChange }: Props) {
  const { nameFilter, listFilter, statusFilter, prioritaExcluded, relatedFilter, detailRelatedFilter, dateFrom, dateTo } = filters

  const [listOpen, setListOpen] = useState(false)
  const [statusOpen, setStatusOpen] = useState(false)
  const [prioritaOpen, setPrioritaOpen] = useState(false)
  const [dateOpen, setDateOpen] = useState(false)
  const [pendingSubtaskFor, setPendingSubtaskFor] = useState<number | null>(null)
  const [subtaskDraft, setSubtaskDraft] = useState('')
  const [subtaskDate, setSubtaskDate] = useState('')
  const [movingId, setMovingId] = useState<number | null>(null)
  const [moveSearch, setMoveSearch] = useState('')

  // Drag-and-drop reorder state
  const [draggingId, setDraggingId] = useState<number | null>(null)
  const [dropTarget, setDropTarget] = useState<{ id: number; position: DropPosition } | null>(null)

  // Which drop zone is the cursor over, based on vertical position within the row:
  // top third → before, bottom third → after, middle → inside (nest as child).
  function zoneFor(e: React.DragEvent): DropPosition {
    const rect = (e.currentTarget as HTMLElement).getBoundingClientRect()
    const y = e.clientY - rect.top
    if (y < rect.height * 0.30) return 'before'
    if (y > rect.height * 0.70) return 'after'
    return 'inside'
  }

  function handleRowDragOver(e: React.DragEvent, targetId: number) {
    if (draggingId === null || draggingId === targetId) return
    e.preventDefault() // allow drop
    const position = zoneFor(e)
    setDropTarget(prev =>
      prev?.id === targetId && prev.position === position ? prev : { id: targetId, position })
  }

  function handleRowDrop(e: React.DragEvent, targetId: number) {
    e.preventDefault()
    const sourceId = draggingId
    const position = dropTarget?.id === targetId ? dropTarget.position : zoneFor(e)
    setDraggingId(null)
    setDropTarget(null)
    if (sourceId === null || sourceId === targetId) return
    onReorder?.(sourceId, targetId, position)
  }

  function endDrag() {
    setDraggingId(null)
    setDropTarget(null)
  }

  // Touch/pen drag reorder via Pointer Events. The native HTML5 drag-and-drop
  // used above only fires for a mouse, so iOS/Android can't reorder with it.
  // For touch we track the pointer ourselves: find the row under the finger,
  // reuse the same before/after/inside zones, and reorder on release.
  function startTouchDrag(sourceId: number, e: React.PointerEvent) {
    if (!onReorder || e.pointerType === 'mouse') return // mouse keeps HTML5 DnD
    e.preventDefault()
    setDraggingId(sourceId)
    let current: { id: number; position: DropPosition } | null = null

    const onMove = (ev: PointerEvent) => {
      ev.preventDefault() // stop the page from scrolling while dragging
      const el = document.elementFromPoint(ev.clientX, ev.clientY) as HTMLElement | null
      const row = el?.closest('[data-todo-id]') as HTMLElement | null
      if (!row) { current = null; setDropTarget(null); return }
      const targetId = Number(row.getAttribute('data-todo-id'))
      if (!targetId || targetId === sourceId) { current = null; setDropTarget(null); return }
      const rect = row.getBoundingClientRect()
      const y = ev.clientY - rect.top
      const position: DropPosition = y < rect.height * 0.30 ? 'before' : y > rect.height * 0.70 ? 'after' : 'inside'
      current = { id: targetId, position }
      setDropTarget(prev => prev?.id === targetId && prev.position === position ? prev : { id: targetId, position })
    }
    const onUp = () => {
      window.removeEventListener('pointermove', onMove)
      window.removeEventListener('pointerup', onUp)
      window.removeEventListener('pointercancel', onUp)
      const drop = current
      setDraggingId(null)
      setDropTarget(null)
      if (drop && drop.id !== sourceId) onReorder?.(sourceId, drop.id, drop.position)
    }
    window.addEventListener('pointermove', onMove, { passive: false })
    window.addEventListener('pointerup', onUp)
    window.addEventListener('pointercancel', onUp)
  }

  // Strip diacritics + lowercase for accent-insensitive matching ("krem" matches "krém")
  function normalizeText(s: string): string {
    return s.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase()
  }

  function closeMovePicker() {
    setMovingId(null)
    setMoveSearch('')
  }

  function toggleStatus(value: string) {
    const next = new Set(statusFilter)
    if (next.has(value)) next.delete(value)
    else next.add(value)
    onFiltersChange({ statusFilter: next })
  }

  function toggleList(id: number) {
    const next = new Set(listFilter)
    if (next.has(id)) next.delete(id)
    else next.add(id)
    onFiltersChange({ listFilter: next })
  }

  function togglePriorita(value: string) {
    const next = new Set(prioritaExcluded)
    if (next.has(value)) next.delete(value)
    else next.add(value)
    onFiltersChange({ prioritaExcluded: next })
  }

  const allPrioritas = [...new Set(todos.map(t => t.priority || ''))].sort()

  const sorted = [...todos].sort((a, b) => a.sortOrder - b.sortOrder || a.createdAt.localeCompare(b.createdAt))
  const rootTodos = sorted.filter(t => t.parentId === null)
  const subtasksOf = (parentId: number) => sorted.filter(t => t.parentId === parentId)

  const childStatusMap = useMemo(() => {
    const childrenOf = new Map<number, number[]>()
    todos.forEach(t => {
      if (t.parentId !== null) {
        if (!childrenOf.has(t.parentId)) childrenOf.set(t.parentId, [])
        childrenOf.get(t.parentId)!.push(t.id)
      }
    })
    const todoById = new Map(todos.map(t => [t.id, t]))

    function collectCounts(id: number): Record<string, number> {
      const counts: Record<string, number> = {}
      for (const childId of childrenOf.get(id) ?? []) {
        const s = todoById.get(childId)?.status || ''
        counts[s] = (counts[s] || 0) + 1
        for (const [gs, gc] of Object.entries(collectCounts(childId))) {
          counts[gs] = (counts[gs] || 0) + gc
        }
      }
      return counts
    }

    const map: Record<number, Record<string, number>> = {}
    todos.forEach(t => {
      if (childrenOf.has(t.id)) {
        const counts = collectCounts(t.id)
        const s = t.status || ''
        counts[s] = (counts[s] || 0) + 1
        map[t.id] = counts
      }
    })
    return map
  }, [todos])

  // Map each todo ID to its root ancestor's ID (for the List filter)
  const rootIdOf = useMemo(() => {
    const todoById = new Map(todos.map(t => [t.id, t]))
    const map = new Map<number, number>()
    function getRootId(id: number): number {
      const todo = todoById.get(id)
      if (!todo || todo.parentId === null) return id
      return getRootId(todo.parentId)
    }
    todos.forEach(t => map.set(t.id, getRootId(t.id)))
    return map
  }, [todos])

  const visibleIds = useMemo<Set<number> | null>(() => {
    const noFilters = !nameFilter
      && listFilter.size === 0
      && statusFilter.size === ALL_STATUSES.size
      && prioritaExcluded.size === 0
      && !relatedFilter
      && !detailRelatedFilter
      && !dateFrom
      && !dateTo
      && activityMatchIds === null
    if (noFilters) return null

    function matches(todo: Todo): boolean {
      // When the activity filter is on, it takes over: every other filter (incl.
      // any applied bookmark's status/list/name…) is ignored, so you see all todos
      // in the activity range. Turning the activity filter off restores the rest.
      if (activityMatchIds !== null) return activityMatchIds.has(todo.id)

      if (nameFilter && !todo.title.toLowerCase().includes(nameFilter.toLowerCase())) return false
      if (listFilter.size > 0 && !listFilter.has(rootIdOf.get(todo.id) ?? todo.id)) return false
      if (!statusFilter.has(todo.status)) return false
      if (prioritaExcluded.has(todo.priority || '')) return false
      if (relatedFilter && !todo.related.toLowerCase().includes(relatedFilter.toLowerCase())) return false
      if (detailRelatedFilter && !todo.detailRelated.toLowerCase().includes(detailRelatedFilter.toLowerCase())) return false
      if (dateFrom || dateTo) {
        if (!todo.dueDate) return false
        const d = todo.dueDate.split('T')[0]
        if (dateFrom && d < dateFrom) return false
        if (dateTo && d > dateTo) return false
      }
      return true
    }

    const ids = new Set<number>()
    todos.forEach(todo => {
      if (matches(todo)) ids.add(todo.id)
    })
    return ids
  }, [todos, filters, rootIdOf, activityMatchIds])

  if (rootTodos.length === 0) {
    return <p className="empty">No todos yet. Add one above!</p>
  }

  function openSubtask(id: number) {
    setPendingSubtaskFor(id)
    setSubtaskDraft('')
    setSubtaskDate('')
  }

  function commitSubtask(parentId: number) {
    const title = subtaskDraft.trim()
    if (title) {
      onAdd?.({ title, parentId, dueDate: subtaskDate || null })
      const parent = todos.find(t => t.id === parentId)
      if (parent?.isCompleted) onUpdate({ ...parent, isCompleted: false })
    }
    setPendingSubtaskFor(null)
    setSubtaskDraft('')
    setSubtaskDate('')
  }

  function cancelSubtask() {
    setPendingSubtaskFor(null)
    setSubtaskDraft('')
    setSubtaskDate('')
  }

  function getDescendantIds(id: number): Set<number> {
    const result = new Set<number>([id])
    todos.filter(t => t.parentId === id).forEach(c => getDescendantIds(c.id).forEach(d => result.add(d)))
    return result
  }

  function flatPickerList(parentId: number | null, depth: number, exclude: Set<number>): Array<{ todo: Todo; depth: number }> {
    return todos
      .filter(t => t.parentId === parentId && !exclude.has(t.id))
      .flatMap(t => [{ todo: t, depth }, ...flatPickerList(t.id, depth + 1, exclude)])
  }

  function toggleCollapse(id: number) {
    const next = new Set(collapsed)
    if (next.has(id)) next.delete(id)
    else next.add(id)
    onCollapsedChange(next)
  }

  function renderTodo(todo: Todo, depth: number, siblings: Todo[]): React.ReactNode {
    const children = subtasksOf(todo.id)
    const isCollapsed = collapsed.has(todo.id)
    const sibIdx = siblings.findIndex(s => s.id === todo.id)

    if (visibleIds !== null && !visibleIds.has(todo.id)) {
      return <Fragment key={todo.id}>{children.map(c => renderTodo(c, depth + 1, children))}</Fragment>
    }

    return (
      <Fragment key={todo.id}>
        <TodoItem
          todo={todo}
          onUpdate={onUpdate}
          onDelete={onDelete}
          depth={depth}
          onSubtaskCreate={todo.isCompleted ? () => openSubtask(todo.id) : undefined}
          onOpenComments={onOpenComments}
          onMove={() => setMovingId(todo.id)}
          onMoveUp={sibIdx > 0 ? () => onMove?.(todo.id, 'up') : undefined}
          onMoveDown={sibIdx < siblings.length - 1 ? () => onMove?.(todo.id, 'down') : undefined}
          commentCount={commentCounts[todo.id] ?? 0}
          childStatusCounts={childStatusMap[todo.id]}
          hasChildren={children.length > 0}
          isCollapsed={isCollapsed}
          onToggleCollapse={() => toggleCollapse(todo.id)}
          isActive={activeTodoIds.has(todo.id)}
          onToggleActive={() => onToggleActive?.(todo.id)}
          draggable={!!onReorder}
          isDragging={draggingId === todo.id}
          dropPosition={dropTarget?.id === todo.id ? dropTarget.position : null}
          onDragStart={() => setDraggingId(todo.id)}
          onDragEnd={endDrag}
          onDragOverRow={e => handleRowDragOver(e, todo.id)}
          onDropRow={e => handleRowDrop(e, todo.id)}
          onHandlePointerDown={e => startTouchDrag(todo.id, e)}
        />
        {!isCollapsed && children.map(child => renderTodo(child, depth + 1, children))}
        {!isCollapsed && pendingSubtaskFor === todo.id && (
          <li className="todo-item subtask-item subtask-new">
            <span style={{ width: (depth + 1) * 36, flexShrink: 0 }} />
            <span className="collapse-placeholder" />
            <span className="subtask-indent" />
            <input
              className="edit-input"
              placeholder="Název podúkolu..."
              autoFocus
              value={subtaskDraft}
              onChange={e => setSubtaskDraft(e.target.value)}
              onKeyDown={e => {
                if (e.key === 'Enter') commitSubtask(todo.id)
                if (e.key === 'Escape') cancelSubtask()
              }}
            />
            <input
              type="date"
              className="date-input"
              value={subtaskDate}
              onChange={e => setSubtaskDate(e.target.value)}
              onKeyDown={e => {
                if (e.key === 'Enter') commitSubtask(todo.id)
                if (e.key === 'Escape') cancelSubtask()
              }}
            />
          </li>
        )}
      </Fragment>
    )
  }

  function closeDropdowns() {
    setStatusOpen(false)
    setPrioritaOpen(false)
    setDateOpen(false)
    setListOpen(false)
  }

  return (
    <div className="todo-table" onClick={closeDropdowns}>
      <div className="todo-header">
        <span className="collapse-placeholder" />
        <span />

        <span className="header-cell">
          <span className="header-label">Název</span>
          <div className="name-filter-row">
            <input
              className="filter-input"
              placeholder="Hledat..."
              value={nameFilter}
              onChange={e => onFiltersChange({ nameFilter: e.target.value })}
              onClick={e => e.stopPropagation()}
            />
            <div className="list-filter-wrap">
              <button
                aria-label="Filtrovat dle listu"
                className={`filter-btn${listFilter.size > 0 ? ' filter-btn--active' : ''}`}
                onClick={e => { e.stopPropagation(); setListOpen(o => !o); setStatusOpen(false); setPrioritaOpen(false); setDateOpen(false) }}
              >
                List ▾
              </button>
              {listOpen && (
                <div className="filter-dropdown" role="group" aria-label="List filter" onClick={e => e.stopPropagation()}>
                  {rootTodos.map(t => (
                    <label key={t.id} className="filter-option">
                      <input
                        type="checkbox"
                        checked={listFilter.has(t.id)}
                        onChange={() => toggleList(t.id)}
                      />
                      {t.title}
                    </label>
                  ))}
                  {listFilter.size > 0 && (
                    <button className="date-filter-clear" onClick={() => onFiltersChange({ listFilter: new Set() })}>✕ Vymazat</button>
                  )}
                </div>
              )}
            </div>
          </div>
        </span>

        <span />

        <span className="header-cell">
          <span className="header-label">Status</span>
          <button
            aria-label="Filter Status"
            className={`filter-btn${statusFilter.size < ALL_STATUSES.size ? ' filter-btn--active' : ''}`}
            onClick={e => { e.stopPropagation(); setStatusOpen(o => !o); setPrioritaOpen(false) }}
          >
            ▾
          </button>
          {statusOpen && (
            <div className="filter-dropdown" role="group" aria-label="Status filter options" onClick={e => e.stopPropagation()}>
              {STATUS_OPTIONS.map(opt => (
                <label key={opt.value} className="filter-option">
                  <input
                    type="checkbox"
                    checked={statusFilter.has(opt.value)}
                    onChange={() => toggleStatus(opt.value)}
                  />
                  {opt.label}
                </label>
              ))}
            </div>
          )}
        </span>

        <span className="header-cell" style={{ justifyContent: 'center' }}>
          <span className="header-label">A</span>
        </span>

        <span className="header-cell" style={{ position: 'relative', justifyContent: 'center' }}>
          <span className="header-label">Priorita</span>
          <button
            aria-label="Filter Priorita"
            className={`filter-btn${prioritaExcluded.size > 0 ? ' filter-btn--active' : ''}`}
            onClick={e => { e.stopPropagation(); setPrioritaOpen(o => !o); setStatusOpen(false) }}
          >
            ▾
          </button>
          {prioritaOpen && (
            <div className="filter-dropdown" role="group" aria-label="Priorita filter options" onClick={e => e.stopPropagation()}>
              {allPrioritas.map(p => (
                <label key={p} className="filter-option">
                  <input
                    type="checkbox"
                    checked={!prioritaExcluded.has(p)}
                    onChange={() => togglePriorita(p)}
                  />
                  {p || '(Blank)'}
                </label>
              ))}
            </div>
          )}
        </span>

        <span className="header-cell">
          <span className="header-label">Related</span>
          <input
            className="filter-input"
            placeholder="Hledat..."
            value={relatedFilter}
            onChange={e => onFiltersChange({ relatedFilter: e.target.value })}
            onClick={e => e.stopPropagation()}
          />
        </span>

        <span className="header-cell">
          <span className="header-label">Detail related</span>
          <input
            className="filter-input"
            placeholder="Hledat..."
            value={detailRelatedFilter}
            onChange={e => onFiltersChange({ detailRelatedFilter: e.target.value })}
            onClick={e => e.stopPropagation()}
          />
        </span>

        <span className="header-cell" style={{ position: 'relative' }}>
          <span className="header-label">Calendar</span>
          <button
            aria-label="Filter Datum"
            className={`filter-btn${(dateFrom || dateTo) ? ' filter-btn--active' : ''}`}
            onClick={e => { e.stopPropagation(); setDateOpen(o => !o); setStatusOpen(false); setPrioritaOpen(false) }}
          >
            ▾
          </button>
          {dateOpen && (
            <div className="filter-dropdown filter-dropdown--date" onClick={e => e.stopPropagation()}>
              <div className="date-filter-row">
                <label className="date-filter-label">
                  Od
                  <input type="date" className="date-filter-input" value={dateFrom} onChange={e => onFiltersChange({ dateFrom: e.target.value })} />
                </label>
                <label className="date-filter-label">
                  Do
                  <input type="date" className="date-filter-input" value={dateTo} onChange={e => onFiltersChange({ dateTo: e.target.value })} />
                </label>
              </div>
              {(dateFrom || dateTo) && (
                <button className="date-filter-clear" onClick={() => onFiltersChange({ dateFrom: '', dateTo: '' })}>
                  ✕ Vymazat
                </button>
              )}
            </div>
          )}
        </span>
        <span style={{ width: 60, flexShrink: 0 }} />
      </div>

      <ul className="todo-list">
        {rootTodos.map(todo => renderTodo(todo, 0, rootTodos))}
      </ul>

      {movingId !== null && (() => {
        const movingTodo = todos.find(t => t.id === movingId)!
        const excluded = getDescendantIds(movingId)
        const allItems = flatPickerList(null, 0, excluded)
        const q = normalizeText(moveSearch.trim())
        const items = q ? allItems.filter(({ todo }) => normalizeText(todo.title).includes(q)) : allItems
        function doMove(newParentId: number | null) {
          onUpdate({ ...movingTodo, parentId: newParentId })
          closeMovePicker()
        }
        return (
          <div className="move-overlay" onClick={closeMovePicker}>
            <div className="move-picker" onClick={e => e.stopPropagation()}>
              <div className="move-picker-header">
                <span>Přesunout: <strong>{movingTodo.title}</strong></span>
                <button className="move-picker-close" onClick={closeMovePicker}>✕</button>
              </div>
              <input
                className="move-picker-search"
                placeholder="Hledat složku..."
                autoFocus
                value={moveSearch}
                onChange={e => setMoveSearch(e.target.value)}
                onKeyDown={e => {
                  if (e.key === 'Escape') closeMovePicker()
                  if (e.key === 'Enter' && items.length > 0) doMove(items[0].todo.id)
                }}
              />
              <ul className="move-picker-list">
                {!q && (
                  <li className="move-picker-item move-picker-root" onClick={() => doMove(null)}>
                    ↖ Přesunout na root (bez rodiče)
                  </li>
                )}
                {items.length === 0 ? (
                  <li className="move-picker-empty">Nic nenalezeno</li>
                ) : items.map(({ todo, depth }) => (
                  <li
                    key={todo.id}
                    className="move-picker-item"
                    style={{ paddingLeft: q ? 12 : 12 + depth * 28 }}
                    onClick={() => doMove(todo.id)}
                  >
                    {todo.title}
                  </li>
                ))}
              </ul>
            </div>
          </div>
        )
      })()}
    </div>
  )
}
