import { useState } from 'react'

interface Props {
  /** Task title, shown for confirmation and used as the event title. */
  title: string
  /** Called with the chosen start time ("HH:MM") and duration in minutes. */
  onConfirm: (startTime: string, durationMinutes: number) => void
  onClose: () => void
}

const PRESETS = [30, 60, 90, 120]

export default function CalendarEventModal({ title, onConfirm, onClose }: Props) {
  const [time, setTime] = useState('09:00')
  const [duration, setDuration] = useState(60)

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal calendar-modal" onClick={e => e.stopPropagation()}>
        <h3>Přidat do Google kalendáře</h3>
        <p className="calendar-modal-title">{title}</p>

        <label className="calendar-field">
          Začátek
          <input type="time" value={time} onChange={e => setTime(e.target.value)} />
        </label>

        <label className="calendar-field">
          Délka (min)
          <input
            type="number"
            min={5}
            step={5}
            value={duration}
            onChange={e => setDuration(Math.max(5, Number(e.target.value) || 0))}
          />
        </label>

        <div className="calendar-presets">
          {PRESETS.map(p => (
            <button
              key={p}
              type="button"
              className={duration === p ? 'active' : ''}
              onClick={() => setDuration(p)}
            >
              {p < 60 ? `${p} min` : `${p / 60} h`}
            </button>
          ))}
        </div>

        <div className="modal-actions">
          <button type="button" className="btn-secondary" onClick={onClose}>Zrušit</button>
          <button type="button" className="btn-primary" onClick={() => onConfirm(time, duration)}>
            Přidat do kalendáře
          </button>
        </div>
      </div>
    </div>
  )
}
