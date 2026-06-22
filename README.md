# Todo List App

A full-stack todo list application built with **React + TypeScript** (frontend) and **C# ASP.NET Core** (backend), backed by a SQLite database.

---

## Tech Stack

| Layer | Technology |
|---|---|
| Frontend | React 18, TypeScript, Vite |
| Backend | C# ASP.NET Core Web API (.NET 8) |
| Database | SQLite via Entity Framework Core |

---

## Features

### Todos
- **Add todos** with an optional due date and initial status
- **Subtasks** — double-click a completed todo's title to add a subtask; subtasks are indented under their parent; parent is automatically unchecked when a subtask is added
- **Move todos** — ⇄ button opens a picker to re-parent any todo (including to root); the picker has a **search field** (autofocused, diacritics-insensitive) to quickly filter the target list instead of scrolling — Enter selects the first match, Escape closes; the "move to root" option is hidden while searching
- **Collapse/expand** — todos with children have a toggle button to hide/show subtasks
- **Inline editing** — double-click any title to edit in place; Enter or blur to save
- **Status select** — blank / In Process / On Hold / Done / Failed, color-coded per row
- **Time tracking** — **A** column button starts/stops a session timer per todo; stopping opens a small modal for an optional session comment; all sessions are stored with start time, end time, and comment
- **Child status gradient** — the status button shows a proportional horizontal gradient across all descendants' statuses (recursive), so you can see the health of a whole subtree at a glance
- **Extra columns** — Priorita (centered, inline-editable), Related, Detail Related (all inline-editable by double-click)
- **Due date badges** — click to set/change; overdue dates turn red
- **Mark complete** — checkbox; completed state is stored and preserved
- **Reorder** — ↑ ↓ buttons move a todo one position up or down among its siblings (disabled when already first or last)
- **Delete** with the ✕ button (cascades to subtasks)
- **Persistent storage** — SQLite database (`todos.db`), survives restarts

### Comments & Attachments
- **Comments panel** — click 💬 to open a side panel for any todo
- **Multiple attachments** — attach any number of files to a single comment; all are sent together as one comment; text is optional if at least one file is attached
- **HEIC/HEIF support** — Apple's HEIC photos are automatically converted to JPEG for preview in the browser; the original HEIC file is kept and available for download (📎)
- **Drag & drop** — drag files from Explorer or the desktop directly onto the comment compose area; a dashed blue overlay confirms the drop target; files are queued as pending attachments (not sent immediately)
- **Clipboard paste** — paste any file from the clipboard with Ctrl+V: screenshots, images, and files copied in Explorer all work; pasted screenshots are saved as `screenshot-<timestamp>.png`
- **Pending preview** — all attached files are shown as thumbnails in the compose area before sending; click a thumbnail to preview it fullscreen (image lightbox, PDF viewer, DOCX viewer, TXT viewer, video player); click ✕ to remove individual files
- **Image lightbox** — click any image in a sent comment to view it fullscreen; click outside to close
- **PDF viewer** — PDF attachments open in an inline PDF.js viewer; click the thumbnail (or "Otevřít PDF" link) to open
- **DOCX viewer** — DOCX attachments are rendered to HTML via Mammoth and shown in a scrollable in-app viewer
- **TXT viewer** — plain text files get a thumbnail showing the first lines; clicking opens a fullscreen scrollable text viewer with monospace font
- **Video playback** — video attachments play inline with browser controls
- **Office file icons** — DOC, XLS, XLSX, PPT, PPTX and other binary Office files get an auto-generated icon thumbnail with the file extension displayed
- **Web links** — URLs in comment text are auto-detected and rendered as clickable links opening in a new tab: full `http(s)://…` URLs, bare `www.…` domains (prefixed with `https://` automatically), and Markdown-style `[label](url)` links; trailing sentence punctuation is kept out of the link
- **Local file links** — Windows file paths typed or pasted into a comment (e.g. `C:\Users\st\Downloads\report.xlsx`) are automatically detected and rendered as green clickable links; clicking opens the file in its default application (equivalent to double-clicking in Explorer); UNC paths (`\\server\share\...`) are also supported
- **Folder links** — a path ending with a trailing backslash is treated as a folder and gets a 📁 icon; clicking opens it directly in Explorer. Folder paths may contain spaces (e.g. `\\192.168.0.131\Download\My Great Game - video - 2026-02-06 12-55-13\`) — the trailing `\` marks where the path ends, so the whole thing becomes one link
- **Comment count badge** — shows number of comments per todo
- **Delete comments** individually; **edit** comment text inline; **Ctrl+Enter** to send
- **Three tabs in the comments panel:**
  - **Komentáře** — comments with attachments (default)
  - **Časování** — all time-tracking sessions for the todo: start date/time, duration, optional comment; supports inline edit (start, end, comment) and delete
  - **Log** — automatic audit trail: task creation, column changes (title, status, priority, related, due date), subtask added/moved/deleted, task moved; auto-refreshes in real time when changes are made while the panel is open

### Global Search
- **🔍 button** in the top-right header opens a floating search panel
- Searches across **all columns** (title, Related, Detail related, Priorita) and **comment text**
- **Diacritics-insensitive** — "krem" matches "krém", "bater" matches "baterka" etc.
- Results are labeled with a colored badge: **TASK** (purple) for root todos, **SUBTASK** (blue) for nested todos
- Subtask results show the parent name below (↳ parent title) so you know where in the tree it lives
- Comment matches are shown with a **💬 komentář** chip and the matching text; clicking opens the comments panel
- Column matches (Related, Priorita…) are labeled with the field name
- Clicking a result closes the panel and scrolls to the todo with a brief blue highlight
- Minimum 2 characters; 300 ms debounce; close with Escape or click outside

### Filter Bookmarks
- **🔖 Záložky** button sits in a bar above the filter row; click to open the bookmark panel
- In the panel: enter a name, pick a color from the palette, click **Uložit** (or Enter) to save the current filter state as a named bookmark
- **Captures the tree view too** — a bookmark also stores the exact expand/collapse state of every tree down to the deepest subtask, so applying it restores precisely which subtasks were open and which were closed, not just the filters; the panel summary shows `uložené zobrazení` for bookmarks that include a view
- Saved bookmarks appear as **colored chips** to the right of the button — one click restores all filters **and** the saved expand/collapse layout instantly
- Bookmarks created before this feature have no saved view; applying one leaves the current expand/collapse state untouched (only filters change)
- Each bookmark in the panel shows a color dot, the filter summary, and an ✕ to delete; bookmarks are stored in the database and shared across all origins (`:6001`, `:6173`, etc.) — opening the app from any port always shows the same bookmarks

### Filtering
- **Name filter** — substring search on title; next to it a **List ▾** dropdown shows checkboxes for every root-level todo — checking one or more shows only those trees and hides the rest
- **Status filter** — multi-select checkbox dropdown
- **Priorita filter** — exclude specific priority values
- **Related / Detail Related filters** — substring search
- **Date range filter** — filter by due date (Od / Do)
- All filters use **AND logic** across all columns and apply to todos at every level of the tree; non-matching todos are hidden but their matching children are still shown

### Export / Import (Backup)
- **Export** — downloads an encrypted `.backup` file containing all todos, comments, time sessions (with comments), overlaps, activity logs, and optionally uploaded files; encrypted with AES-256-CBC (PBKDF2/SHA-256, 100 000 iterations, random salt + IV); checkbox "Zahrnout přílohy z komentářů" lets you export data only
- **Export časování** — downloads a plain ZIP with two semicolon-separated CSV files (`sessions.csv`, `overlaps.csv`) for analysis in Excel or similar tools; `sessions.csv` includes the optional session `comment` column; no password required
- **Import** has four modes (chosen via radio buttons in the modal):
  - **Nahradit vše** — decrypts the backup and replaces all data including time sessions and overlaps
  - **Přidat nové + odebrat chybějící** — three-way merge: keeps existing records, adds new ones, removes those absent from backup (including sessions/overlaps)
  - **Přidat nové + zachovat vše** — additive only: adds records missing from DB, never deletes anything; supports both `.backup` (encrypted) and `.csv` (todos only, no password)
  - **Importovat časování (.zip)** — imports only time sessions and overlaps from a previously exported time ZIP; additive only, no password required
- Password entered in a modal; wrong password is detected and rejected

### CSV Import
Only available in "Přidat nové + zachovat vše" mode. No password required. A small **ⓘ** button next to the CSV option shows the expected format — click the dark popup to copy the header line to clipboard.

**Format** (semicolon-separated, first row = header):
```
title;parent;status;priority;related;detailRelated;dueDate
opalovací krém;seznam na dovolenou;;;;;;
cestovní pojištění;seznam na dovolenou;done;;;;;
pas;seznam na dovolenou;;;;;2026-06-01
nová položka;;in-process;1;;;
```

- `title` — required
- `parent` — title of the parent todo (looked up in DB first, then earlier rows in the same file)
- `status` — `in-process` / `done` / `failed` / blank
- `priority`, `related`, `detailRelated`, `dueDate` (YYYY-MM-DD) — optional
- Titles containing commas do **not** need to be quoted; extra comma-separated fragments are automatically merged back into the title

### Collapse/Expand State
The collapsed/expanded state of all todo trees is persisted in `localStorage` and restored automatically on page reload.

---

## Planner — „GPS pro tasky" (`/now`)

A forward-scheduling layer for time-blindness: a YAML **manifest** of tasks (with estimates, dependencies and constraints) drives a forward simulation over a planning horizon (~3 months, configurable) that predicts when each task will likely start/finish — recalculated on every change, like a car GPS recomputing the route. The manifest is **diagnostic + navigation, never a lock**: a stuck dependency is surfaced as an alert, never left silently waiting.

- **`/now` page** — opened via 🧭 Teď in the header. Four sections: **✅ Teď** (the single most urgent actionable task), **⏭ Pak** (next few), **🔥 V ohrožení** (predicted to miss its deadline, visible months ahead), **🔒 Zablokované** (waiting on another task or a person). Each card shows a plain-language relative time ("za ~2 dny"), the slack/skluz, and the deadline. Alerts surface bottlenecks and overdue recurring tasks.
- **Manifest** (`manifest.yaml` at the project root) — bidirectional mirror of the DB (DB is the source of truth). Edit it in the in-app **📄 Manifest** editor or directly in the file; external hand-edits are detected (SHA-256) and offered for reload rather than silently overwritten.
- **Task fields** — required: `id` (stable slug), `title`, `odhad` (2h/15m), `dependencies` (list of ids), `muzu_zacit` (earliest start). Optional: `status`, `deadline` (recommendation only), `kdy` (preferred day-windows, soft), `jen_v_praci` (work-hours only, hard), `muze_bezet_s` (may share a window), `ceka_na_cloveka` ({kdo, reakce}), `pevny_cas` (fixed appointment, immovable), `periodicita`.
- **Settings** (`nastaveni:` block) — `horizont_planovani`, `pracovni_doba`, `okna_dne` (rano/dopo/odpo/vecer), `reakce_lidi` (rychle/normalne/pomalu).
- **periodicita** — calendar rhythm: `denne` / `tydne[:po,st,pa]` / `mesicne[:15|prvni-streda|posledni-patek]` / `kvartalne` / `rocne` / `interval:14d`. `dependencies` is **superior** to periodicita: a generated occurrence becomes workable only when its deps are done; if the calendar says "now" but deps aren't done it is flagged **stuck**, not left waiting. Only one pending occurrence at a time.
- The scheduler is a pure, deterministic backend service (`SchedulerService`) covered by unit tests. It runs on manual `odhad` estimates now; a later phase will learn estimates from the existing time-tracking sessions.

> Note: the scheduler works in local wall-clock time; date-only deadlines (stored UTC midnight) may display with a small local-timezone offset.

---

## Project Structure

```
todolist/
├── backend/
│   └── TodoApi/
│       ├── TodoApi.csproj
│       ├── Program.cs
│       ├── Models/
│       │   ├── TodoItem.cs
│       │   ├── Comment.cs
│       │   └── CommentAttachment.cs
│       ├── Data/
│       │   └── AppDbContext.cs
│       ├── Controllers/
│       │   ├── TodosController.cs
│       │   ├── CommentsController.cs
│       │   └── ExportController.cs
│       └── uploads/          ← uploaded attachment files
├── backend/TodoApi.Tests/    ← xUnit integration tests
├── frontend/
│   ├── index.html
│   ├── package.json
│   ├── vite.config.ts
│   ├── tsconfig.json
│   └── src/
│       ├── main.tsx
│       ├── App.tsx
│       ├── App.css
│       ├── types.ts
│       ├── api/
│       │   ├── todos.ts
│       │   └── comments.ts
│       └── components/
│           ├── AddTodoForm.tsx
│           ├── TodoItem.tsx
│           ├── TodoList.tsx
│           └── CommentsPanel.tsx
└── README.md
```

---

## API Endpoints

### Todos
| Method | Route | Description |
|---|---|---|
| `GET` | `/api/todos` | Fetch all todos |
| `POST` | `/api/todos` | Create a new todo |
| `PUT` | `/api/todos/{id}` | Update a todo (title, status, completed, due date, priority, related, parentId) |
| `POST` | `/api/todos/{id}/move` | Move todo one position up or down among siblings (body: `{ "direction": "up"\|"down" }`) |
| `DELETE` | `/api/todos/{id}` | Delete a todo and its subtasks |

### Comments
| Method | Route | Description |
|---|---|---|
| `GET` | `/api/comments?todoId={id}` | Fetch comments for a todo (includes `attachments` array) |
| `GET` | `/api/comments/counts` | Fetch comment counts for all todos |
| `POST` | `/api/comments` | Create a comment (multipart: `todoId`, `text`, `file_0`…`file_N`, `preview_0`…`preview_N`) |
| `PUT` | `/api/comments/{id}` | Edit comment text (JSON body: `{ "text": "..." }`) |
| `DELETE` | `/api/comments/{id}` | Delete a comment and all its attachments |

### Export / Import
| Method | Route | Description |
|---|---|---|
| `POST` | `/api/export/export` | Export encrypted backup (JSON body: `{ "password": "..." }`) |
| `POST` | `/api/export/import` | Import backup (multipart: `password`, `file`, `mode`: `replace`/`merge`/`addonly`) |
| `POST` | `/api/export/import-csv` | Import CSV additively — no password (multipart: `file`) |

### Files
| Method | Route | Description |
|---|---|---|
| `GET` | `/api/files/open?path=...` | Open a local file or folder in its default Windows application (Process.Start) |

### Planner / Manifest
| Method | Route | Description |
|---|---|---|
| `GET` | `/api/plan?horizon=3m` | Forward-simulate the horizon; returns `now`, `next`, `blocked`, `atRisk`, `alerts`, `timeline` |
| `GET` | `/api/manifest` | Current manifest as YAML text (rendered from the DB) |
| `PUT` | `/api/manifest` | Validate + apply edited YAML (`{ "yaml": "..." }`) and mirror it to `manifest.yaml` |
| `GET` | `/api/manifest/status` | Whether `manifest.yaml` on disk was hand-edited since last write |
| `POST` | `/api/manifest/reload` | Adopt the on-disk `manifest.yaml` into the DB |

---

## Getting Started

### Prerequisites

- [.NET 8 SDK](https://aka.ms/dotnet/download)
- [Node.js](https://nodejs.org/) (v18+)

### Run the backend

```bash
cd backend/TodoApi
dotnet run
```

API starts on `http://localhost:6001`. The SQLite database (`todos.db`) and `uploads/` directory are created automatically on first run.

### Run the frontend

```bash
cd frontend
npm install
npm run dev
```

UI starts on `http://localhost:6173`. API calls are proxied to the backend automatically.

---

## Todo Item Schema

```json
{
  "id": 1,
  "title": "Buy groceries",
  "isCompleted": false,
  "status": "in-process",
  "dueDate": "2026-04-20T00:00:00",
  "createdAt": "2026-04-18T10:00:00",
  "parentId": null,
  "priority": "1",
  "related": "",
  "detailRelated": ""
}
```

**Status values:** `""` (blank) · `"in-process"` · `"on_hold"` · `"done"` · `"failed"`

---

## Comment Schema

```json
{
  "id": 42,
  "todoId": 1,
  "text": "Checked and confirmed.",
  "createdAt": "2026-05-28T10:00:00",
  "attachments": [
    {
      "id": 7,
      "commentId": 42,
      "path": "/uploads/abc123.jpg",
      "type": "image",
      "preview": null,
      "sortOrder": 0
    }
  ]
}
```

**Attachment `type` values:** `"image"` · `"video"` · `"file"` · `null`

`preview` is a server path to a generated JPEG thumbnail (used for DOCX, Office files, HEIC originals).
