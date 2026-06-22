import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import AddTodoForm from './AddTodoForm'

describe('AddTodoForm', () => {
  it('does not call onAdd when the title is empty', async () => {
    const onAdd = vi.fn()
    render(<AddTodoForm onAdd={onAdd} />)

    await userEvent.click(screen.getByRole('button', { name: /add/i }))

    expect(onAdd).not.toHaveBeenCalled()
  })

  it('calls onAdd with the trimmed title and null dueDate when no date is set', async () => {
    const onAdd = vi.fn()
    render(<AddTodoForm onAdd={onAdd} />)

    await userEvent.type(screen.getByPlaceholderText(/what needs to be done/i), '  Buy milk  ')
    await userEvent.click(screen.getByRole('button', { name: /add/i }))

    expect(onAdd).toHaveBeenCalledWith('Buy milk', null, '')
  })

  it('resets title, date, and status fields after a successful submit', async () => {
    render(<AddTodoForm onAdd={vi.fn()} />)
    const titleInput = screen.getByPlaceholderText(/what needs to be done/i)

    await userEvent.type(titleInput, 'Some task')
    await userEvent.click(screen.getByRole('button', { name: /add/i }))

    expect(titleInput).toHaveValue('')
    expect(screen.getByRole('combobox')).toHaveValue('')
  })

  it('passes the dueDate string when a date is entered', async () => {
    const onAdd = vi.fn()
    render(<AddTodoForm onAdd={onAdd} />)

    await userEvent.type(screen.getByPlaceholderText(/what needs to be done/i), 'Task')
    await userEvent.type(document.querySelector('input[type="date"]')!, '2026-06-01')
    await userEvent.click(screen.getByRole('button', { name: /add/i }))

    expect(onAdd).toHaveBeenCalledWith('Task', '2026-06-01', '')
  })

  it('offers exactly blank, in-process, on-hold, done, and failed as status options', () => {
    render(<AddTodoForm onAdd={vi.fn()} />)
    const options = Array.from(screen.getByRole('combobox').querySelectorAll('option'))
    const values = options.map(o => o.value)

    expect(values).toEqual(['', 'in-process', 'on_hold', 'done', 'failed'])
  })

  it('passes the selected status to onAdd', async () => {
    const onAdd = vi.fn()
    render(<AddTodoForm onAdd={onAdd} />)

    await userEvent.type(screen.getByPlaceholderText(/what needs to be done/i), 'Task')
    await userEvent.selectOptions(screen.getByRole('combobox'), 'in-process')
    await userEvent.click(screen.getByRole('button', { name: /add/i }))

    expect(onAdd).toHaveBeenCalledWith('Task', null, 'in-process')
  })
})
