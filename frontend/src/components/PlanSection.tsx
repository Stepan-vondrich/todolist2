import type { PlanNode } from '../types'
import PlanNodeCard from './PlanNodeCard'

export default function PlanSection({
  title, emoji, nodes, accent, atRisk, emptyHint,
}: {
  title: string
  emoji: string
  nodes: PlanNode[]
  accent: string
  atRisk?: boolean
  emptyHint?: string
}) {
  return (
    <section className="plan-section" style={{ ['--accent' as string]: accent }}>
      <h2 className="plan-section-title">
        <span className="plan-section-emoji">{emoji}</span>
        {title}
        <span className="plan-section-count">{nodes.length}</span>
      </h2>
      {nodes.length === 0 ? (
        <p className="plan-section-empty">{emptyHint ?? '—'}</p>
      ) : (
        <div className="plan-section-cards">
          {nodes.map(n => <PlanNodeCard key={n.slug} node={n} atRisk={atRisk} />)}
        </div>
      )}
    </section>
  )
}
