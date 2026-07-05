import { useRef, useState } from 'react'
import type { TodoItem as Todo } from '../types'
import type { DropPosition } from '../api/todos'
import { isOverdue as checkOverdue } from '../utils/isOverdue'
import { fetchComments } from '../api/comments'
import { buildGoogleCalendarUrl, combineDateTime } from '../utils/googleCalendarUrl'
import CalendarEventModal from './CalendarEventModal'

interface Props {
  todo: Todo
  onUpdate: (todo: Todo) => void
  onDelete: (id: number) => void
  onSubtaskCreate?: () => void
  onOpenComments?: (id: number) => void
  onMove?: () => void
  onMoveUp?: () => void
  onMoveDown?: () => void
  commentCount?: number
  childStatusCounts?: Record<string, number>
  depth?: number
  hasChildren?: boolean
  isCollapsed?: boolean
  onToggleCollapse?: () => void
  isActive?: boolean
  onToggleActive?: () => void
  // Drag-and-drop reorder
  draggable?: boolean
  isDragging?: boolean
  dropPosition?: DropPosition | null
  onDragStart?: () => void
  onDragEnd?: () => void
  onDragOverRow?: (e: React.DragEvent) => void
  onDropRow?: (e: React.DragEvent) => void
  // Touch/pen drag start (Pointer Events) — native HTML5 DnD is mouse-only.
  onHandlePointerDown?: (e: React.PointerEvent) => void
}

const STATUS_BG: Record<string, string> = {
  '':           '#e5e7eb',
  'in-process': '#fde68a',
  'done':       '#a7f3d0',
  'failed':     '#fecaca',
  'on_hold':    '#bfdbfe',
}
const STATUS_ORDER = ['done', 'in-process', 'on_hold', 'failed', '']

function selectBg(counts: Record<string, number> | undefined): string | undefined {
  if (!counts) return undefined
  const total = Object.values(counts).reduce((a, b) => a + b, 0)
  if (total === 0) return undefined
  if (!Object.entries(counts).some(([s, c]) => s !== '' && c > 0)) return undefined

  const stops: string[] = []
  let pos = 0
  for (const status of STATUS_ORDER) {
    const count = counts[status] ?? 0
    if (count === 0) continue
    const pct = (count / total) * 100
    const color = STATUS_BG[status]
    stops.push(`${color} ${pos.toFixed(2)}%`)
    pos += pct
    stops.push(`${color} ${pos.toFixed(2)}%`)
  }
  if (stops.length === 2) return STATUS_BG[Object.keys(counts).find(s => s !== '' && (counts[s] ?? 0) > 0) ?? '']
  return `linear-gradient(to right, ${stops.join(', ')})`
}

const STATUS_OPTIONS = [
  { value: '',           label: '' },
  { value: 'in-process', label: 'In Process' },
  { value: 'on_hold',    label: 'On Hold' },
  { value: 'done',       label: 'Done' },
  { value: 'failed',     label: 'Failed' },
]

function InlineCell({ value, placeholder, onSave, center }: {
  value: string
  placeholder?: string
  onSave: (v: string) => void
  center?: boolean
}) {
  const [editing, setEditing] = useState(false)
  const [draft, setDraft] = useState(value)

  function commit() {
    const trimmed = draft.trim()
    onSave(trimmed)
    setEditing(false)
  }

  if (editing) {
    return (
      <input
        className="edit-input extra-cell-input"
        value={draft}
        autoFocus
        onChange={e => setDraft(e.target.value)}
        onBlur={commit}
        onKeyDown={e => {
          if (e.key === 'Enter') commit()
          if (e.key === 'Escape') { setDraft(value); setEditing(false) }
        }}
      />
    )
  }

  return (
    <span
      className={`extra-cell${!value ? ' extra-cell--empty' : ''}`}
      style={center ? { textAlign: 'center' } : undefined}
      onDoubleClick={() => { setDraft(value); setEditing(true) }}
    >
      {value || placeholder}
    </span>
  )
}

export default function TodoItem({ todo, onUpdate, onDelete, onSubtaskCreate, onOpenComments, onMove, onMoveUp, onMoveDown, commentCount = 0, childStatusCounts, depth = 0, hasChildren, isCollapsed, onToggleCollapse, isActive = false, onToggleActive, draggable = false, isDragging = false, dropPosition = null, onDragStart, onDragEnd, onDragOverRow, onDropRow, onHandlePointerDown }: Props) {
  // Only the drag handle initiates a drag; rows aren't draggable by default so
  // text selection and double-click-to-edit keep working everywhere else.
  const [handleHeld, setHandleHeld] = useState(false)
  const [editing, setEditing] = useState(false)
  const [draft, setDraft] = useState(todo.title)
  const [editingDate, setEditingDate] = useState(false)
  const cancelDateRef = useRef(false)
  // Holds the just-set date ("YYYY-MM-DD") while the "add to Google Calendar" modal is open.
  const [calendarDate, setCalendarDate] = useState<string | null>(null)

  async function handleCalendarConfirm(startTime: string, durationMinutes: number) {
    if (!calendarDate) return
    let description = ''
    try {
      const comments = await fetchComments(todo.id)
      description = comments.map(c => c.text).filter(Boolean).join('\n\n')
    } catch {
      // Still create the event even if comments can't be loaded.
    }
    const url = buildGoogleCalendarUrl({
      title: todo.title,
      description,
      start: combineDateTime(calendarDate, startTime),
      durationMinutes,
    })
    window.open(url, '_blank')
    setCalendarDate(null)
  }

  function commitEdit() {
    const trimmed = draft.trim()
    if (trimmed && trimmed !== todo.title) {
      onUpdate({ ...todo, title: trimmed })
    } else {
      setDraft(todo.title)
    }
    setEditing(false)
  }

  function handleKeyDown(e: React.KeyboardEvent) {
    if (e.key === 'Enter') commitEdit()
    if (e.key === 'Escape') { setDraft(todo.title); setEditing(false) }
  }

  const isOverdue = !todo.isCompleted && checkOverdue(todo.dueDate)

  const dueDateLabel = todo.dueDate
    ? new Date(todo.dueDate.split('T')[0] + 'T00:00:00').toLocaleDateString()
    : null

  const statusBackground = selectBg(childStatusCounts)

  const dropClass = dropPosition ? ` drop-${dropPosition}` : ''

  return (
    <li
      className={`todo-item${todo.isCompleted ? ' completed' : ''}${depth > 0 ? ' subtask-item' : ''}${isDragging ? ' is-dragging' : ''}${dropClass}`}
      data-todo-id={todo.id}
      draggable={draggable && handleHeld}
      onDragStart={e => { e.dataTransfer.effectAllowed = 'move'; onDragStart?.() }}
      onDragEnd={() => { setHandleHeld(false); onDragEnd?.() }}
      onDragOver={onDragOverRow}
      onDrop={onDropRow}
    >
      {depth > 0 && <span style={{ width: depth * 20, flexShrink: 0 }} />}
      {draggable && (
        <span
          className="drag-handle"
          aria-label="Přetáhnout"
          title="Přetáhnout pro přesun"
          onMouseDown={() => setHandleHeld(true)}
          onMouseUp={() => setHandleHeld(false)}
          onPointerDown={onHandlePointerDown}
        >
          ⠿
        </span>
      )}
      {hasChildren ? (
        <button
          className="collapse-btn"
          aria-label={isCollapsed ? 'Expand subtasks' : 'Collapse subtasks'}
          onClick={e => { e.stopPropagation(); onToggleCollapse?.() }}
        >
          {isCollapsed ? '>' : 'v'}
        </button>
      ) : (
        <span className="collapse-placeholder" />
      )}

      <input
        type="checkbox"
        checked={todo.isCompleted}
        onChange={e => onUpdate({ ...todo, isCompleted: e.target.checked })}
      />

      {editing ? (
        <input
          className="edit-input"
          value={draft}
          autoFocus
          onChange={e => setDraft(e.target.value)}
          onBlur={commitEdit}
          onKeyDown={handleKeyDown}
        />
      ) : (
        <span
          className="todo-title"
          onDoubleClick={() => {
            if (todo.isCompleted && onSubtaskCreate) onSubtaskCreate()
            else setEditing(true)
          }}
        >
          {todo.title}
        </span>
      )}

      <span className="row-mid-actions">
        <button className="order-btn" aria-label="Posunout nahoru" disabled={!onMoveUp} onClick={() => onMoveUp?.()}>↑</button>
        <button className="order-btn" aria-label="Posunout dolů" disabled={!onMoveDown} onClick={() => onMoveDown?.()}>↓</button>
        <button
          className={`comments-btn${commentCount > 0 ? ' comments-btn--active' : ''}`}
          aria-label="Komentáře"
          onClick={() => onOpenComments?.(todo.id)}
        >
          💬{commentCount > 0 && <span className="comments-count">{commentCount}</span>}
        </button>
      </span>

      <select
        className={`status-select ${todo.status ? `status-${todo.status}` : 'status-empty'}`}
        style={statusBackground ? { background: statusBackground } : undefined}
        value={todo.status}
        onChange={e => onUpdate({ ...todo, status: e.target.value as Todo['status'] })}
      >
        {STATUS_OPTIONS.map(opt => (
          <option key={opt.value} value={opt.value}>{opt.label}</option>
        ))}
      </select>

      <button
        className={`active-btn${isActive ? ' active-btn--on' : ''}`}
        aria-label={isActive ? 'Ukončit sledování' : 'Spustit sledování'}
        onClick={() => onToggleActive?.()}
        title={isActive ? 'Aktivní — kliknutím ukončíš' : 'Kliknutím spustíš sledování'}
      />

      <InlineCell
        value={todo.priority}
        placeholder="—"
        center
        onSave={v => onUpdate({ ...todo, priority: v })}
      />

      <InlineCell
        value={todo.related}
        placeholder="—"
        onSave={v => onUpdate({ ...todo, related: v })}
      />

      <InlineCell
        value={todo.detailRelated}
        placeholder="—"
        onSave={v => onUpdate({ ...todo, detailRelated: v })}
      />

      {editingDate ? (
        <input
          type="date"
          className="date-input"
          defaultValue={todo.dueDate || ''}
          autoFocus
          onBlur={e => {
            const value = e.target.value
            if (!cancelDateRef.current) {
              onUpdate({ ...todo, dueDate: value || null })
              // When a (new) date is set, offer to put it in Google Calendar.
              const prev = todo.dueDate ? todo.dueDate.split('T')[0] : ''
              if (value && value !== prev) setCalendarDate(value)
            }
            cancelDateRef.current = false
            setEditingDate(false)
          }}
          onKeyDown={e => {
            if (e.key === 'Enter') e.currentTarget.blur()
            if (e.key === 'Escape') { cancelDateRef.current = true; setEditingDate(false) }
          }}
        />
      ) : dueDateLabel ? (
        <span
          className={`due-badge${isOverdue ? ' overdue' : ''}`}
          onClick={() => setEditingDate(true)}
        >
          {dueDateLabel}
        </span>
      ) : (
        <span className="due-badge due-badge--empty" onClick={() => setEditingDate(true)} />
      )}

      <button className="move-btn" aria-label="Přesunout" onClick={() => onMove?.()}>⇄</button>
      <button className="delete-btn" onClick={() => onDelete(todo.id)}>✕</button>

      {calendarDate && (
        <CalendarEventModal
          title={todo.title}
          onConfirm={handleCalendarConfirm}
          onClose={() => setCalendarDate(null)}
        />
      )}
    </li>
  )
}
