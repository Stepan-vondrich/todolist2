import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import type { PlanResult } from '../types'
import { fetchPlan } from '../api/plan'
import PlanSection from './PlanSection'
import PlanNodeCard from './PlanNodeCard'
import ManifestPanel from './ManifestPanel'
import './now.css'

const HORIZONS = ['2w', '1m', '3m', '6m']

export default function NowPage() {
  const [plan, setPlan] = useState<PlanResult | null>(null)
  const [horizon, setHorizon] = useState('3m')
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [manifestOpen, setManifestOpen] = useState(false)

  const load = useCallback(async (h: string) => {
    setLoading(true); setError(null)
    try { setPlan(await fetchPlan(h)) }
    catch { setError('Plán se nepodařilo načíst. Běží backend?') }
    finally { setLoading(false) }
  }, [])

  useEffect(() => { load(horizon) }, [horizon, load])

  return (
    <div className="now-page">
      <div className="now-header">
        <h1>🧭 Teď</h1>
        <div className="now-header-controls">
          <select className="now-horizon" value={horizon} onChange={e => setHorizon(e.target.value)}>
            {HORIZONS.map(h => <option key={h} value={h}>horizont {h}</option>)}
          </select>
          <button className="backup-btn" onClick={() => load(horizon)}>↻ Přepočítat</button>
          <button className="backup-btn" onClick={() => setManifestOpen(true)}>📄 Manifest</button>
          <Link className="backup-btn" to="/">← Seznam</Link>
        </div>
      </div>

      {manifestOpen && (
        <ManifestPanel onClose={() => setManifestOpen(false)} onSaved={() => load(horizon)} />
      )}

      {error && <p className="error">{error}</p>}
      {loading && !plan && <p className="now-loading">Počítám plán…</p>}

      {plan && (
        <>
          {plan.alerts.length > 0 && (
            <div className="plan-alerts">
              {plan.alerts.map((a, i) => (
                <div key={i} className={`plan-alert plan-alert--${a.type}`}>
                  ⚠️ {a.message}
                </div>
              ))}
            </div>
          )}

          <section className="plan-section plan-section--now" style={{ ['--accent' as string]: '#16a34a' }}>
            <h2 className="plan-section-title"><span className="plan-section-emoji">✅</span>Teď</h2>
            {plan.now
              ? <PlanNodeCard node={plan.now} atRisk={plan.atRisk.some(n => n.slug === plan.now!.slug)} />
              : <p className="plan-section-empty">Nic akčního — buď je vše hotové, nebo vše čeká (viz Zablokované).</p>}
          </section>

          <PlanSection title="Pak" emoji="⏭" accent="#2563eb" nodes={plan.next}
            emptyHint="Žádné další volné tasky." />
          <PlanSection title="V ohrožení" emoji="🔥" accent="#dc2626" nodes={plan.atRisk} atRisk
            emptyHint="Nic nehoří — termíny zatím stíháš." />
          <PlanSection title="Zablokované" emoji="🔒" accent="#9333ea" nodes={plan.blocked}
            emptyHint="Nic nečeká na jiný task ani na člověka." />

          <p className="now-footer">
            Spočítáno {new Date(plan.computedAt).toLocaleString('cs-CZ')} · horizont {plan.horizon} · {plan.timeline.length} výskytů na ose
          </p>
        </>
      )}
    </div>
  )
}
