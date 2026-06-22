import type { PlanNode } from '../types'

const DOW = ['ne', 'po', 'út', 'st', 'čt', 'pá', 'so']

function fmtDateTime(iso: string): string {
  const d = new Date(iso)
  const time = d.getHours() === 0 && d.getMinutes() === 0
    ? '' : ` ${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`
  return `${DOW[d.getDay()]} ${d.getDate()}.${d.getMonth() + 1}.${time}`
}

/** Time-blindness aid: always show how far away something is in plain words. */
function relative(iso: string): string {
  const ms = new Date(iso).getTime() - Date.now()
  const past = ms < 0
  const mins = Math.round(Math.abs(ms) / 60000)
  const hours = Math.round(mins / 60)
  const days = Math.round(hours / 24)
  let body: string
  if (mins < 90) body = `${mins} min`
  else if (hours < 36) body = `~${hours} h`
  else body = `~${days} ${days === 1 ? 'den' : days < 5 ? 'dny' : 'dní'}`
  return past ? `před ${body}` : `za ${body}`
}

function slackBadge(node: PlanNode) {
  if (node.slackMinutes >= 1e9) return null // no deadline → infinite slack
  const h = Math.round(node.slackMinutes / 60)
  const neg = node.slackMinutes < 0
  return (
    <span className={`plan-slack${neg ? ' plan-slack--neg' : ''}`}>
      {neg ? `${h} h skluz` : `rezerva ${h} h`}
    </span>
  )
}

export default function PlanNodeCard({ node, atRisk }: { node: PlanNode; atRisk?: boolean }) {
  return (
    <div className={`plan-card plan-card--${node.state}${atRisk ? ' plan-card--risk' : ''}`}>
      <div className="plan-card-head">
        <span className="plan-card-title">{node.title || node.slug}</span>
        {node.isOccurrence && <span className="plan-chip plan-chip--rec">↻ opakované</span>}
        {node.softWindowMissed && <span className="plan-chip plan-chip--soft">mimo okno</span>}
      </div>

      <div className="plan-card-meta">
        <span className="plan-when" title={fmtDateTime(node.predictedStart)}>
          🕒 {relative(node.predictedStart)} · {fmtDateTime(node.predictedStart)}
        </span>
        {slackBadge(node)}
      </div>

      {node.blockedBy && (
        <div className="plan-blocked-note">
          🔒 {node.blockedBy.reason}
          {node.downstreamImpactCount > 0 && ` · drží ${node.downstreamImpactCount} navazujících`}
        </div>
      )}

      {node.deadline && (
        <div className="plan-deadline">
          🏁 termín {fmtDateTime(node.deadline)}{atRisk && ' — nestíháš'}
        </div>
      )}
    </div>
  )
}
