import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import CommentsPanel from './CommentsPanel'
import type { Comment } from '../types'

const noop = () => {}

function makeComment(id: number, text: string, overrides: Partial<Comment> = {}): Comment {
  return { id, todoId: 1, text, attachments: [], createdAt: '2026-01-01T00:00:00Z', ...overrides }
}

describe('CommentsPanel', () => {
  it('renders the todo title in the panel header', () => {
    render(<CommentsPanel todoId={1} todoTitle="My task" comments={[]} onClose={noop} onAddComment={noop} />)
    expect(screen.getByText('My task')).toBeInTheDocument()
  })

  it('renders a close button that calls onClose', async () => {
    const onClose = vi.fn()
    render(<CommentsPanel todoId={1} todoTitle="My task" comments={[]} onClose={onClose} onAddComment={noop} />)
    await userEvent.click(screen.getByRole('button', { name: /close/i }))
    expect(onClose).toHaveBeenCalled()
  })

  it('renders a textarea for writing a new comment', () => {
    render(<CommentsPanel todoId={1} todoTitle="My task" comments={[]} onClose={noop} onAddComment={noop} />)
    expect(screen.getByRole('textbox')).toBeInTheDocument()
  })

  it('renders a send button labelled "Odeslat"', () => {
    render(<CommentsPanel todoId={1} todoTitle="My task" comments={[]} onClose={noop} onAddComment={noop} />)
    expect(screen.getByRole('button', { name: /odeslat/i })).toBeInTheDocument()
  })

  it('renders a file attachment button', () => {
    render(<CommentsPanel todoId={1} todoTitle="My task" comments={[]} onClose={noop} onAddComment={noop} />)
    expect(screen.getByRole('button', { name: /přiložit/i })).toBeInTheDocument()
  })

  it('calls onAddComment with todoId and text when send is clicked', async () => {
    const onAddComment = vi.fn()
    render(<CommentsPanel todoId={1} todoTitle="My task" comments={[]} onClose={noop} onAddComment={onAddComment} />)
    await userEvent.type(screen.getByRole('textbox'), 'Hello world')
    await userEvent.click(screen.getByRole('button', { name: /odeslat/i }))
    expect(onAddComment).toHaveBeenCalledWith(1, 'Hello world', undefined, undefined)
  })

  it('clears the textarea after sending', async () => {
    render(<CommentsPanel todoId={1} todoTitle="My task" comments={[]} onClose={noop} onAddComment={noop} />)
    await userEvent.type(screen.getByRole('textbox'), 'Hello')
    await userEvent.click(screen.getByRole('button', { name: /odeslat/i }))
    expect(screen.getByRole('textbox')).toHaveValue('')
  })

  it('does not call onAddComment when the textarea is empty', async () => {
    const onAddComment = vi.fn()
    render(<CommentsPanel todoId={1} todoTitle="My task" comments={[]} onClose={noop} onAddComment={onAddComment} />)
    await userEvent.click(screen.getByRole('button', { name: /odeslat/i }))
    expect(onAddComment).not.toHaveBeenCalled()
  })

  it('shows an empty state message when there are no comments', () => {
    render(<CommentsPanel todoId={1} todoTitle="My task" comments={[]} onClose={noop} onAddComment={noop} />)
    expect(screen.getByText(/žádné komentáře/i)).toBeInTheDocument()
  })

  it('renders each comment as an article', () => {
    const comments = [makeComment(1, 'First'), makeComment(2, 'Second')]
    render(<CommentsPanel todoId={1} todoTitle="My task" comments={comments} onClose={noop} onAddComment={noop} />)
    expect(screen.getAllByRole('article')).toHaveLength(2)
  })

  it('displays comments newest-first (highest id at top)', () => {
    const comments = [makeComment(1, 'Older'), makeComment(2, 'Newer')]
    render(<CommentsPanel todoId={1} todoTitle="My task" comments={comments} onClose={noop} onAddComment={noop} />)
    const articles = screen.getAllByRole('article')
    expect(articles[0]).toHaveTextContent('Newer')
    expect(articles[1]).toHaveTextContent('Older')
  })

  it('renders an image when the comment has an image attachment', () => {
    const comment = makeComment(1, 'See pic', {
      attachments: [{ id: 1, commentId: 1, path: 'data:image/png;base64,abc', fileName: null, type: 'image', preview: null, sortOrder: 0 }],
    })
    render(<CommentsPanel todoId={1} todoTitle="My task" comments={[comment]} onClose={noop} onAddComment={noop} />)
    expect(screen.getByRole('img')).toBeInTheDocument()
  })

  describe('Delete comment', () => {
    it('renders a delete button on each comment', () => {
      const comments = [makeComment(1, 'First'), makeComment(2, 'Second')]
      render(<CommentsPanel todoId={1} todoTitle="My task" comments={comments} onClose={noop} onAddComment={noop} onDeleteComment={noop} />)
      expect(screen.getAllByRole('button', { name: /smazat komentář/i })).toHaveLength(2)
    })

    it('calls onDeleteComment with the comment id when delete is clicked', async () => {
      const onDeleteComment = vi.fn()
      const comments = [makeComment(7, 'Hello')]
      render(<CommentsPanel todoId={1} todoTitle="My task" comments={comments} onClose={noop} onAddComment={noop} onDeleteComment={onDeleteComment} />)
      await userEvent.click(screen.getByRole('button', { name: /smazat komentář/i }))
      expect(onDeleteComment).toHaveBeenCalledWith(7)
    })
  })
})
