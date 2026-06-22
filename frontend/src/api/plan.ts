import type { PlanResult } from '../types'

const BASE = '/api/plan'

export async function fetchPlan(horizon?: string): Promise<PlanResult> {
  const url = horizon ? `${BASE}?horizon=${encodeURIComponent(horizon)}` : BASE
  // never cache — the plan is recomputed on every change
  const res = await fetch(url, { cache: 'no-store' })
  if (!res.ok) throw new Error('Failed to fetch plan')
  return res.json()
}

// ── manifest YAML editor API ─────────────────────────────────────────────────
const M = '/api/manifest'

export async function fetchManifest(): Promise<string> {
  const res = await fetch(M, { cache: 'no-store' })
  if (!res.ok) throw new Error('Failed to fetch manifest')
  return (await res.json()).yaml
}

export async function saveManifest(yaml: string): Promise<{ ok: boolean; error?: string }> {
  const res = await fetch(M, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ yaml }),
  })
  if (res.ok) return { ok: true }
  let error = 'Uložení selhalo.'
  try { error = (await res.json()).error ?? error } catch { /* ignore */ }
  return { ok: false, error }
}

export interface ManifestStatus {
  fileExists: boolean
  externalChange: boolean
  fileMtimeUtc: string | null
  fileTaskCount: number
}

export async function fetchManifestStatus(): Promise<ManifestStatus> {
  const res = await fetch(`${M}/status`, { cache: 'no-store' })
  if (!res.ok) throw new Error('Failed to fetch manifest status')
  return res.json()
}

export async function reloadManifest(): Promise<{ ok: boolean; error?: string }> {
  const res = await fetch(`${M}/reload`, { method: 'POST' })
  if (res.ok) return { ok: true }
  let error = 'Načtení selhalo.'
  try { error = (await res.json()).error ?? error } catch { /* ignore */ }
  return { ok: false, error }
}
