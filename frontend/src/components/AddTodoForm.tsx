import { useRef, useState } from 'react'

interface Props {
  onAdd: (title: string, dueDate: string | null, status: string) => void
}

// Temporary on-screen debug panel — shown ONLY on the dev URL, never on prod.
const DEBUG = typeof location !== 'undefined' && location.hostname.includes('todolist-dev')

export default function AddTodoForm({ onAdd }: Props) {
  const [title, setTitle] = useState('')
  const [dueDate, setDueDate] = useState('')
  const [status, setStatus] = useState('')
  const inputRef = useRef<HTMLInputElement>(null)
  const [dbg, setDbg] = useState('(zatím nic — piš do inputu)')

  function nudgeRepaint() {
    // Force iOS Safari to repaint the input after each keystroke (some compositing
    // setups leave the typed text unpainted until blur).
    const el = inputRef.current
    if (!el) return
    el.style.opacity = '0.999'
    requestAnimationFrame(() => { if (inputRef.current) inputRef.current.style.opacity = '' })
  }

  function onTitleChange(e: React.ChangeEvent<HTMLInputElement>) {
    const v = e.target.value
    setTitle(v)
    nudgeRepaint()
    if (DEBUG) {
      const el = inputRef.current
      const cs = el ? getComputedStyle(el) : null
      const fill = cs ? cs.getPropertyValue('-webkit-text-fill-color') : '?'
      setDbg(
        `react: "${v}"\n` +
        `dom:   "${el ? el.value : '?'}"\n` +
        `len:   ${v.length}\n` +
        `color: ${cs ? cs.color : '?'}\n` +
        `fill:  ${fill || '(none)'}\n` +
        `caret: ${cs ? cs.caretColor : '?'}\n` +
        `opac:  ${cs ? cs.opacity : '?'}`
      )
    }
  }

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    const trimmed = title.trim()
    if (!trimmed) return
    onAdd(trimmed, dueDate || null, status)
    setTitle('')
    setDueDate('')
    setStatus('')
  }

  return (
    <>
      {DEBUG && (
        <div style={{
          position: 'fixed', top: 0, left: 0, right: 0, zIndex: 99999,
          background: '#111', color: '#0f0', font: '12px monospace',
          padding: '6px 10px', whiteSpace: 'pre-wrap', opacity: 0.95,
          borderBottom: '1px solid #0f0',
        }}>
          {'DEBUG add-input\n' + dbg}
        </div>
      )}
      <form className="add-form" onSubmit={handleSubmit}>
        <input
          ref={inputRef}
          className="add-input"
          type="text"
          placeholder="What needs to be done?"
          value={title}
          onChange={onTitleChange}
        />
        <input
          className="date-input"
          type="date"
          value={dueDate}
          onChange={e => setDueDate(e.target.value)}
        />
        <select
          className={`status-select ${status ? `status-${status}` : 'status-empty'}`}
          value={status}
          onChange={e => setStatus(e.target.value)}
        >
          <option value=""></option>
          <option value="in-process">In Process</option>
          <option value="on_hold">On Hold</option>
          <option value="done">Done</option>
          <option value="failed">Failed</option>
        </select>
        <button className="add-btn" type="submit">Add</button>
      </form>
    </>
  )
}
