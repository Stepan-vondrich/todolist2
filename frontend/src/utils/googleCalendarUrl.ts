/**
 * Builds a "create event" link for Google Calendar (the render/TEMPLATE form).
 * Opening it shows a prefilled event the user saves with one click — no login or API needed.
 *
 * Times are formatted from the Date's LOCAL components (wall-clock), so the event appears at the
 * chosen time in the user's calendar.
 */
export interface CalendarEvent {
  title: string
  description?: string
  start: Date
  durationMinutes: number
}

function pad(n: number): string {
  return String(n).padStart(2, '0')
}

/** Format a Date as YYYYMMDDTHHMMSS using its local components. */
function formatLocal(d: Date): string {
  return (
    `${d.getFullYear()}${pad(d.getMonth() + 1)}${pad(d.getDate())}` +
    `T${pad(d.getHours())}${pad(d.getMinutes())}${pad(d.getSeconds())}`
  )
}

export function buildGoogleCalendarUrl({ title, description = '', start, durationMinutes }: CalendarEvent): string {
  const end = new Date(start.getTime() + durationMinutes * 60_000)
  const dates = `${formatLocal(start)}/${formatLocal(end)}`
  return (
    'https://calendar.google.com/calendar/render?action=TEMPLATE' +
    `&text=${encodeURIComponent(title)}` +
    `&dates=${dates}` +
    `&details=${encodeURIComponent(description)}`
  )
}

/** Combine a "YYYY-MM-DD" (or full ISO) date string and an "HH:MM" time into a local Date. */
export function combineDateTime(dateStr: string, timeStr: string): Date {
  const [y, m, d] = dateStr.split('T')[0].split('-').map(Number)
  const [hh, mm] = timeStr.split(':').map(Number)
  return new Date(y, m - 1, d, hh || 0, mm || 0, 0)
}
