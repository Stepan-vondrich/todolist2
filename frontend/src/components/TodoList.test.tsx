import { useState } from 'react'
import { describe, it, expect, vi } from 'vitest'
import { render, screen, within, fireEvent } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import TodoList from './TodoList'
import type { TodoItem as Todo, FilterState } from '../types'

const noop = () => {}

const ALL_STATUSES = ['', 'in-process', 'on_hold', 'done', 'failed']

function makeTodo(id: number, title: string, overrides: Partial<Todo> = {}): Todo {
  return { id, title, isCompleted: false, status: '', dueDate: null, createdAt: '2026-01-01T00:00:00Z', parentId: null, priority: '', related: '', detailRelated: '', ...overrides }
}

// TodoList is a controlled component — filter and collapse state live in the parent
// (App). This harness owns that state so filter/collapse interactions behave the same
// as in the real app, and provides the required props with sensible defaults.
type ListProps = Omit<Parameters<typeof TodoList>[0], 'filters' | 'onFiltersChange' | 'collapsed' | 'onCollapsedChange'>

function Harness(props: ListProps) {
  const [filters, setFilters] = useState<FilterState>(() => ({
    nameFilter: '',
    listFilter: new Set<number>(),
    statusFilter: new Set(ALL_STATUSES),
    prioritaExcluded: new Set<string>(),
    relatedFilter: '',
    detailRelatedFilter: '',
    dateFrom: '',
    dateTo: '',
    activityFrom: '',
    activityTo: '',
    activityTypes: new Set(['created', 'modified', 'commented']),
  }))
  const [collapsed, setCollapsed] = useState<Set<number>>(() => new Set())
  return (
    <TodoList
      {...props}
      filters={filters}
      onFiltersChange={patch => setFilters(prev => ({ ...prev, ...patch }))}
      collapsed={collapsed}
      onCollapsedChange={setCollapsed}
    />
  )
}

function renderList(props: ListProps) {
  return render(<Harness {...props} />)
}

// The Name, Related and Detail-related columns all use a "Hledat..." placeholder, so
// scope queries to the Název column's filter input specifically.
function nameFilterInput(): HTMLInputElement {
  return document.querySelector('.name-filter-row input.filter-input') as HTMLInputElement
}

describe('TodoList', () => {
  it('renders column headers: Název, Calendar, Status', () => {
    renderList({ todos: [makeTodo(1, 'Any')], onUpdate: noop, onDelete: noop })
    expect(screen.getByText('Název')).toBeInTheDocument()
    expect(screen.getByText('Calendar')).toBeInTheDocument()
    expect(screen.getByText('Status')).toBeInTheDocument()
  })

  it('shows the empty state message when there are no todos', () => {
    renderList({ todos: [], onUpdate: noop, onDelete: noop })
    expect(screen.getByText(/no todos yet/i)).toBeInTheDocument()
  })

  it('renders one list item per todo', () => {
    const todos = [makeTodo(1, 'First'), makeTodo(2, 'Second'), makeTodo(3, 'Third')]
    renderList({ todos, onUpdate: noop, onDelete: noop })
    expect(screen.getAllByRole('listitem')).toHaveLength(3)
  })

  describe('Status filter', () => {
    const todos = [
      makeTodo(1, 'Alpha', { status: '' }),
      makeTodo(2, 'Beta',  { status: 'done' }),
      makeTodo(3, 'Gamma', { status: 'in-process' }),
      makeTodo(4, 'Delta', { status: 'failed' }),
    ]

    async function openStatusFilter() {
      await userEvent.click(screen.getByRole('button', { name: /filter status/i }))
    }

    it('shows a filter button on the Status column header', () => {
      renderList({ todos, onUpdate: noop, onDelete: noop })
      expect(screen.getByRole('button', { name: /filter status/i })).toBeInTheDocument()
    })

    it('opens a dropdown with one checkbox per status value when the filter button is clicked', async () => {
      renderList({ todos, onUpdate: noop, onDelete: noop })
      await openStatusFilter()

      expect(screen.getByRole('checkbox', { name: /\(blank\)/i })).toBeInTheDocument()
      expect(screen.getByRole('checkbox', { name: /in process/i })).toBeInTheDocument()
      expect(screen.getByRole('checkbox', { name: /done/i })).toBeInTheDocument()
      expect(screen.getByRole('checkbox', { name: /failed/i })).toBeInTheDocument()
    })

    it('all status checkboxes are checked by default', async () => {
      renderList({ todos, onUpdate: noop, onDelete: noop })
      await openStatusFilter()

      const group = screen.getByRole('group', { name: /status filter options/i })
      const checkboxes = within(group).getAllByRole('checkbox')
      checkboxes.forEach(cb => expect(cb).toBeChecked())
    })

    it('hides todos whose status is unchecked', async () => {
      renderList({ todos, onUpdate: noop, onDelete: noop })
      await openStatusFilter()
      await userEvent.click(screen.getByRole('checkbox', { name: /done/i }))

      expect(screen.queryByText('Beta')).toBeNull()
      expect(screen.getByText('Alpha')).toBeInTheDocument()
      expect(screen.getByText('Gamma')).toBeInTheDocument()
      expect(screen.getByText('Delta')).toBeInTheDocument()
    })

    it('can filter by multiple statuses simultaneously', async () => {
      renderList({ todos, onUpdate: noop, onDelete: noop })
      await openStatusFilter()
      await userEvent.click(screen.getByRole('checkbox', { name: /done/i }))
      await userEvent.click(screen.getByRole('checkbox', { name: /in process/i }))

      expect(screen.queryByText('Beta')).toBeNull()
      expect(screen.queryByText('Gamma')).toBeNull()
      expect(screen.getByText('Alpha')).toBeInTheDocument()
      expect(screen.getByText('Delta')).toBeInTheDocument()
    })

    it('restores todos when a status is re-checked', async () => {
      renderList({ todos, onUpdate: noop, onDelete: noop })
      await openStatusFilter()
      await userEvent.click(screen.getByRole('checkbox', { name: /done/i }))
      await userEvent.click(screen.getByRole('checkbox', { name: /done/i }))

      expect(screen.getByText('Beta')).toBeInTheDocument()
    })
  })

  describe('Name filter', () => {
    const todos = [makeTodo(1, 'Buy milk'), makeTodo(2, 'Walk dog'), makeTodo(3, 'Buy bread')]

    it('shows a text input for filtering by name', () => {
      renderList({ todos, onUpdate: noop, onDelete: noop })
      expect(nameFilterInput()).toBeInTheDocument()
    })

    it('hides todos whose title does not match the typed text', async () => {
      renderList({ todos, onUpdate: noop, onDelete: noop })
      await userEvent.type(nameFilterInput(), 'buy')

      expect(screen.getByText('Buy milk')).toBeInTheDocument()
      expect(screen.getByText('Buy bread')).toBeInTheDocument()
      expect(screen.queryByText('Walk dog')).toBeNull()
    })

    it('is case-insensitive', async () => {
      renderList({ todos, onUpdate: noop, onDelete: noop })
      await userEvent.type(nameFilterInput(), 'BUY')

      expect(screen.getByText('Buy milk')).toBeInTheDocument()
      expect(screen.getByText('Buy bread')).toBeInTheDocument()
    })

    it('shows all todos when the search is cleared', async () => {
      renderList({ todos, onUpdate: noop, onDelete: noop })
      const input = nameFilterInput()
      await userEvent.type(input, 'buy')
      await userEvent.clear(input)

      expect(screen.getAllByRole('listitem')).toHaveLength(3)
    })
  })

  describe('Subtask creation', () => {
    it('shows a subtask input below a completed todo when it is double-clicked', async () => {
      const todo = makeTodo(1, 'Done task', { isCompleted: true })
      renderList({ todos: [todo], onUpdate: noop, onDelete: noop })

      await userEvent.dblClick(screen.getByText('Done task'))

      expect(screen.getByPlaceholderText(/podúkol/i)).toBeInTheDocument()
    })

    it('does not show subtask input when an incomplete todo is double-clicked', async () => {
      const todo = makeTodo(1, 'Active task', { isCompleted: false })
      renderList({ todos: [todo], onUpdate: noop, onDelete: noop })

      await userEvent.dblClick(screen.getByText('Active task'))

      expect(screen.queryByPlaceholderText(/podúkol/i)).toBeNull()
    })

    it('calls onAdd with title, parentId and dueDate when Enter is pressed', async () => {
      const todo = makeTodo(1, 'Done task', { isCompleted: true })
      const onAdd = vi.fn()
      renderList({ todos: [todo], onUpdate: noop, onDelete: noop, onAdd })

      await userEvent.dblClick(screen.getByText('Done task'))
      await userEvent.type(screen.getByPlaceholderText(/podúkol/i), 'My subtask{Enter}')

      expect(onAdd).toHaveBeenCalledWith({ title: 'My subtask', parentId: 1, dueDate: null })
    })

    it('shows a date input in the subtask creation row', async () => {
      const todo = makeTodo(1, 'Done task', { isCompleted: true })
      renderList({ todos: [todo], onUpdate: noop, onDelete: noop })

      await userEvent.dblClick(screen.getByText('Done task'))

      expect(document.querySelector('.subtask-new input[type="date"]')).toBeInTheDocument()
    })

    it('passes the typed date to onAdd when submitting a subtask', async () => {
      const todo = makeTodo(1, 'Done task', { isCompleted: true })
      const onAdd = vi.fn()
      renderList({ todos: [todo], onUpdate: noop, onDelete: noop, onAdd })

      await userEvent.dblClick(screen.getByText('Done task'))
      await userEvent.type(screen.getByPlaceholderText(/podúkol/i), 'My subtask')
      const dateInput = document.querySelector('.subtask-new input[type="date"]') as HTMLInputElement
      fireEvent.change(dateInput, { target: { value: '2026-06-01' } })
      await userEvent.keyboard('{Enter}')

      expect(onAdd).toHaveBeenCalledWith({ title: 'My subtask', parentId: 1, dueDate: '2026-06-01' })
    })

    it('hides the subtask input when Escape is pressed', async () => {
      const todo = makeTodo(1, 'Done task', { isCompleted: true })
      renderList({ todos: [todo], onUpdate: noop, onDelete: noop })

      await userEvent.dblClick(screen.getByText('Done task'))
      await userEvent.keyboard('{Escape}')

      expect(screen.queryByPlaceholderText(/podúkol/i)).toBeNull()
    })

    it('renders a saved subtask immediately after its parent with an indent class', () => {
      const parent = makeTodo(1, 'Parent task', { isCompleted: true })
      const child  = makeTodo(2, 'Child task',  { parentId: 1 })
      renderList({ todos: [parent, child], onUpdate: noop, onDelete: noop })

      const items = screen.getAllByRole('listitem')
      expect(items[0]).toHaveTextContent('Parent task')
      expect(items[1]).toHaveTextContent('Child task')
      expect(items[1]).toHaveClass('subtask-item')
    })
  })

  describe('Collapse / expand', () => {
    it('shows a collapse button only for todos that have subtasks', () => {
      const parent = makeTodo(1, 'Parent', { isCompleted: true })
      const child  = makeTodo(2, 'Child',  { parentId: 1 })
      const solo   = makeTodo(3, 'Solo')
      renderList({ todos: [parent, child, solo], onUpdate: noop, onDelete: noop })

      expect(screen.getAllByRole('button', { name: /collapse subtasks/i })).toHaveLength(1)
    })

    it('shows "v" on the button when subtasks are expanded', () => {
      const parent = makeTodo(1, 'Parent', { isCompleted: true })
      const child  = makeTodo(2, 'Child',  { parentId: 1 })
      renderList({ todos: [parent, child], onUpdate: noop, onDelete: noop })

      expect(screen.getByRole('button', { name: /collapse subtasks/i })).toHaveTextContent('v')
    })

    it('hides subtasks and shows ">" after clicking collapse', async () => {
      const parent = makeTodo(1, 'Parent', { isCompleted: true })
      const child  = makeTodo(2, 'Child',  { parentId: 1 })
      renderList({ todos: [parent, child], onUpdate: noop, onDelete: noop })

      await userEvent.click(screen.getByRole('button', { name: /collapse subtasks/i }))

      expect(screen.queryByText('Child')).toBeNull()
      expect(screen.getByRole('button', { name: /expand subtasks/i })).toHaveTextContent('>')
    })

    it('shows subtasks again after clicking expand', async () => {
      const parent = makeTodo(1, 'Parent', { isCompleted: true })
      const child  = makeTodo(2, 'Child',  { parentId: 1 })
      renderList({ todos: [parent, child], onUpdate: noop, onDelete: noop })

      await userEvent.click(screen.getByRole('button', { name: /collapse subtasks/i }))
      await userEvent.click(screen.getByRole('button', { name: /expand subtasks/i }))

      expect(screen.getByText('Child')).toBeInTheDocument()
    })

    it('hides all descendants (sub-subtasks too) when collapsed', async () => {
      const root   = makeTodo(1, 'Root',   { isCompleted: true })
      const sub    = makeTodo(2, 'Sub',    { parentId: 1 })
      const subsub = makeTodo(3, 'SubSub', { parentId: 2 })
      renderList({ todos: [root, sub, subsub], onUpdate: noop, onDelete: noop })

      // Root has children, Sub has children — click the first (root's) button
      await userEvent.click(screen.getAllByRole('button', { name: /collapse subtasks/i })[0])

      expect(screen.queryByText('Sub')).toBeNull()
      expect(screen.queryByText('SubSub')).toBeNull()
    })
  })

  describe('Sub-subtask creation', () => {
    it('shows a subtask input below a completed subtask when double-clicked', async () => {
      const root = makeTodo(1, 'Root task', { isCompleted: true })
      const sub  = makeTodo(2, 'Sub task',  { parentId: 1, isCompleted: true })
      renderList({ todos: [root, sub], onUpdate: noop, onDelete: noop })

      await userEvent.dblClick(screen.getByText('Sub task'))

      expect(screen.getByPlaceholderText(/podúkol/i)).toBeInTheDocument()
    })

    it('calls onAdd with the subtask id as parentId when creating a sub-subtask', async () => {
      const root = makeTodo(1, 'Root task', { isCompleted: true })
      const sub  = makeTodo(2, 'Sub task',  { parentId: 1, isCompleted: true })
      const onAdd = vi.fn()
      renderList({ todos: [root, sub], onUpdate: noop, onDelete: noop, onAdd })

      await userEvent.dblClick(screen.getByText('Sub task'))
      await userEvent.type(screen.getByPlaceholderText(/podúkol/i), 'Sub-sub task{Enter}')

      expect(onAdd).toHaveBeenCalledWith({ title: 'Sub-sub task', parentId: 2, dueDate: null })
    })

    it('renders a sub-subtask more indented than its subtask parent', () => {
      const root   = makeTodo(1, 'Root',   { isCompleted: true })
      const sub    = makeTodo(2, 'Sub',    { parentId: 1 })
      const subsub = makeTodo(3, 'SubSub', { parentId: 2 })
      renderList({ todos: [root, sub, subsub], onUpdate: noop, onDelete: noop })

      // Indentation is rendered as a leading spacer span of width depth*20px inside each row.
      const items = screen.getAllByRole('listitem')
      expect(items[1].querySelector('span')).toHaveStyle({ width: '20px' })
      expect(items[2].querySelector('span')).toHaveStyle({ width: '40px' })
    })
  })
})
