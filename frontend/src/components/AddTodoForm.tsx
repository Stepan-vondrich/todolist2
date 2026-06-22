import { useState } from 'react'

interface Props {
  onAdd: (title: string, dueDate: string | null, status: string) => void
}

export default function AddTodoForm({ onAdd }: Props) {
  const [title, setTitle] = useState('')
  const [dueDate, setDueDate] = useState('')
  const [status, setStatus] = useState('')

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
    <form className="add-form" onSubmit={handleSubmit}>
      <input
        className="add-input"
        type="text"
        placeholder="What needs to be done?"
        value={title}
        onChange={e => setTitle(e.target.value)}
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
  )
}
