import { useEffect, useRef, useState, type ReactNode } from 'react'
import heic2any from 'heic2any'
import { convertToHtml as mammothConvertToHtml } from 'mammoth/mammoth.browser.min.js'
import * as pdfjsLib from 'pdfjs-dist'
import pdfjsWorkerUrl from 'pdfjs-dist/build/pdf.worker.min.mjs?url'
import type { Comment, CommentAttachment, TaskSession, TaskLog } from '../types'
import { fetchSessions, deleteSession, updateSession } from '../api/taskSessions'
import { fetchLogs } from '../api/taskLogs'
import { normalizeForSearch, findMatchIndex } from '../utils/findMatch'

pdfjsLib.GlobalWorkerOptions.workerSrc = pdfjsWorkerUrl

// --- helpers ---

function roundRect(ctx: CanvasRenderingContext2D, x: number, y: number, w: number, h: number, r: number) {
  ctx.beginPath()
  ctx.moveTo(x + r, y)
  ctx.lineTo(x + w - r, y)
  ctx.quadraticCurveTo(x + w, y, x + w, y + r)
  ctx.lineTo(x + w, y + h - r)
  ctx.quadraticCurveTo(x + w, y + h, x + w - r, y + h)
  ctx.lineTo(x + r, y + h)
  ctx.quadraticCurveTo(x, y + h, x, y + h - r)
  ctx.lineTo(x, y + r)
  ctx.quadraticCurveTo(x, y, x + r, y)
  ctx.closePath()
}

async function generatePdfThumbnail(file: File): Promise<File | undefined> {
  try {
    const arrayBuffer = await file.arrayBuffer()
    const pdf = await pdfjsLib.getDocument({ data: new Uint8Array(arrayBuffer) }).promise
    const page = await pdf.getPage(1)
    const nat = page.getViewport({ scale: 1 })
    const scale = Math.min(240 / nat.width, 320 / nat.height)
    const viewport = page.getViewport({ scale })
    const canvas = document.createElement('canvas')
    canvas.width = viewport.width
    canvas.height = viewport.height
    const ctx = canvas.getContext('2d')!
    ctx.fillStyle = '#fff'
    ctx.fillRect(0, 0, canvas.width, canvas.height)
    await page.render({ canvasContext: ctx, viewport, canvas }).promise
    const blob = await withTimeout(
      new Promise<Blob | null>(resolve => canvas.toBlob(resolve, 'image/jpeg', 0.85)),
      4000,
    )
    if (!blob) return undefined
    return new File([blob], file.name.replace(/\.pdf$/i, '_preview.jpg'), { type: 'image/jpeg' })
  } catch {
    return undefined
  }
}

async function generateDocIcon(file: File): Promise<File | undefined> {
  try {
    const ext = (file.name.split('.').pop() ?? '').toLowerCase()
    const colorMap: Record<string, string> = {
      doc: '#2B579A', docx: '#2B579A',
      xls: '#217346', xlsx: '#217346',
      ppt: '#D24726', pptx: '#D24726',
    }
    const letterMap: Record<string, string> = {
      doc: 'W', docx: 'W',
      xls: 'X', xlsx: 'X',
      ppt: 'P', pptx: 'P',
    }
    const color = colorMap[ext] ?? '#6B7280'
    const letter = letterMap[ext] ?? ext.slice(0, 1).toUpperCase()

    const S = 3 // scale factor — render at 3× for crisp fullscreen display
    const W = 120 * S, H = 156 * S
    const canvas = document.createElement('canvas')
    canvas.width = W; canvas.height = H
    const ctx = canvas.getContext('2d')
    if (!ctx) return undefined

    // page background
    ctx.fillStyle = '#fff'
    roundRect(ctx, 2 * S, 2 * S, W - 4 * S, H - 4 * S, 6 * S)
    ctx.fill()
    ctx.strokeStyle = '#e5e7eb'
    ctx.lineWidth = 1.5 * S
    roundRect(ctx, 2 * S, 2 * S, W - 4 * S, H - 4 * S, 6 * S)
    ctx.stroke()

    // folded corner (top-right)
    const fold = 22 * S
    ctx.fillStyle = '#f3f4f6'
    ctx.beginPath()
    ctx.moveTo(W - fold - 2 * S, 2 * S)
    ctx.lineTo(W - 2 * S, fold + 2 * S)
    ctx.lineTo(W - fold - 2 * S, fold + 2 * S)
    ctx.closePath()
    ctx.fill()
    ctx.strokeStyle = '#e5e7eb'
    ctx.lineWidth = S
    ctx.stroke()

    // colored badge
    const bx = 16 * S, by = 44 * S, bw = (W - 32 * S), bh = 52 * S
    ctx.fillStyle = color
    roundRect(ctx, bx, by, bw, bh, 5 * S)
    ctx.fill()

    // letter
    ctx.fillStyle = '#fff'
    ctx.font = `bold ${Math.round(bh * 0.68)}px Arial, sans-serif`
    ctx.textAlign = 'center'
    ctx.textBaseline = 'middle'
    ctx.fillText(letter, bx + bw / 2, by + bh / 2)

    // extension label
    ctx.fillStyle = '#9ca3af'
    ctx.font = `bold ${11 * S}px Arial, sans-serif`
    ctx.textBaseline = 'bottom'
    ctx.fillText(ext.toUpperCase(), W / 2, H - 6 * S)

    const blob = await withTimeout(
      new Promise<Blob | null>(resolve => canvas.toBlob(resolve, 'image/jpeg', 0.92)),
      4000,
    )
    if (!blob) return undefined
    return new File([blob], file.name + '_icon.jpg', { type: 'image/jpeg' })
  } catch {
    return undefined
  }
}

// --- linkify: turn URLs and local file paths into clickable links ---

// Order of alternatives matters:
// 1. Markdown-style links [label](url)
// 2. Bare http(s) URLs and bare www. domains
// 3. Windows folder paths ending with a trailing backslash (handles spaces) — e.g.
//    \\server\share\My Great Game - video - 2026-02-06 12-55-13\
// 4. Windows paths ending with a file extension (handles spaces)
// 5. Windows paths without spaces
const LINK_RE = /\[([^\]]+)\]\((https?:\/\/[^)]+)\)|((?:https?:\/\/|www\.)[^\s]+)|(?:[A-Za-z]:\\|\\\\)[^\n\r<>"|?*]*\\(?=\s|$)|(?:[A-Za-z]:\\|\\\\)[^\n\r<>"|?*]*\.[a-zA-Z0-9]{1,15}(?=[\s.,;!?)\]>]|$)|(?:[A-Za-z]:\\|\\\\)[^\s<>"|?*\n\r]+/g

function linkify(text: string): ReactNode[] {
  const nodes: ReactNode[] = []
  let last = 0
  let m: RegExpExecArray | null
  LINK_RE.lastIndex = 0
  while ((m = LINK_RE.exec(text)) !== null) {
    let match = m[0]
    if (m.index > last) nodes.push(text.slice(last, m.index))
    if (m[1] !== undefined) {
      // Markdown link: [label](url)
      nodes.push(
        <a key={m.index} href={m[2]} target="_blank" rel="noopener noreferrer" className="comment-link">
          {m[1]}
        </a>
      )
    } else if (m[3] !== undefined) {
      // Bare URL (http(s)://… or www.…) — trim trailing sentence punctuation
      const trailing = match.match(/[.,;:!?)\]]+$/)?.[0] ?? ''
      const url = trailing ? match.slice(0, match.length - trailing.length) : match
      const href = url.startsWith('http') ? url : `https://${url}`
      nodes.push(
        <a key={m.index} href={href} target="_blank" rel="noopener noreferrer" className="comment-link">
          {url}
        </a>
      )
      if (trailing) nodes.push(trailing)
      last = m.index + match.length
      continue
    } else {
      // Local file / folder path — open via backend (Process.Start on the server side).
      // A trailing backslash means it's a folder → show a folder icon.
      const isFolder = match.endsWith('\\')
      nodes.push(
        <a
          key={m.index}
          href="#"
          className="comment-link comment-link--local"
          title={`Otevřít: ${match}`}
          onClick={e => {
            e.preventDefault()
            fetch(`/api/files/open?path=${encodeURIComponent(match)}`).catch(() => {})
          }}
        >
          {isFolder ? '📁' : '📄'} {match}
        </a>
      )
    }
    last = m.index + match.length
  }
  if (last < text.length) nodes.push(text.slice(last))
  return nodes
}

function withTimeout<T>(promise: Promise<T>, ms: number): Promise<T> {
  return Promise.race([
    promise,
    new Promise<never>((_, reject) => setTimeout(() => reject(new Error('timeout')), ms)),
  ])
}

function formatSessionDate(iso: string): string {
  return new Date(iso).toLocaleString('cs-CZ', {
    day: 'numeric', month: 'numeric', year: 'numeric',
    hour: '2-digit', minute: '2-digit',
  })
}

function formatDuration(startedAt: string, endedAt: string | null): string {
  const ms = (endedAt ? new Date(endedAt) : new Date()).getTime() - new Date(startedAt).getTime()
  const totalMin = Math.max(0, Math.floor(ms / 60000))
  const h = Math.floor(totalMin / 60)
  const m = totalMin % 60
  if (h === 0) return `${m} min`
  return m > 0 ? `${h} h ${m} min` : `${h} h`
}

// Convert UTC ISO to value for datetime-local input (local time, no seconds)
function toDateTimeLocal(iso: string | null): string {
  if (!iso) return ''
  const d = new Date(iso)
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`
}

async function generateDocxThumbnail(file: File): Promise<File | undefined> {
  try {
    const arrayBuffer = await file.arrayBuffer()
    const result = await withTimeout(mammothConvertToHtml({ arrayBuffer }), 8000)
    const html = result.value as string
    if (!html.trim()) return undefined

    const htmlDoc = new DOMParser().parseFromString(html, 'text/html')
    const W = 240, H = 320
    const canvas = document.createElement('canvas')
    canvas.width = W; canvas.height = H
    const ctx = canvas.getContext('2d')!

    ctx.fillStyle = '#fff'
    ctx.fillRect(0, 0, W, H)

    const PAD = 12
    const startY = PAD + 8
    let y = startY
    const maxW = W - PAD * 2

    function wrapText(text: string, fontSize: number, bold: boolean, indent = 0) {
      if (y > H - PAD || !text.trim()) return
      ctx.font = `${bold ? 'bold ' : ''}${fontSize}px Arial, sans-serif`
      ctx.fillStyle = '#111'
      const words = text.trim().split(/\s+/)
      let line = ''
      const x0 = PAD + indent
      for (const word of words) {
        const test = line ? line + ' ' + word : word
        if (ctx.measureText(test).width > maxW - indent && line) {
          if (y > H - PAD) return
          ctx.fillText(line, x0, y); y += fontSize * 1.35; line = word
        } else line = test
      }
      if (line && y <= H - PAD) { ctx.fillText(line, x0, y); y += fontSize * 1.35 }
    }

    function walk(node: Node) {
      if (y > H - PAD) return
      if (node.nodeType !== Node.ELEMENT_NODE) return
      const el = node as Element
      const tag = el.tagName.toLowerCase()
      if      (tag === 'h1') { y += 4; wrapText(el.textContent ?? '', 11, true); y += 2 }
      else if (tag === 'h2') { y += 3; wrapText(el.textContent ?? '', 10, true); y += 1 }
      else if (tag === 'h3') { y += 2; wrapText(el.textContent ?? '', 9, true) }
      else if (tag === 'p')  { wrapText(el.textContent ?? '', 8, false); y += 1 }
      else if (tag === 'li') {
        ctx.font = '8px Arial, sans-serif'; ctx.fillStyle = '#555'
        if (y <= H - PAD) ctx.fillText('•', PAD, y)
        wrapText(el.textContent ?? '', 8, false, 10)
      }
      else if (tag === 'tr') {
        const cells = Array.from(el.querySelectorAll('td,th'))
        const cellW = maxW / Math.max(cells.length, 1)
        ctx.font = '7px Arial, sans-serif'; ctx.fillStyle = '#111'
        cells.forEach((cell, i) => {
          if (y <= H - PAD) ctx.fillText((cell.textContent ?? '').trim().slice(0, 28), PAD + i * cellW, y)
        })
        y += 9
      }
      else { el.childNodes.forEach(walk) }
    }

    htmlDoc.body.childNodes.forEach(walk)

    // Nothing rendered → fall back to icon instead of returning a blank white image
    if (y <= startY) return undefined

    ctx.strokeStyle = '#d1d5db'; ctx.lineWidth = 1
    ctx.strokeRect(0.5, 0.5, W - 1, H - 1)

    return new Promise(resolve => {
      canvas.toBlob(b => {
        resolve(b ? new File([b], file.name.replace(/\.docx$/i, '_preview.jpg'), { type: 'image/jpeg' }) : undefined)
      }, 'image/jpeg', 0.88)
    })
  } catch {
    return undefined
  }
}

async function generateTxtThumbnail(file: File): Promise<File | undefined> {
  try {
    const text = await withTimeout(file.text(), 5000)
    if (!text.trim()) return undefined

    const W = 240, H = 320
    const canvas = document.createElement('canvas')
    canvas.width = W; canvas.height = H
    const ctx = canvas.getContext('2d')
    if (!ctx) return undefined

    ctx.fillStyle = '#fff'
    ctx.fillRect(0, 0, W, H)

    const PAD = 10
    const FONT_SIZE = 7.5
    const LINE_H = FONT_SIZE * 1.45
    ctx.font = `${FONT_SIZE}px 'Courier New', Courier, monospace`
    ctx.fillStyle = '#1f2937'

    // Approximate max chars per line based on monospace char width ≈ 0.6× font size
    const maxChars = Math.floor((W - PAD * 2) / (FONT_SIZE * 0.6))
    const lines = text.replace(/\r\n/g, '\n').split('\n')
    let y = PAD + FONT_SIZE

    outer: for (const line of lines) {
      if (!line.trim()) { y += LINE_H; if (y > H - PAD) break; continue }
      let pos = 0
      while (pos < line.length) {
        if (y > H - PAD) break outer
        ctx.fillText(line.slice(pos, pos + maxChars), PAD, y)
        y += LINE_H
        pos += maxChars
      }
    }

    ctx.strokeStyle = '#d1d5db'; ctx.lineWidth = 1
    ctx.strokeRect(0.5, 0.5, W - 1, H - 1)

    const blob = await withTimeout(
      new Promise<Blob | null>(resolve => canvas.toBlob(resolve, 'image/jpeg', 0.88)),
      4000,
    )
    if (!blob) return undefined
    return new File([blob], file.name + '_preview.jpg', { type: 'image/jpeg' })
  } catch {
    return undefined
  }
}

// --- process a single file into a pending item ---

interface PendingItem {
  file: File
  preview?: File
  previewUrl: string | null
  fileType: 'image' | 'video' | 'pdf' | 'docx' | 'txt' | 'file'
}

async function processFile(raw: File): Promise<PendingItem> {
  try {
    const ext = (raw.name.split('.').pop() ?? '').toLowerCase()
    const isHeic = ext === 'heic' || ext === 'heif'
    const isPdf = ext === 'pdf' || raw.type === 'application/pdf'
    const isDocx = ext === 'docx'
    const isTxt = ext === 'txt'
    const isOffice = ['doc', 'xls', 'xlsx', 'ppt', 'pptx'].includes(ext)
    const isVideo = raw.type.startsWith('video/')
    const isImage = !isHeic && raw.type.startsWith('image/')

    if (isHeic) {
      const blob = await heic2any({ blob: raw, toType: 'image/jpeg', quality: 0.85 }) as Blob
      const previewName = raw.name.replace(/\.(heic|heif)$/i, '_preview.jpg')
      const preview = new File([blob], previewName, { type: 'image/jpeg' })
      return { file: raw, preview, previewUrl: URL.createObjectURL(preview), fileType: 'image' }
    } else if (isPdf) {
      const preview = await generatePdfThumbnail(raw)
      return { file: raw, preview, previewUrl: preview ? URL.createObjectURL(preview) : null, fileType: 'pdf' }
    } else if (isDocx) {
      const contentPreview = await generateDocxThumbnail(raw)
      const preview = contentPreview ?? await generateDocIcon(raw)
      const previewUrl = preview ? URL.createObjectURL(preview) : null
      return { file: raw, preview, previewUrl, fileType: 'docx' }
    } else if (isTxt) {
      const preview = await generateTxtThumbnail(raw)
      const previewUrl = preview ? URL.createObjectURL(preview) : null
      return { file: raw, preview, previewUrl, fileType: 'txt' }
    } else if (isOffice) {
      const preview = await generateDocIcon(raw)
      const previewUrl = preview ? URL.createObjectURL(preview) : null
      return { file: raw, preview, previewUrl, fileType: 'file' }
    } else if (isVideo) {
      return { file: raw, preview: undefined, previewUrl: null, fileType: 'video' }
    } else if (isImage) {
      return { file: raw, preview: undefined, previewUrl: URL.createObjectURL(raw), fileType: 'image' }
    } else {
      return { file: raw, preview: undefined, previewUrl: null, fileType: 'file' }
    }
  } catch {
    // Ultimate fallback — never let processFile reject so handlers' finally blocks always run
    return { file: raw, preview: undefined, previewUrl: null, fileType: 'file' }
  }
}

// --- PDF viewer (canvas-based, no iframe) ---

function PdfViewer({ src, query }: { src: string; query?: string }) {
  const containerRef = useRef<HTMLDivElement>(null)
  const [status, setStatus] = useState<'loading' | 'done' | 'error'>('loading')

  useEffect(() => {
    let cancelled = false
    const container = containerRef.current
    if (!container) return

    // clear previous canvases
    container.innerHTML = ''
    setStatus('loading')

    ;(async () => {
      try {
        const pdf = await pdfjsLib.getDocument(src).promise
        const normQuery = query ? normalizeForSearch(query.trim()) : ''
        let matchCanvas: HTMLCanvasElement | null = null

        for (let i = 1; i <= pdf.numPages; i++) {
          if (cancelled) return
          const page = await pdf.getPage(i)
          const viewport = page.getViewport({ scale: 1.8 })
          const canvas = document.createElement('canvas')
          canvas.width = viewport.width
          canvas.height = viewport.height
          canvas.className = 'pdf-viewer-canvas'
          container.appendChild(canvas)
          const ctx = canvas.getContext('2d')!
          await page.render({ canvasContext: ctx, viewport, canvas }).promise
          if (cancelled) return

          // First page whose text contains the query → remember it to jump to.
          if (normQuery && !matchCanvas) {
            const content = await page.getTextContent()
            const pageText = content.items.map(it => ('str' in it ? it.str : '')).join(' ')
            if (normalizeForSearch(pageText).includes(normQuery)) matchCanvas = canvas
          }
        }
        if (!cancelled) setStatus('done')

        // Jump to the matching page once everything is laid out.
        if (matchCanvas && !cancelled) {
          requestAnimationFrame(() => {
            matchCanvas!.scrollIntoView({ behavior: 'smooth', block: 'start' })
            matchCanvas!.classList.add('pdf-page-match')
            setTimeout(() => matchCanvas?.classList.remove('pdf-page-match'), 2000)
          })
        }
      } catch {
        if (!cancelled) setStatus('error')
      }
    })()

    return () => { cancelled = true }
  }, [src, query])

  return (
    <div className="pdf-viewer-scroll" onClick={e => e.stopPropagation()}>
      {status === 'loading' && <div className="pdf-viewer-loading">⏳ Načítám PDF…</div>}
      {status === 'error'   && <div className="pdf-viewer-loading">❌ PDF se nepodařilo načíst.</div>}
      <div ref={containerRef} className="pdf-viewer-pages" />
    </div>
  )
}

// --- DOCX viewer (mammoth → HTML) ---

function DocxViewer({ src, query }: { src: string; query?: string }) {
  const [html, setHtml] = useState<string | null>(null)
  const [status, setStatus] = useState<'loading' | 'done' | 'error'>('loading')
  const contentRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    let cancelled = false
    setStatus('loading')
    setHtml(null)
    ;(async () => {
      try {
        const res = await withTimeout(fetch(src), 10000)
        const arrayBuffer = await res.arrayBuffer()
        const result = await withTimeout(mammothConvertToHtml({ arrayBuffer }), 15000)
        if (!cancelled) { setHtml(result.value || '<p><em>Dokument neobsahuje žádný text.</em></p>'); setStatus('done') }
      } catch {
        if (!cancelled) setStatus('error')
      }
    })()
    return () => { cancelled = true }
  }, [src])

  // After the HTML is in the DOM, find the text node holding the match, wrap it
  // in a <mark>, and scroll to it. The rendered HTML flows continuously (no pages),
  // so jumping to the matching text is the docx equivalent of "open at the page".
  useEffect(() => {
    if (html === null) return
    const root = contentRef.current
    const q = query?.trim()
    if (!root || !q) return

    requestAnimationFrame(() => {
      const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT)
      let node: Node | null
      while ((node = walker.nextNode())) {
        const value = node.nodeValue ?? ''
        const idx = findMatchIndex(value, q)
        if (idx < 0) continue
        // Split the text node and wrap the matched run in a <mark>.
        const after = (node as Text).splitText(idx)
        after.splitText(q.length)
        const mark = document.createElement('mark')
        mark.className = 'viewer-match'
        mark.textContent = after.nodeValue
        after.parentNode?.replaceChild(mark, after)
        mark.scrollIntoView({ behavior: 'smooth', block: 'center' })
        break
      }
    })
  }, [html, query])

  return (
    <div className="docx-viewer-scroll" onClick={e => e.stopPropagation()}>
      {status === 'loading' && <div className="pdf-viewer-loading">⏳ Načítám dokument…</div>}
      {status === 'error'   && <div className="pdf-viewer-loading">❌ Dokument se nepodařilo načíst.</div>}
      {html !== null && (
        <div ref={contentRef} className="docx-viewer-content" dangerouslySetInnerHTML={{ __html: html }} />
      )}
    </div>
  )
}

// --- TXT viewer (plain text in a scrollable pre) ---

function TxtViewer({ src, query }: { src: string; query?: string }) {
  const [text, setText] = useState<string | null>(null)
  const [status, setStatus] = useState<'loading' | 'done' | 'error'>('loading')
  const markRef = useRef<HTMLElement>(null)

  useEffect(() => {
    let cancelled = false
    setStatus('loading')
    setText(null)
    ;(async () => {
      try {
        const res = await withTimeout(fetch(src), 10000)
        const t = await res.text()
        if (!cancelled) { setText(t); setStatus('done') }
      } catch {
        if (!cancelled) setStatus('error')
      }
    })()
    return () => { cancelled = true }
  }, [src])

  // Scroll the highlighted match into view once rendered.
  useEffect(() => {
    if (text !== null && markRef.current) {
      requestAnimationFrame(() =>
        markRef.current?.scrollIntoView({ behavior: 'smooth', block: 'center' }))
    }
  }, [text])

  // Split the text around the first match so we can wrap it in a <mark>.
  const idx = text !== null && query ? findMatchIndex(text, query.trim()) : -1
  const content = text === null ? null
    : idx < 0 ? text
    : <>
        {text.slice(0, idx)}
        <mark ref={markRef} className="viewer-match">{text.slice(idx, idx + query!.trim().length)}</mark>
        {text.slice(idx + query!.trim().length)}
      </>

  return (
    <div className="docx-viewer-scroll" onClick={e => e.stopPropagation()}>
      {status === 'loading' && <div className="pdf-viewer-loading">⏳ Načítám soubor…</div>}
      {status === 'error'   && <div className="pdf-viewer-loading">❌ Soubor se nepodařilo načíst.</div>}
      {text !== null && <pre className="txt-viewer-content">{content}</pre>}
    </div>
  )
}

// --- component ---

interface Props {
  todoId: number
  todoTitle: string
  comments: Comment[]
  onClose: () => void
  onAddComment: (todoId: number, text: string, files?: File[], previews?: (File | undefined)[]) => void
  onDeleteComment?: (commentId: number) => void
  onEditComment?: (commentId: number, text: string) => void
  logRefreshKey?: number
  // Reveal this todo in the list (expand ancestors, scroll to it, flash it).
  onReveal?: (id: number) => void
  // When set (from a search hit), open this attachment's viewer and jump to the
  // place where `query` matches. Call onDocJumpConsumed once handled.
  docJump?: { path: string; query: string } | null
  onDocJumpConsumed?: () => void
}

export default function CommentsPanel({ todoId, todoTitle, comments, onClose, onAddComment, onDeleteComment, onEditComment, logRefreshKey, onReveal, docJump, onDocJumpConsumed }: Props) {
  const [tab, setTab] = useState<'comments' | 'sessions' | 'log'>('comments')
  const [sessions, setSessions] = useState<TaskSession[]>([])
  const [sessionsLoaded, setSessionsLoaded] = useState(false)
  const [logs, setLogs] = useState<TaskLog[]>([])
  const [logsLoaded, setLogsLoaded] = useState(false)

  const [editingSessionId, setEditingSessionId] = useState<number | null>(null)
  const [editStart, setEditStart] = useState('')
  const [editEnd, setEditEnd] = useState('')
  const [editComment, setEditComment] = useState('')

  function startEditSession(s: TaskSession) {
    setEditingSessionId(s.id)
    setEditStart(toDateTimeLocal(s.startedAt))
    setEditEnd(toDateTimeLocal(s.endedAt))
    setEditComment(s.comment ?? '')
  }

  async function saveSessionEdit(id: number) {
    if (!editStart) return
    try {
      const updated = await updateSession(id, {
        startedAt: new Date(editStart).toISOString(),
        endedAt: editEnd ? new Date(editEnd).toISOString() : null,
        comment: editComment.trim() || null,
      })
      setSessions(prev => prev.map(s => s.id === id ? updated : s))
      setEditingSessionId(null)
    } catch { /* ignore */ }
  }

  async function handleDeleteSession(id: number) {
    try {
      await deleteSession(id)
      setSessions(prev => prev.filter(s => s.id !== id))
    } catch { /* ignore */ }
  }

  function switchToLog() { setTab('log') }

  // Auto-refresh log whenever tab is active AND logRefreshKey changes (todo was mutated)
  useEffect(() => {
    if (tab !== 'log') return
    setLogsLoaded(false)
    fetchLogs(todoId)
      .then(data => { setLogs(data); setLogsLoaded(true) })
      .catch(() => setLogsLoaded(true))
  }, [tab, todoId, logRefreshKey])

  function switchToSessions() {
    setTab('sessions')
    if (!sessionsLoaded) {
      fetchSessions(todoId)
        .then(data => { setSessions(data); setSessionsLoaded(true) })
        .catch(() => setSessionsLoaded(true))
    }
  }

  const [draft, setDraft] = useState('')
  const [pendingItems, setPendingItems] = useState<PendingItem[]>([])
  const [converting, setConverting] = useState(false)
  const [convertingLabel, setConvertingLabel] = useState('')
  const [isDragging, setIsDragging] = useState(false)
  const dragCounter = useRef(0)
  const [fullscreenSrc, setFullscreenSrc] = useState<string | null>(null)
  const [fullscreenType, setFullscreenType] = useState<'image' | 'video' | 'pdf' | 'docx' | 'txt'>('image')
  // Search term to jump to inside a document viewer (empty = no jump).
  const [fullscreenQuery, setFullscreenQuery] = useState('')
  const pendingBlobUrl = useRef<string | null>(null)
  const [editingCommentId, setEditingCommentId] = useState<number | null>(null)
  const [editDraft, setEditDraft] = useState('')

  function openFullscreen(src: string, type: 'image' | 'video' | 'pdf' | 'docx' | 'txt', query = '') {
    setFullscreenSrc(src)
    setFullscreenType(type)
    setFullscreenQuery(query)
  }

  function closeFullscreen() {
    if (pendingBlobUrl.current) {
      URL.revokeObjectURL(pendingBlobUrl.current)
      pendingBlobUrl.current = null
    }
    setFullscreenSrc(null)
    setFullscreenQuery('')
  }

  // A search hit inside an attachment asked us to open that file at the match.
  // Map the file extension to a viewer type and open it with the query.
  useEffect(() => {
    if (!docJump) return
    const ext = (docJump.path.split('.').pop() ?? '').toLowerCase()
    const viewType = ext === 'pdf' ? 'pdf' : ext === 'docx' ? 'docx' : ext === 'txt' ? 'txt' : null
    if (viewType) openFullscreen(docJump.path, viewType, docJump.query)
    onDocJumpConsumed?.()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [docJump])

  function openPendingFullscreen(item: PendingItem) {
    // Revoke any previous temporary blob URL
    if (pendingBlobUrl.current) {
      URL.revokeObjectURL(pendingBlobUrl.current)
      pendingBlobUrl.current = null
    }
    if (item.fileType === 'image') {
      // previewUrl is already a blob URL pointing to the image data
      openFullscreen(item.previewUrl!, 'image')
    } else if (item.fileType === 'video') {
      const url = URL.createObjectURL(item.file)
      pendingBlobUrl.current = url
      openFullscreen(url, 'video')
    } else if (item.fileType === 'pdf' || item.fileType === 'docx' || item.fileType === 'txt') {
      const url = URL.createObjectURL(item.file)
      pendingBlobUrl.current = url
      openFullscreen(url, item.fileType)
    }
    // 'file' type (Office icons etc.) — no fullscreen
  }
  const COL_LABELS: Record<string, string> = {
    title: 'Název', isCompleted: 'Dokončeno', status: 'Status',
    dueDate: 'Datum', priority: 'Priorita', related: 'Related', detailRelated: 'Detail related',
  }
  const STATUS_LABELS: Record<string, string> = {
    '': '(prázdné)', 'in-process': 'In Process', 'on_hold': 'On Hold', 'done': 'Done', 'failed': 'Failed',
  }

  function formatLogEvent(log: TaskLog): string {
    let d: Record<string, unknown> = {}
    try { if (log.detail) d = JSON.parse(log.detail) } catch { /* ok */ }
    switch (log.eventType) {
      case 'create': return d.parentTitle ? `Create a task (subtask of: ${d.parentTitle})` : 'Create a task'
      case 'column_change': {
        const col = String(d.column ?? '')
        const label = COL_LABELS[col] ?? col
        if (col === 'isCompleted') return d.to ? 'Označeno jako dokončeno' : 'Označeno jako aktivní'
        if (col === 'status') return `Add into column ${label}: ${STATUS_LABELS[String(d.to ?? '')] ?? String(d.to ?? '(prázdné)')}`
        const val = String(d.to ?? '')
        return `Add into column ${label}${val ? `: ${val}` : ' (prázdné)'}`
      }
      case 'subtask_added':    return `Subtask vytvořen: ${d.title}`
      case 'subtask_moved_in': return `Task "${d.title}" přiřazen sem${d.fromParentTitle ? ` (z: ${d.fromParentTitle})` : ''}`
      case 'subtask_moved_out': return `Task "${d.title}" přesunut${d.toParentTitle ? ` do: ${d.toParentTitle}` : ' na root'}`
      case 'subtask_deleted':  return `Subtask smazán: ${d.title}`
      case 'moved':            return d.toParentTitle ? `Přesunut do: ${d.toParentTitle}` : 'Přesunut na root'
      default:                 return log.eventType
    }
  }

  const fileInputRef = useRef<HTMLInputElement>(null)

  const sorted = [...comments].sort((a, b) => b.id - a.id)

  async function handleFileChange(e: React.ChangeEvent<HTMLInputElement>) {
    const files = Array.from(e.target.files ?? [])
    if (files.length === 0) return
    if (fileInputRef.current) fileInputRef.current.value = ''

    setConverting(true)
    setConvertingLabel(files.length > 1 ? `⏳ Zpracovávám ${files.length} souborů…` : '⏳ Zpracovávám…')

    try {
      const results = await Promise.all(files.map(processFile))
      setPendingItems(prev => [...prev, ...results])
    } finally {
      setConverting(false)
      setConvertingLabel('')
    }
  }

  function clearFile(index: number) {
    setPendingItems(prev => {
      const item = prev[index]
      if (item?.previewUrl) URL.revokeObjectURL(item.previewUrl)
      return prev.filter((_, i) => i !== index)
    })
  }

  function handlePaste(e: React.ClipboardEvent) {
    const fileItems = Array.from(e.clipboardData.items).filter(item => item.kind === 'file')
    if (fileItems.length === 0) return
    e.preventDefault()

    const rawFiles = fileItems
      .map(item => {
        const f = item.getAsFile()
        if (!f) return null
        // clipboard image (screenshot) has no name — give it one
        if (!f.name || f.name === 'image.png') {
          return new File([f], `screenshot-${Date.now()}.png`, { type: f.type || 'image/png' })
        }
        return f
      })
      .filter((f): f is File => f !== null)

    if (rawFiles.length === 0) return

    setConverting(true)
    setConvertingLabel(rawFiles.length > 1 ? `⏳ Zpracovávám ${rawFiles.length} souborů…` : '⏳ Zpracovávám…')
    Promise.all(rawFiles.map(processFile))
      .then(results => setPendingItems(prev => [...prev, ...results]))
      .finally(() => { setConverting(false); setConvertingLabel('') })
  }

  async function handleDrop(e: React.DragEvent) {
    e.preventDefault()
    dragCounter.current = 0
    setIsDragging(false)
    const files = Array.from(e.dataTransfer.files)
    if (files.length === 0) return

    setConverting(true)
    setConvertingLabel(files.length > 1 ? `⏳ Zpracovávám ${files.length} souborů…` : '⏳ Zpracovávám…')
    try {
      const results = await Promise.all(files.map(processFile))
      setPendingItems(prev => [...prev, ...results])
    } finally {
      setConverting(false)
      setConvertingLabel('')
    }
  }

  function handleSend() {
    const text = draft.trim()
    if (!text && pendingItems.length === 0) return

    const files = pendingItems.map(item => item.file)
    const previews = pendingItems.map(item => item.preview)
    onAddComment(todoId, text, files.length > 0 ? files : undefined, previews.length > 0 ? previews : undefined)

    setDraft('')
    pendingItems.forEach(item => { if (item.previewUrl) URL.revokeObjectURL(item.previewUrl) })
    setPendingItems([])
    if (fileInputRef.current) fileInputRef.current.value = ''
  }

  return (
    <>
      {fullscreenSrc && (
        <div className="video-overlay" onClick={closeFullscreen}>
          <button className="video-overlay-close" aria-label="Zavřít" onClick={closeFullscreen}>✕</button>
          {fullscreenType === 'image' && (
            <img src={fullscreenSrc} alt="fullscreen" className="image-overlay-img" onClick={e => e.stopPropagation()} />
          )}
          {fullscreenType === 'video' && (
            <video src={fullscreenSrc} controls autoPlay className="video-overlay-player" onClick={e => e.stopPropagation()} />
          )}
          {fullscreenType === 'pdf' && (
            <>
              <a href={fullscreenSrc} target="_blank" rel="noopener noreferrer"
                className="pdf-overlay-newtab" onClick={e => e.stopPropagation()}>
                ↗ Otevřít v záložce
              </a>
              <PdfViewer src={fullscreenSrc} query={fullscreenQuery} />
            </>
          )}
          {fullscreenType === 'docx' && (
            <>
              <a href={fullscreenSrc} download
                className="pdf-overlay-newtab" onClick={e => e.stopPropagation()}>
                ↓ Stáhnout
              </a>
              <DocxViewer src={fullscreenSrc} query={fullscreenQuery} />
            </>
          )}
          {fullscreenType === 'txt' && (
            <>
              <a href={fullscreenSrc} download
                className="pdf-overlay-newtab" onClick={e => e.stopPropagation()}>
                ↓ Stáhnout
              </a>
              <TxtViewer src={fullscreenSrc} query={fullscreenQuery} />
            </>
          )}
        </div>
      )}

      <div className="comments-panel">
        <div className="comments-panel-header">
          {onReveal ? (
            <button
              className="comments-panel-title comments-panel-title--link"
              title="Zobrazit úkol v seznamu"
              onClick={() => onReveal(todoId)}
            >
              {todoTitle}
            </button>
          ) : (
            <span className="comments-panel-title">{todoTitle}</span>
          )}
          <button className="comments-panel-close" aria-label="Close" onClick={onClose}>✕</button>
        </div>

        <div className="comments-tabs">
          <button
            className={`comments-tab${tab === 'comments' ? ' comments-tab--active' : ''}`}
            onClick={() => setTab('comments')}
          >
            Komentáře{comments.length > 0 ? ` (${comments.length})` : ''}
          </button>
          <button
            className={`comments-tab${tab === 'sessions' ? ' comments-tab--active' : ''}`}
            onClick={switchToSessions}
          >
            Časování{sessionsLoaded && sessions.length > 0 ? ` (${sessions.length})` : ''}
          </button>
          <button
            className={`comments-tab${tab === 'log' ? ' comments-tab--active' : ''}`}
            onClick={switchToLog}
          >
            Log
          </button>
        </div>

        {tab === 'log' ? (
          <div className="sessions-list">
            {!logsLoaded ? (
              <p className="comments-empty">⏳ Načítám…</p>
            ) : logs.length === 0 ? (
              <p className="comments-empty">Žádné záznamy</p>
            ) : (
              logs.map(l => (
                <div key={l.id} className="log-item">
                  <span className="log-item-text">{formatLogEvent(l)}</span>
                  <span className="log-item-date">{formatSessionDate(l.timestamp)}</span>
                </div>
              ))
            )}
          </div>
        ) : tab === 'sessions' ? (
          <div className="sessions-list">
            {!sessionsLoaded ? (
              <p className="comments-empty">⏳ Načítám…</p>
            ) : sessions.length === 0 ? (
              <p className="comments-empty">Žádné záznamy časování</p>
            ) : (
              [...sessions].reverse().map(s => (
                editingSessionId === s.id ? (
                  <div key={s.id} className="session-item session-item--editing">
                    <div className="session-edit-row">
                      <label className="session-edit-label">Začátek</label>
                      <input
                        type="datetime-local"
                        className="session-edit-input"
                        value={editStart}
                        onChange={e => setEditStart(e.target.value)}
                      />
                    </div>
                    <div className="session-edit-row">
                      <label className="session-edit-label">Konec</label>
                      <input
                        type="datetime-local"
                        className="session-edit-input"
                        value={editEnd}
                        onChange={e => setEditEnd(e.target.value)}
                      />
                    </div>
                    <textarea
                      className="session-edit-textarea"
                      placeholder="Komentář…"
                      value={editComment}
                      onChange={e => setEditComment(e.target.value)}
                      onKeyDown={e => { if (e.key === 'Enter' && e.ctrlKey) saveSessionEdit(s.id) }}
                    />
                    <div className="session-edit-actions">
                      <button className="session-edit-cancel" onClick={() => setEditingSessionId(null)}>Zrušit</button>
                      <button className="session-edit-save" onClick={() => saveSessionEdit(s.id)}>Uložit</button>
                    </div>
                  </div>
                ) : (
                  <div key={s.id} className="session-item">
                    <div className="session-item-row">
                      <span className="session-item-date">{formatSessionDate(s.startedAt)}</span>
                      <span className={`session-item-duration${!s.endedAt ? ' session-item-duration--active' : ''}`}>
                        {!s.endedAt ? '▶ probíhá' : formatDuration(s.startedAt, s.endedAt)}
                      </span>
                      <div className="session-item-actions">
                        <button className="session-action-btn" aria-label="Upravit" onClick={() => startEditSession(s)}>✎</button>
                        <button className="session-action-btn session-action-btn--delete" aria-label="Smazat" onClick={() => handleDeleteSession(s.id)}>✕</button>
                      </div>
                    </div>
                    {s.comment && <p className="session-item-comment">{s.comment}</p>}
                  </div>
                )
              ))
            )}
          </div>
        ) : (
        <>
        <div className="comments-list">
          {sorted.length === 0 ? (
            <p className="comments-empty">Žádné komentáře</p>
          ) : (
            sorted.map(c => (
              <article key={c.id} className="comment-item">
                <div className="comment-actions">
                  <button
                    className="comment-edit-btn"
                    aria-label="Upravit komentář"
                    onClick={() => { setEditingCommentId(c.id); setEditDraft(c.text) }}
                  >
                    ✎
                  </button>
                  <button
                    className="comment-delete-btn"
                    aria-label="Smazat komentář"
                    onClick={() => onDeleteComment?.(c.id)}
                  >
                    ✕
                  </button>
                </div>
                {editingCommentId === c.id ? (
                  <textarea
                    className="comment-edit-textarea"
                    value={editDraft}
                    autoFocus
                    onChange={e => setEditDraft(e.target.value)}
                    onKeyDown={e => {
                      if (e.key === 'Escape') { setEditingCommentId(null); setEditDraft('') }
                      if (e.key === 'Enter' && e.ctrlKey) {
                        onEditComment?.(c.id, editDraft.trim())
                        setEditingCommentId(null); setEditDraft('')
                      }
                    }}
                    onBlur={() => {
                      if (editDraft.trim() !== c.text) onEditComment?.(c.id, editDraft.trim())
                      setEditingCommentId(null); setEditDraft('')
                    }}
                  />
                ) : (
                  <p className="comment-text">{linkify(c.text)}</p>
                )}
                {(c.attachments ?? []).map((att: CommentAttachment) => (
                  <div key={att.id} className="comment-attachment">
                    {att.type === 'image' && (
                      <img
                        src={att.path}
                        alt="attachment"
                        className="comment-image comment-image--clickable"
                        onClick={() => openFullscreen(att.path, 'image')}
                      />
                    )}
                    {att.type === 'video' && (
                      <div className="comment-video-wrap">
                        <video src={att.path} controls className="comment-video" />
                        <button
                          className="comment-video-fullscreen"
                          aria-label="Celá obrazovka"
                          onClick={() => openFullscreen(att.path, 'video')}
                        >
                          ⛶
                        </button>
                      </div>
                    )}
                    {att.type === 'file' && (() => {
                      const ext = (att.path.split('.').pop() ?? '').toLowerCase()
                      const isPdf = ext === 'pdf'
                      const isDocx = ext === 'docx'
                      const isTxt = ext === 'txt'
                      const canView = isPdf || isDocx || isTxt
                      const viewType = isPdf ? 'pdf' : isDocx ? 'docx' : isTxt ? 'txt' : null
                      return (
                        <>
                          {att.preview ? (
                            <img
                              src={att.preview}
                              alt="náhled"
                              className="comment-image comment-image--clickable"
                              onClick={() => {
                                if (viewType) openFullscreen(att.path, viewType)
                                else openFullscreen(att.preview!, 'image')
                              }}
                            />
                          ) : canView && viewType ? (
                            <button
                              className="comment-view-btn"
                              onClick={() => openFullscreen(att.path, viewType)}
                            >
                              {isPdf ? '📄 Otevřít PDF' : isTxt ? '📄 Otevřít text' : '📝 Otevřít dokument'}
                            </button>
                          ) : null}
                          <a href={att.path} download={att.fileName ?? undefined} className="comment-file-link">
                            📎 {att.fileName ?? att.path.split('/').pop()}
                          </a>
                        </>
                      )
                    })()}
                  </div>
                ))}
                <span className="comment-date">{new Date(c.createdAt).toLocaleString()}</span>
              </article>
            ))
          )}
        </div>

        <div
          className={`comments-compose${isDragging ? ' comments-compose--drag' : ''}`}
          onDragEnter={e => { e.preventDefault(); dragCounter.current++; setIsDragging(true) }}
          onDragOver={e => { e.preventDefault(); e.dataTransfer.dropEffect = 'copy' }}
          onDragLeave={() => { dragCounter.current--; if (dragCounter.current === 0) setIsDragging(false) }}
          onDrop={handleDrop}
        >
          {isDragging && (
            <div className="comments-drop-overlay">
              <span>Přetáhněte soubory sem</span>
            </div>
          )}
          <textarea
            className="comments-textarea"
            placeholder="Napište komentář... (Ctrl+Enter odeslat, Ctrl+V vložit soubor)"
            value={draft}
            onChange={e => setDraft(e.target.value)}
            onPaste={handlePaste}
            onKeyDown={e => { if (e.key === 'Enter' && e.ctrlKey) { e.preventDefault(); handleSend() } }}
          />
          {converting && <p className="comments-pending-file">{convertingLabel}</p>}
          {!converting && pendingItems.length > 0 && (
            <div className="comments-preview-list">
              {pendingItems.map((item, i) => {
                const canFullscreen = item.fileType === 'image' || item.fileType === 'video' || item.fileType === 'pdf' || item.fileType === 'docx' || item.fileType === 'txt'
                return (
                  <div key={i} className="comments-preview-wrap">
                    {item.previewUrl ? (
                      <img
                        src={item.previewUrl}
                        alt="náhled"
                        className={`comments-preview-img${canFullscreen ? ' comments-preview-img--clickable' : ''}`}
                        onClick={canFullscreen ? () => openPendingFullscreen(item) : undefined}
                      />
                    ) : item.fileType === 'video' ? (
                      <div
                        className="comments-preview-video-placeholder comments-preview-img--clickable"
                        onClick={() => openPendingFullscreen(item)}
                        title={item.file.name}
                      >
                        ▶ {item.file.name}
                      </div>
                    ) : (
                      <span className="comments-pending-file">📎 {item.file.name}</span>
                    )}
                    <button className="comments-preview-clear" onClick={() => clearFile(i)} aria-label="Odebrat soubor">✕</button>
                  </div>
                )
              })}
            </div>
          )}
          <div className="comments-actions">
            <button
              aria-label="Přiložit soubor"
              className="comments-attach-btn"
              onClick={() => fileInputRef.current?.click()}
            >
              Přiložit
            </button>
            <input
              ref={fileInputRef}
              type="file"
              accept="*/*"
              multiple
              style={{ display: 'none' }}
              onChange={handleFileChange}
            />
            <button className="comments-send-btn" onClick={handleSend}>Odeslat</button>
          </div>
        </div>
        </>
        )}
      </div>
    </>
  )
}
