import { useCallback, useEffect, useState } from 'react'
import { fetchManifest, saveManifest, fetchManifestStatus, reloadManifest } from '../api/plan'
import './manifest.css'

const PLACEHOLDER = `nastaveni:
   horizont_planovani: 3m
   pracovni_doba:
      po-pa: "09:00-17:30"

tasky:
   - id: muj_task
     title: Můj první task
     odhad: 2h
     muzu_zacit: 2026-06-02
     dependencies:
`

export default function ManifestPanel({ onClose, onSaved }: { onClose: () => void; onSaved?: () => void }) {
  const [yaml, setYaml] = useState('')
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [savedFlash, setSavedFlash] = useState(false)
  const [externalChange, setExternalChange] = useState(false)
  const [filePath] = useState('manifest.yaml')

  const load = useCallback(async () => {
    setLoading(true); setError(null)
    try { setYaml(await fetchManifest()) }
    catch { setError('Manifest se nepodařilo načíst.') }
    finally { setLoading(false) }
  }, [])

  const checkStatus = useCallback(async () => {
    try { setExternalChange((await fetchManifestStatus()).externalChange) }
    catch { /* ignore */ }
  }, [])

  useEffect(() => { load(); checkStatus() }, [load, checkStatus])

  async function handleSave() {
    setSaving(true); setError(null)
    const res = await saveManifest(yaml)
    setSaving(false)
    if (res.ok) {
      setSavedFlash(true); setTimeout(() => setSavedFlash(false), 1500)
      setExternalChange(false)
      onSaved?.()
    } else {
      setError(res.error ?? 'Uložení selhalo.')
    }
  }

  async function handleReload() {
    const res = await reloadManifest()
    if (res.ok) { await load(); setExternalChange(false); onSaved?.() }
    else setError(res.error ?? 'Načtení ze souboru selhalo.')
  }

  return (
    <div className="manifest-overlay" onClick={onClose}>
      <div className="manifest-modal" onClick={e => e.stopPropagation()}>
        <div className="manifest-head">
          <span className="manifest-title">📄 Manifest <code>{filePath}</code></span>
          <button className="manifest-close" onClick={onClose} aria-label="Zavřít">✕</button>
        </div>

        {externalChange && (
          <div className="manifest-banner">
            Soubor na disku se změnil mimo aplikaci.
            <button className="manifest-banner-btn" onClick={handleReload}>Načíst ze souboru</button>
          </div>
        )}

        {loading ? (
          <p className="manifest-loading">Načítám…</p>
        ) : (
          <textarea
            className="manifest-textarea"
            value={yaml}
            spellCheck={false}
            placeholder={PLACEHOLDER}
            onChange={e => setYaml(e.target.value)}
          />
        )}

        {error && <div className="manifest-error">{error}</div>}

        <div className="manifest-actions">
          <span className="manifest-hint">DB je zdroj pravdy; uložení zapíše i do souboru.</span>
          <button className="backup-btn" onClick={load}>↻ Načíst z DB</button>
          <button className="backup-confirm-btn" disabled={saving} onClick={handleSave}>
            {saving ? '…' : savedFlash ? '✓ Uloženo' : 'Uložit'}
          </button>
        </div>
      </div>
    </div>
  )
}
