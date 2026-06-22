import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, fireEvent, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import TodoItem from './TodoItem'
import type { TodoItem as Todo } from '../types'

vi.mock('../api/comments', () => ({
  fetchComments: vi.fn().mockResolvedValue([
    { id: 1, todoId: 1, text: 'První pozn.', createdAt: '', attachments: [] },
  ]),
}))

const noop = () => {}

function makeTodo(overrides: Partial<Todo> = {}): Todo {
  return {
    id: 1,
    title: 'Test todo',
    isCompleted: false,
    status: 'nothing',
    dueDate: null,
    createdAt: '2026-01-01T00:00:00.000Z',
    parentId: null,
    priority: '',
    related: '',
    detailRelated: '',
    ...overrides,
  }
}

describe('TodoItem', () => {
  describe('Rendering', () => {
    it('renders the todo title', () => {
      render(<TodoItem todo={makeTodo({ title: 'Buy milk' })} onUpdate={noop} onDelete={noop} />)
      expect(screen.getByText('Buy milk')).toBeInTheDocument()
    })

    it('adds completed CSS class when isCompleted is true', () => {
      const { container } = render(
        <TodoItem todo={makeTodo({ isCompleted: true })} onUpdate={noop} onDelete={noop} />
      )
      expect(container.querySelector('li')).toHaveClass('completed')
    })

    it('does not add completed CSS class when isCompleted is false', () => {
      const { container } = render(
        <TodoItem todo={makeTodo({ isCompleted: false })} onUpdate={noop} onDelete={noop} />
      )
      expect(container.querySelector('li')).not.toHaveClass('completed')
    })

    it('renders an empty date placeholder when dueDate is null', () => {
      render(<TodoItem todo={makeTodo({ dueDate: null })} onUpdate={noop} onDelete={noop} />)
      const placeholder = document.querySelector('.due-badge--empty')
      expect(placeholder).toBeInTheDocument()
      expect(placeholder).toBeEmptyDOMElement()
    })
  })

  describe('Interactions', () => {
    it('calls onUpdate with isCompleted toggled when checkbox is clicked', async () => {
      const onUpdate = vi.fn()
      const todo = makeTodo({ isCompleted: false })
      render(<TodoItem todo={todo} onUpdate={onUpdate} onDelete={noop} />)

      await userEvent.click(screen.getByRole('checkbox'))

      expect(onUpdate).toHaveBeenCalledWith({ ...todo, isCompleted: true })
    })

    it('calls onDelete with the todo id when delete button is clicked', async () => {
      const onDelete = vi.fn()
      render(<TodoItem todo={makeTodo({ id: 42 })} onUpdate={noop} onDelete={onDelete} />)

      await userEvent.click(screen.getByRole('button', { name: '✕' }))

      expect(onDelete).toHaveBeenCalledWith(42)
    })

    it('calls onUpdate with new status when status dropdown changes', async () => {
      const onUpdate = vi.fn()
      const todo = makeTodo({ status: 'nothing' })
      render(<TodoItem todo={todo} onUpdate={onUpdate} onDelete={noop} />)

      await userEvent.selectOptions(screen.getByRole('combobox'), 'in-process')

      expect(onUpdate).toHaveBeenCalledWith({ ...todo, status: 'in-process' })
    })
  })

  describe('Date editing', () => {
    it('shows a date input when the date badge is clicked', async () => {
      render(<TodoItem todo={makeTodo({ dueDate: '2026-05-25' })} onUpdate={noop} onDelete={noop} />)

      await userEvent.click(screen.getByText(new Date('2026-05-25').toLocaleDateString()))

      expect(document.querySelector('input[type="date"]')).toBeInTheDocument()
    })

    it('calls onUpdate with the new date when the date input is blurred', async () => {
      const onUpdate = vi.fn()
      const todo = makeTodo({ dueDate: '2026-05-25' })
      render(<TodoItem todo={todo} onUpdate={onUpdate} onDelete={noop} />)

      await userEvent.click(screen.getByText(new Date('2026-05-25').toLocaleDateString()))
      const input = document.querySelector('input[type="date"]') as HTMLInputElement
      fireEvent.change(input, { target: { value: '2026-06-01' } })
      fireEvent.blur(input)

      expect(onUpdate).toHaveBeenCalledWith({ ...todo, dueDate: '2026-06-01' })
    })

    it('shows a date input when the empty date placeholder is clicked', async () => {
      render(<TodoItem todo={makeTodo({ dueDate: null })} onUpdate={noop} onDelete={noop} />)

      await userEvent.click(document.querySelector('.due-badge--empty')!)

      expect(document.querySelector('input[type="date"]')).toBeInTheDocument()
    })

    it('does not call onUpdate when Escape is pressed during date editing', async () => {
      const onUpdate = vi.fn()
      const todo = makeTodo({ dueDate: '2026-05-25' })
      render(<TodoItem todo={todo} onUpdate={onUpdate} onDelete={noop} />)

      await userEvent.click(screen.getByText(new Date('2026-05-25').toLocaleDateString()))
      const input = document.querySelector('input[type="date"]') as HTMLInputElement
      fireEvent.change(input, { target: { value: '2026-06-01' } })
      fireEvent.keyDown(input, { key: 'Escape' })

      expect(onUpdate).not.toHaveBeenCalled()
    })
  })

  describe('Comments button', () => {
    it('renders a comments button', () => {
      render(<TodoItem todo={makeTodo()} onUpdate={noop} onDelete={noop} />)
      expect(screen.getByRole('button', { name: /komentáře/i })).toBeInTheDocument()
    })

    it('calls onOpenComments with the todo id when the comments button is clicked', async () => {
      const onOpenComments = vi.fn()
      render(<TodoItem todo={makeTodo({ id: 7 })} onUpdate={noop} onDelete={noop} onOpenComments={onOpenComments} />)
      await userEvent.click(screen.getByRole('button', { name: /komentáře/i }))
      expect(onOpenComments).toHaveBeenCalledWith(7)
    })

    it('shows no count badge when commentCount is 0', () => {
      render(<TodoItem todo={makeTodo()} onUpdate={noop} onDelete={noop} commentCount={0} />)
      const btn = screen.getByRole('button', { name: /komentáře/i })
      expect(btn).not.toHaveTextContent(/[1-9]/)
    })

    it('shows the count on the button when commentCount > 0', () => {
      render(<TodoItem todo={makeTodo()} onUpdate={noop} onDelete={noop} commentCount={5} />)
      expect(screen.getByRole('button', { name: /komentáře/i })).toHaveTextContent('5')
    })
  })

  describe('Subtask creation', () => {
    it('calls onSubtaskCreate and does not enter edit mode when a completed todo is double-clicked', async () => {
      const onSubtaskCreate = vi.fn()
      render(
        <TodoItem
          todo={makeTodo({ isCompleted: true, title: 'Finished task' })}
          onUpdate={noop}
          onDelete={noop}
          onSubtaskCreate={onSubtaskCreate}
        />
      )

      await userEvent.dblClick(screen.getByText('Finished task'))

      expect(onSubtaskCreate).toHaveBeenCalled()
      expect(screen.queryByRole('textbox')).toBeNull()
    })
  })

  describe('Inline editing', () => {
    it('shows an input when the title is double-clicked', async () => {
      render(<TodoItem todo={makeTodo({ title: 'Original' })} onUpdate={noop} onDelete={noop} />)

      await userEvent.dblClick(screen.getByText('Original'))

      expect(screen.getByRole('textbox', { name: '' })).toBeInTheDocument()
    })

    it('calls onUpdate with the new title when Enter is pressed', async () => {
      const onUpdate = vi.fn()
      const todo = makeTodo({ title: 'Original' })
      render(<TodoItem todo={todo} onUpdate={onUpdate} onDelete={noop} />)

      await userEvent.dblClick(screen.getByText('Original'))
      const input = screen.getByDisplayValue('Original')
      await userEvent.clear(input)
      await userEvent.type(input, 'Updated{Enter}')

      expect(onUpdate).toHaveBeenCalledWith({ ...todo, title: 'Updated' })
    })

    it('does not call onUpdate and reverts when Escape is pressed', async () => {
      const onUpdate = vi.fn()
      render(<TodoItem todo={makeTodo({ title: 'Original' })} onUpdate={onUpdate} onDelete={noop} />)

      await userEvent.dblClick(screen.getByText('Original'))
      const input = screen.getByDisplayValue('Original')
      await userEvent.clear(input)
      await userEvent.type(input, 'Changed{Escape}')

      expect(onUpdate).not.toHaveBeenCalled()
      expect(screen.getByText('Original')).toBeInTheDocument()
    })

    it('commits the edit when the input loses focus', async () => {
      const onUpdate = vi.fn()
      const todo = makeTodo({ title: 'Original' })
      render(<TodoItem todo={todo} onUpdate={onUpdate} onDelete={noop} />)

      await userEvent.dblClick(screen.getByText('Original'))
      const input = screen.getByDisplayValue('Original')
      await userEvent.clear(input)
      await userEvent.type(input, 'Blurred')
      fireEvent.blur(input)

      expect(onUpdate).toHaveBeenCalledWith({ ...todo, title: 'Blurred' })
    })

    it('does not call onUpdate when the edit is cleared to blank', async () => {
      const onUpdate = vi.fn()
      render(<TodoItem todo={makeTodo({ title: 'Original' })} onUpdate={onUpdate} onDelete={noop} />)

      await userEvent.dblClick(screen.getByText('Original'))
      const input = screen.getByDisplayValue('Original')
      await userEvent.clear(input)
      fireEvent.blur(input)

      expect(onUpdate).not.toHaveBeenCalled()
      expect(screen.getByText('Original')).toBeInTheDocument()
    })
  })

  describe('Extra text columns (priorita, related, detail_related)', () => {
    it('renders the priority value', () => {
      render(<TodoItem todo={makeTodo({ priority: 'High' })} onUpdate={noop} onDelete={noop} />)
      expect(screen.getByText('High')).toBeInTheDocument()
    })

    it('shows priority input on double-click and saves on Enter', async () => {
      const onUpdate = vi.fn()
      const todo = makeTodo({ priority: 'Low' })
      render(<TodoItem todo={todo} onUpdate={onUpdate} onDelete={noop} />)
      await userEvent.dblClick(screen.getByText('Low'))
      const input = screen.getByDisplayValue('Low')
      await userEvent.clear(input)
      await userEvent.type(input, 'High{Enter}')
      expect(onUpdate).toHaveBeenCalledWith({ ...todo, priority: 'High' })
    })

    it('renders the related value', () => {
      render(<TodoItem todo={makeTodo({ related: 'PROJ-42' })} onUpdate={noop} onDelete={noop} />)
      expect(screen.getByText('PROJ-42')).toBeInTheDocument()
    })

    it('shows related input on double-click and saves on Enter', async () => {
      const onUpdate = vi.fn()
      const todo = makeTodo({ related: 'OLD' })
      render(<TodoItem todo={todo} onUpdate={onUpdate} onDelete={noop} />)
      await userEvent.dblClick(screen.getByText('OLD'))
      const input = screen.getByDisplayValue('OLD')
      await userEvent.clear(input)
      await userEvent.type(input, 'NEW{Enter}')
      expect(onUpdate).toHaveBeenCalledWith({ ...todo, related: 'NEW' })
    })

    it('renders the detailRelated value', () => {
      render(<TodoItem todo={makeTodo({ detailRelated: 'note xyz' })} onUpdate={noop} onDelete={noop} />)
      expect(screen.getByText('note xyz')).toBeInTheDocument()
    })

    it('shows detailRelated input on double-click and saves on Enter', async () => {
      const onUpdate = vi.fn()
      const todo = makeTodo({ detailRelated: 'old note' })
      render(<TodoItem todo={todo} onUpdate={onUpdate} onDelete={noop} />)
      await userEvent.dblClick(screen.getByText('old note'))
      const input = screen.getByDisplayValue('old note')
      await userEvent.clear(input)
      await userEvent.type(input, 'new note{Enter}')
      expect(onUpdate).toHaveBeenCalledWith({ ...todo, detailRelated: 'new note' })
    })
  })

  describe('Overdue badge', () => {
    // Fix "today" to 2026-04-18 so tests are deterministic
    const FIXED_NOW = new Date('2026-04-18T12:00:00.000Z')
    let dateSpy: ReturnType<typeof vi.spyOn>

    beforeEach(() => {
      dateSpy = vi.spyOn(global, 'Date').mockImplementation((...args) => {
        if (args.length === 0) return new RealDate(FIXED_NOW)
        // @ts-expect-error forwarding args
        return new RealDate(...args)
      })
    })

    afterEach(() => {
      dateSpy.mockRestore()
    })

    const RealDate = Date

    it('shows the actual date (not the word Overdue) with overdue class when past due', () => {
      const yesterday = '2026-04-17'
      const todo = makeTodo({ dueDate: yesterday })

      render(<TodoItem todo={todo} onUpdate={noop} onDelete={noop} />)

      const badge = screen.getByText(new Date(yesterday).toLocaleDateString())
      expect(badge).toBeInTheDocument()
      expect(badge).toHaveClass('overdue')
      expect(screen.queryByText(/overdue/i)).toBeNull()
    })

    it('does not show Overdue badge when due date is today', () => {
      const today = '2026-04-18'
      const todo = makeTodo({ dueDate: today })

      render(<TodoItem todo={todo} onUpdate={noop} onDelete={noop} />)

      // The due date badge is present but without overdue class
      const badge = screen.getByText(new Date(today).toLocaleDateString())
      expect(badge).not.toHaveClass('overdue')
    })

    it('does not show Overdue badge when todo is completed even if past due', () => {
      const yesterday = '2026-04-17'
      const todo = makeTodo({ dueDate: yesterday, isCompleted: true })

      render(<TodoItem todo={todo} onUpdate={noop} onDelete={noop} />)

      const badge = screen.getByText(new Date(yesterday).toLocaleDateString())
      expect(badge).not.toHaveClass('overdue')
    })
  })

  describe('Calendar event', () => {
    function setDate(value: string) {
      const input = document.querySelector('input[type="date"]') as HTMLInputElement
      fireEvent.change(input, { target: { value } })
      fireEvent.blur(input)
    }

    it('opens a calendar modal after a non-empty date is set', async () => {
      render(<TodoItem todo={makeTodo({ title: 'Schůzka', dueDate: null })} onUpdate={noop} onDelete={noop} />)
      await userEvent.click(document.querySelector('.due-badge--empty')!)
      setDate('2026-06-01')
      expect(await screen.findByRole('button', { name: /přidat do kalendáře/i })).toBeInTheDocument()
    })

    it('opens a prefilled Google Calendar URL on confirm (title, date+time, comments)', async () => {
      const openSpy = vi.spyOn(window, 'open').mockImplementation(() => null)
      render(<TodoItem todo={makeTodo({ id: 1, title: 'Schůzka', dueDate: null })} onUpdate={noop} onDelete={noop} />)
      await userEvent.click(document.querySelector('.due-badge--empty')!)
      setDate('2026-06-01')
      await userEvent.click(await screen.findByRole('button', { name: /přidat do kalendáře/i }))

      await waitFor(() => expect(openSpy).toHaveBeenCalled())
      const url = openSpy.mock.calls[0][0] as string
      expect(url).toContain('calendar.google.com/calendar/render')
      expect(url).toContain('dates=20260601T090000/20260601T100000') // default 09:00 + 60 min
      expect(url).toContain(`text=${encodeURIComponent('Schůzka')}`)
      expect(url).toContain(`details=${encodeURIComponent('První pozn.')}`)
    })

    it('does not open a modal when the date is cleared', async () => {
      render(<TodoItem todo={makeTodo({ title: 'Schůzka', dueDate: '2026-05-25' })} onUpdate={noop} onDelete={noop} />)
      await userEvent.click(screen.getByText(new Date('2026-05-25').toLocaleDateString()))
      setDate('')
      expect(screen.queryByRole('button', { name: /přidat do kalendáře/i })).not.toBeInTheDocument()
    })
  })
})
