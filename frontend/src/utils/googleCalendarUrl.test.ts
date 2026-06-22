import { describe, it, expect } from 'vitest'
import { buildGoogleCalendarUrl, combineDateTime } from './googleCalendarUrl'

describe('buildGoogleCalendarUrl', () => {
  // Local components so formatting is timezone-independent (getHours etc. echo them back).
  const start = new Date(2026, 3, 20, 9, 0, 0) // 2026-04-20 09:00 local

  it('points at the Google Calendar render template', () => {
    const url = buildGoogleCalendarUrl({ title: 'X', start, durationMinutes: 30 })
    expect(url).toContain('https://calendar.google.com/calendar/render?action=TEMPLATE')
  })

  it('encodes start/end as YYYYMMDDTHHMMSS/YYYYMMDDTHHMMSS with the literal slash', () => {
    const url = buildGoogleCalendarUrl({ title: 'X', start, durationMinutes: 90 })
    expect(url).toContain('dates=20260420T090000/20260420T103000')
  })

  it('puts the task title in text (URL-encoded)', () => {
    const url = buildGoogleCalendarUrl({ title: 'Schůzka s týmem', start, durationMinutes: 60 })
    expect(url).toContain(`text=${encodeURIComponent('Schůzka s týmem')}`)
  })

  it('puts the comments into details (URL-encoded), empty when omitted', () => {
    const withDesc = buildGoogleCalendarUrl({ title: 'X', description: 'pozn. & detail', start, durationMinutes: 60 })
    expect(withDesc).toContain(`details=${encodeURIComponent('pozn. & detail')}`)
    const without = buildGoogleCalendarUrl({ title: 'X', start, durationMinutes: 60 })
    expect(without).toContain('details=')
  })

  it('rolls the end time over midnight correctly', () => {
    const late = new Date(2026, 3, 20, 23, 30, 0)
    const url = buildGoogleCalendarUrl({ title: 'X', start: late, durationMinutes: 60 })
    expect(url).toContain('dates=20260420T233000/20260421T003000')
  })
})

describe('combineDateTime', () => {
  it('combines a YYYY-MM-DD date and HH:MM time into a local Date', () => {
    const d = combineDateTime('2026-04-20', '09:30')
    expect(d.getFullYear()).toBe(2026)
    expect(d.getMonth()).toBe(3) // April (0-indexed)
    expect(d.getDate()).toBe(20)
    expect(d.getHours()).toBe(9)
    expect(d.getMinutes()).toBe(30)
  })

  it('tolerates a datetime date string by taking the date part', () => {
    const d = combineDateTime('2026-04-20T00:00:00', '08:00')
    expect(d.getDate()).toBe(20)
    expect(d.getHours()).toBe(8)
  })
})
