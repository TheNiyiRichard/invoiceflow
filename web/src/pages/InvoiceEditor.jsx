import { useEffect, useEffectEvent, useRef, useState } from 'react'
import { Link, useLocation, useNavigate, useParams } from 'react-router'
import { ArrowLeft, CheckCircle, CloudArrowUp, FloppyDisk, Plus, Trash, WarningCircle, X } from '@phosphor-icons/react'
import { deleteJson, fieldErrors, getJson, postJson, putJson } from '../api.js'
import { useAuth } from '../auth/AuthContext.js'
import Dialog from '../components/Dialog.jsx'
import InvoicePreview from '../components/InvoicePreview.jsx'
import { addDays, daysBetween, today } from '../format.js'
import { AMOUNT_PATTERN, formatCents, previewTotals, toHundredths } from '../money.js'
import '../styles/invoices.css'
import '../styles/editor.css'

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const MAX_LINES = 100
const PREVIEW_TAX_RATE = 0.05 // new drafts only; the API saves the real rate on each invoice
const AUTOSAVE_MS = 1500 // waits for a pause in typing: one request per pause, not one per key
const NO_PROBLEM = { body: null, kind: null }
const PROBLEMS = {
  invalid: 'Not saved: some fields are incomplete',
  rejected: 'Not saved: check the highlighted fields',
  failed: 'Not saved: couldn’t reach the server',
}

const newLine = () => ({ key: crypto.randomUUID(), description: '', quantity: '1', unitPrice: '' })

function newForm() {
  const issueDate = today()
  return { clientId: '', issueDate, dueDate: addDays(issueDate, 30), notes: '', lineItems: [newLine()] }
}

// The saved invoice → form fields. Inputs hold text, so numbers become strings.
function toForm(inv) {
  return {
    clientId: inv.clientId,
    issueDate: inv.issueDate,
    dueDate: inv.dueDate,
    notes: inv.notes ?? '',
    lineItems: inv.lineItems.map((li) => ({
      key: li.id, description: li.description, quantity: String(li.quantity), unitPrice: String(li.unitPrice),
    })),
  }
}

// Only what InvoiceInput accepts. Number, status, tax rate and totals are the server's job.
function toBody(form) {
  return {
    clientId: form.clientId || null,
    issueDate: form.issueDate || null,
    dueDate: form.dueDate || null,
    notes: form.notes,
    lineItems: form.lineItems.map((li) => ({
      description: li.description, quantity: Number(li.quantity), unitPrice: Number(li.unitPrice),
    })),
  }
}

// One string per version of the form, so "changed since the last save?" is a simple compare
const bodyKey = (form) => JSON.stringify(toBody(form))

// A new invoice counts as started once anything has been typed or picked
const hasContent = (form) => form.clientId !== '' || form.notes.trim() !== '' ||
  form.lineItems.some((li) => li.description.trim() !== '' || li.unitPrice.trim() !== '')

function ago(ms) {
  const s = Math.max(0, Math.floor(ms / 1000))
  if (s < 10) return 'just now'
  if (s < 60) return `${s}s ago`
  return `${Math.floor(s / 60)} min ago`
}

export default function InvoiceEditorPage() {
  const { id } = useParams()
  // A fresh editor for each invoice, so nothing carries over from the previous one
  return <InvoiceLoader key={id ?? 'new'} id={id} />
}

// Loads your clients (and the invoice, when editing), then hands over to the form
function InvoiceLoader({ id }) {
  const isNew = id === undefined
  const [clients, setClients] = useState(null)
  const [invoice, setInvoice] = useState(null)
  const [loadError, setLoadError] = useState(isNew || GUID.test(id) ? null : 'notfound')

  useEffect(() => {
    const controller = new AbortController()
    getJson('/api/clients', controller.signal)
      .then((res) => (res.ok ? setClients(res.data) : setLoadError('failed')))
      .catch((err) => { if (err.name !== 'AbortError') setLoadError('failed') })
    return () => controller.abort()
  }, [])

  useEffect(() => {
    // The id in the URL is user input: only a real ID shape is sent to the API
    if (!id || !GUID.test(id)) return
    const controller = new AbortController()
    getJson(`/api/invoices/${id}`, controller.signal)
      .then((res) => (res.ok ? setInvoice(res.data) : setLoadError(res.status === 404 ? 'notfound' : 'failed')))
      .catch((err) => { if (err.name !== 'AbortError') setLoadError('failed') })
    return () => controller.abort()
  }, [id])

  // Someone else's invoice gets exactly the same answer as one that doesn't exist
  if (loadError === 'notfound') {
    return (
      <>
        <h1 className="page-title">Invoice not found</h1>
        <div className="empty"><p>Check the link, or go back to <Link to="/invoices">your invoices</Link>.</p></div>
      </>
    )
  }
  if (loadError) return <p className="alert" role="alert">Couldn’t load this invoice. Refresh to try again.</p>
  if (!clients || (!isNew && !invoice)) return <p className="muted">Loading…</p>

  return <InvoiceForm clients={clients} initial={invoice} onGone={() => setLoadError('notfound')} />
}

function InvoiceForm({ clients, initial, onGone }) {
  const navigate = useNavigate()
  const { state } = useLocation()
  const { user } = useAuth()
  const formRef = useRef(null)
  const isNew = initial === null

  const [invoice, setInvoice] = useState(initial) // the last copy the API saved
  const [form, setForm] = useState(() => (isNew ? newForm() : toForm(initial)))
  const [savedBody, setSavedBody] = useState(() => (isNew ? null : bodyKey(toForm(initial))))
  const [problem, setProblem] = useState(NO_PROBLEM) // which version failed to save, and why
  const [saving, setSaving] = useState(false)
  const [saves, setSaves] = useState(0) // successful autosaves so far
  const [elapsed, setElapsed] = useState({ save: 0, ms: 0 }) // time since the latest one
  const [errors, setErrors] = useState({})
  const [notice, setNotice] = useState(state?.created ? { ok: true, text: 'Draft created. From now on, changes save automatically.' } : null)
  const [confirmDelete, setConfirmDelete] = useState(false)
  const [deleting, setDeleting] = useState(false)
  const [deleteError, setDeleteError] = useState('')

  const locked = invoice !== null && invoice.status !== 'Draft'
  const canAutosave = !isNew && !locked
  const body = bodyKey(form)
  const unsaved = isNew ? hasContent(form) : body !== savedBody
  const pending = canAutosave && unsaved && body !== problem.body

  const taxRate = invoice?.taxRate ?? PREVIEW_TAX_RATE
  const ratePercent = Math.round(taxRate * 10_000) / 100
  const totals = previewTotals(form.lineItems, taxRate)
  const terms = daysBetween(form.issueDate, form.dueDate)
  const err = (key) => errors[key.toLowerCase()]

  const update = (patch) => setForm((f) => ({ ...f, ...patch }))
  const updateLine = (key, patch) =>
    setForm((f) => ({ ...f, lineItems: f.lineItems.map((li) => (li.key === key ? { ...li, ...patch } : li)) }))
  const addLine = () => setForm((f) => ({ ...f, lineItems: [...f.lineItems, newLine()] }))
  const removeLine = (key) => {
    setErrors({}) // line numbers shift, so old line errors would point at the wrong row
    setForm((f) => ({ ...f, lineItems: f.lineItems.filter((li) => li.key !== key) }))
  }

  // Existing drafts: PUT what's on screen. The form isn't replaced afterwards, so typing is never interrupted.
  async function save() {
    const sent = body
    setSaving(true)
    try {
      const res = await putJson(`/api/invoices/${invoice.id}`, toBody(form))
      if (res.ok) {
        setInvoice(res.data)
        setSavedBody(sent)
        setErrors({})
        setProblem(NO_PROBLEM)
        setSaves((n) => n + 1)
      } else if (res.status === 400) {
        setErrors(fieldErrors(res.data))
        setProblem({ body: sent, kind: 'rejected' })
      } else if (res.status === 409) {
        await reload('This invoice has been sent, so your last change wasn’t saved.')
      } else if (res.status === 404) {
        onGone()
      } else {
        setProblem({ body: sent, kind: 'failed' })
      }
    } catch {
      setProblem({ body: sent, kind: 'failed' })
    } finally {
      setSaving(false)
    }
  }

  // 409: it stopped being a draft somewhere else. Load the server's copy, which locks the form.
  async function reload(text) {
    const res = await getJson(`/api/invoices/${invoice.id}`)
    if (!res.ok) {
      onGone()
      return
    }
    const fresh = toForm(res.data)
    setInvoice(res.data)
    setForm(fresh)
    setSavedBody(bodyKey(fresh))
    setNotice({ ok: false, text })
  }

  // New invoices are only created when you click Create draft: no junk drafts, no used-up numbers
  async function create() {
    setSaving(true)
    setErrors({})
    setNotice(null)
    try {
      const res = await postJson('/api/invoices', toBody(form))
      if (res.ok) {
        navigate(`/invoices/${res.data.id}`, { replace: true, state: { created: true } })
        return
      }
      if (res.status === 400) {
        setErrors(fieldErrors(res.data))
        setNotice({ ok: false, text: 'Check the highlighted fields.' })
      } else {
        setNotice({ ok: false, text: 'Something went wrong. Try again.' })
      }
    } catch {
      setNotice({ ok: false, text: 'Can’t reach the server. Try again in a moment.' })
    } finally {
      setSaving(false)
    }
  }

  function askDelete() {
    setDeleteError('')
    setConfirmDelete(true)
  }

  // Drafts only. The server checks again, so a draft that was just sent somewhere else is kept.
  async function deleteDraft() {
    setDeleting(true)
    setDeleteError('')
    try {
      const res = await deleteJson(`/api/invoices/${invoice.id}`)
      // 404: it's already gone, which is what you wanted
      if (res.ok || res.status === 404) {
        navigate('/invoices', { replace: true, state: { deleted: invoice.number } })
        return
      }
      if (res.status === 409) {
        setConfirmDelete(false)
        await reload('This invoice has been sent, so it can’t be deleted.')
      } else {
        setDeleteError('Something went wrong. Try again.')
      }
    } catch {
      setDeleteError('Can’t reach the server. Try again in a moment.')
    } finally {
      setDeleting(false)
    }
  }

  function handleSubmit(e) {
    e.preventDefault()
    if (isNew) create()
    else if (!saving) save()
  }

  const autosave = useEffectEvent(() => {
    // The browser's own checks first (required fields, number format): no point sending what it already knows is incomplete
    if (!formRef.current.checkValidity()) {
      setProblem({ body, kind: 'invalid' })
      return
    }
    save()
  })

  // After a pause in typing, one request at a time. A version that failed is never retried in a loop.
  useEffect(() => {
    if (!pending || saving) return
    const timer = setTimeout(() => autosave(), AUTOSAVE_MS)
    return () => clearTimeout(timer)
  }, [pending, saving, body])

  // Keeps "Saved 20s ago" current. The clock is only read here, in an effect, never during render.
  useEffect(() => {
    if (saves === 0) return
    const start = Date.now()
    const timer = setInterval(() => setElapsed({ save: saves, ms: Date.now() - start }), 5000)
    return () => clearInterval(timer)
  }, [saves])
  const savedAgo = saves === 0 ? null : ago(elapsed.save === saves ? elapsed.ms : 0)

  // Warns before closing or refreshing the tab while something isn't saved yet
  useEffect(() => {
    if (!unsaved || locked) return
    const warn = (e) => e.preventDefault()
    window.addEventListener('beforeunload', warn)
    return () => window.removeEventListener('beforeunload', warn)
  }, [unsaved, locked])

  // Plain data only. The buttons in the header get their click handlers directly.
  let status = null
  if (canAutosave) {
    if (saving || pending) status = 'saving'
    else if (!unsaved) status = 'saved'
    else status = 'problem'
  }
  const showMe = () => formRef.current.requestSubmit()

  const previewLines = form.lineItems.map((li, i) => ({
    key: li.key,
    description: li.description,
    quantity: toHundredths(li.quantity) === null ? null : li.quantity.trim(),
    rate: toHundredths(li.unitPrice),
    amount: totals.amounts[i],
  }))

  return (
    <div className="editor-layout">
      <form ref={formRef} className="editor" onSubmit={handleSubmit}>
        <div className="editor-head">
          <div>
            <Link className="back-link" to="/invoices"><ArrowLeft size={14} />Invoices</Link>
            <div className="editor-title">
              <h1 className="page-title">{invoice ? invoice.number : 'New invoice'}</h1>
              {invoice && <span className={`pill pill--${invoice.status.toLowerCase()}`}>{invoice.status}</span>}
            </div>
          </div>
          {isNew && (
            <button className="btn-primary" type="submit" disabled={saving || clients.length === 0}>
              <FloppyDisk size={16} />{saving ? 'Creating…' : 'Create draft'}
            </button>
          )}
          {canAutosave && (
            <div className="editor-actions">
              {status === 'saving' && (
                <div className="save-status"><CloudArrowUp size={16} /><span role="status">Saving…</span></div>
              )}
              {status === 'saved' && (
                <div className="save-status save-status--ok">
                  <CheckCircle size={16} weight="fill" /><span role="status">Saved</span>
                  {savedAgo && <span aria-hidden="true">{savedAgo}</span>}
                </div>
              )}
              {status === 'problem' && (
                <div className="save-status save-status--problem">
                  <WarningCircle size={16} /><span role="status">{PROBLEMS[problem.kind]}</span>
                  {problem.kind === 'invalid' && <button type="button" className="link-btn" onClick={showMe}>Show me</button>}
                  {problem.kind === 'failed' && <button type="button" className="link-btn" onClick={save}>Try again</button>}
                </div>
              )}
              <button type="button" className="btn-ghost-danger" onClick={askDelete} disabled={saving}>
                <Trash size={16} />Delete draft
              </button>
            </div>
          )}
        </div>

        {notice && <p className={notice.ok ? 'notice notice--ok' : 'notice notice--error'} role={notice.ok ? 'status' : 'alert'}>{notice.text}</p>}
        {locked && <p className="notice notice--warn">This invoice is {invoice.status.toLowerCase()}, so it can’t be changed. It’s the record of what your client received.</p>}

        {/* Only disabled while locked or creating: autosave never takes the focus away from what you're typing */}
        <fieldset className="editor-body" disabled={locked || (isNew && saving)}>
          <section>
            <h2 className="section-label">Bill to</h2>
            {clients.length === 0 ? (
              <div className="empty">
                <p>You don’t have any clients yet. <Link to="/clients" state={{ add: true }}>Add your first client</Link>, then come back.</p>
              </div>
            ) : (
              <div className="field">
                <label htmlFor="clientId">Client</label>
                <select id="clientId" className="input" value={form.clientId} required
                  onChange={(e) => update({ clientId: e.target.value })} aria-invalid={Boolean(err('clientId'))}>
                  <option value="" disabled>Choose a client</option>
                  {clients.map((c) => <option key={c.id} value={c.id}>{c.company ? `${c.name} · ${c.company}` : c.name}</option>)}
                </select>
                {err('clientId') && <p className="field-error">{err('clientId')}</p>}
              </div>
            )}
          </section>

          <section className="dates">
            <div className="field">
              <label htmlFor="issueDate">Issue date</label>
              <input id="issueDate" className="input" type="date" value={form.issueDate} required
                onChange={(e) => update({ issueDate: e.target.value })} aria-invalid={Boolean(err('issueDate'))} />
              {err('issueDate') && <p className="field-error">{err('issueDate')}</p>}
            </div>
            <div className="field">
              <label htmlFor="dueDate">Due date{terms > 0 && <span className="optional"> · Net {terms}</span>}</label>
              <input id="dueDate" className="input" type="date" value={form.dueDate} required
                onChange={(e) => update({ dueDate: e.target.value })} aria-invalid={Boolean(err('dueDate'))} />
              {err('dueDate') && <p className="field-error">{err('dueDate')}</p>}
            </div>
          </section>

          <section>
            <h2 className="section-label">Line items</h2>
            <div className="line line-head" aria-hidden="true">
              <span>Description</span><span>Qty</span><span>Rate</span><span className="num">Amount</span><span />
            </div>
            <div className="lines">
              {form.lineItems.map((li, i) => {
                const rowErr = {
                  description: err(`lineItems[${i}].description`),
                  quantity: err(`lineItems[${i}].quantity`),
                  unitPrice: err(`lineItems[${i}].unitPrice`),
                }
                const message = rowErr.description || rowErr.quantity || rowErr.unitPrice
                const amount = totals.amounts[i]
                return (
                  <div key={li.key} className="line-row">
                    <div className="line">
                      <input className="input" placeholder="Description" maxLength={500} required value={li.description}
                        onChange={(e) => updateLine(li.key, { description: e.target.value })}
                        aria-label={`Line ${i + 1} description`} aria-invalid={Boolean(rowErr.description)} />
                      <input className="input mono" placeholder="Qty" inputMode="decimal" pattern={AMOUNT_PATTERN}
                        title="A number, up to 2 decimal places" required value={li.quantity}
                        onChange={(e) => updateLine(li.key, { quantity: e.target.value })}
                        aria-label={`Line ${i + 1} quantity`} aria-invalid={Boolean(rowErr.quantity)} />
                      <input className="input mono" placeholder="0.00" inputMode="decimal" pattern={AMOUNT_PATTERN}
                        title="An amount, up to 2 decimal places" required value={li.unitPrice}
                        onChange={(e) => updateLine(li.key, { unitPrice: e.target.value })}
                        aria-label={`Line ${i + 1} rate`} aria-invalid={Boolean(rowErr.unitPrice)} />
                      <span className="line-amount mono">{amount === null ? '—' : formatCents(amount)}</span>
                      <button type="button" className="icon-btn" onClick={() => removeLine(li.key)}
                        disabled={form.lineItems.length === 1} aria-label={`Remove line ${i + 1}`}><X size={15} /></button>
                    </div>
                    {message && <p className="field-error">{message}</p>}
                  </div>
                )
              })}
            </div>
            {err('lineItems') && <p className="field-error">{err('lineItems')}</p>}

            <div className="lines-foot">
              <button type="button" className="btn-add" onClick={addLine} disabled={form.lineItems.length >= MAX_LINES}>
                <Plus size={14} />Add line item
              </button>
              <div className="totals-wrap">
                <dl className="totals">
                  <div><dt>Subtotal</dt><dd className="mono">{formatCents(totals.subtotal)}</dd></div>
                  <div><dt>GST ({ratePercent}%)</dt><dd className="mono">{formatCents(totals.tax)}</dd></div>
                  <div className="totals-due"><dt>Total due</dt><dd>{formatCents(totals.total)}</dd></div>
                </dl>
                <p className="field-hint">The server works these out again when you save.</p>
              </div>
            </div>
          </section>

          <section>
            <div className="field">
              <label htmlFor="notes">Notes <span className="optional">(optional)</span></label>
              <textarea id="notes" className="input" rows={3} maxLength={2000} value={form.notes}
                onChange={(e) => update({ notes: e.target.value })} placeholder="Payment details or a thank-you note" />
              {err('notes') && <p className="field-error">{err('notes')}</p>}
            </div>
          </section>
        </fieldset>
      </form>

      <aside className="preview-panel" aria-labelledby="preview-label">
        <p id="preview-label" className="preview-label">Live preview</p>
        <InvoicePreview
          sender={{ name: user?.businessName || user?.fullName || '', email: user?.email ?? '' }}
          client={clients.find((c) => c.id === form.clientId) ?? null}
          number={invoice?.number ?? 'Number on save'}
          issueDate={form.issueDate}
          dueDate={form.dueDate}
          lines={previewLines}
          totals={totals}
          ratePercent={ratePercent}
          notes={form.notes}
        />
      </aside>

      <Dialog open={confirmDelete} title={`Delete ${invoice?.number}?`} onClose={() => setConfirmDelete(false)}>
        <p className="dialog-text">This draft and its line items will be deleted. This can’t be undone.</p>
        {deleteError && <p className="notice notice--error" role="alert">{deleteError}</p>}
        <div className="dialog-actions">
          <button type="button" className="btn-secondary" onClick={() => setConfirmDelete(false)} disabled={deleting}>Cancel</button>
          <button type="button" className="btn-danger" onClick={deleteDraft} disabled={deleting}>
            <Trash size={16} />{deleting ? 'Deleting…' : 'Delete draft'}
          </button>
        </div>
      </Dialog>
    </div>
  )
}