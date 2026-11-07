import { useEffect, useState } from 'react'
import { Link, useLocation, useNavigate, useParams } from 'react-router'
import { ArrowLeft, FloppyDisk, Plus, X } from '@phosphor-icons/react'
import { fieldErrors, getJson, postJson, putJson } from '../api.js'
import { addDays, daysBetween, today } from '../format.js'
import { AMOUNT_PATTERN, formatCents, previewTotals } from '../money.js'
import '../styles/invoices.css'
import '../styles/editor.css'

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const MAX_LINES = 100
const PREVIEW_TAX_RATE = 0.05 // new drafts only; the API saves the real rate on each invoice

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

export default function InvoiceEditorPage() {
  const { id } = useParams()
  // A fresh editor for each invoice, so nothing carries over from the previous one
  return <InvoiceEditor key={id ?? 'new'} id={id} />
}

function InvoiceEditor({ id }) {
  const navigate = useNavigate()
  const { state } = useLocation()
  const isNew = id === undefined

  const [form, setForm] = useState(isNew ? newForm : null)
  const [invoice, setInvoice] = useState(null) // the last copy the API saved
  const [clients, setClients] = useState(null)
  const [loadError, setLoadError] = useState(isNew || GUID.test(id) ? null : 'notfound')
  const [errors, setErrors] = useState({})
  const [notice, setNotice] = useState(state?.created ? { ok: true, text: 'Draft created.' } : null)
  const [busy, setBusy] = useState(false)

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
      .then((res) => {
        if (res.ok) {
          setInvoice(res.data)
          setForm(toForm(res.data))
        } else {
          setLoadError(res.status === 404 ? 'notfound' : 'failed')
        }
      })
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
  if (!form || !clients) return <p className="muted">Loading…</p>

  const locked = invoice !== null && invoice.status !== 'Draft'
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

  async function handleSubmit(e) {
    e.preventDefault()
    setBusy(true)
    setErrors({})
    setNotice(null)
    try {
      const body = toBody(form)
      const res = isNew ? await postJson('/api/invoices', body) : await putJson(`/api/invoices/${id}`, body)
      if (res.ok) {
        if (isNew) {
          navigate(`/invoices/${res.data.id}`, { replace: true, state: { created: true } })
          return
        }
        // Show what the server saved (its own totals, trimmed text), not what was typed
        setInvoice(res.data)
        setForm(toForm(res.data))
        setNotice({ ok: true, text: 'Draft saved.' })
      } else if (res.status === 400) {
        setErrors(fieldErrors(res.data))
        setNotice({ ok: false, text: 'Check the highlighted fields.' })
      } else if (res.status === 409) {
        setNotice({ ok: false, text: 'This invoice has already been sent, so it can no longer be edited.' })
      } else if (res.status === 404) {
        setLoadError('notfound')
      } else {
        setNotice({ ok: false, text: 'Something went wrong. Try again.' })
      }
    } catch {
      setNotice({ ok: false, text: 'Can’t reach the server. Try again in a moment.' })
    } finally {
      setBusy(false)
    }
  }

  return (
    <form className="editor" onSubmit={handleSubmit}>
      <div className="editor-head">
        <div>
          <Link className="back-link" to="/invoices"><ArrowLeft size={14} />Invoices</Link>
          <div className="editor-title">
            <h1 className="page-title">{invoice ? invoice.number : 'New invoice'}</h1>
            {invoice && <span className={`pill pill--${invoice.status.toLowerCase()}`}>{invoice.status}</span>}
          </div>
        </div>
        {!locked && (
          <button className="btn-primary" type="submit" disabled={busy || clients.length === 0}>
            <FloppyDisk size={16} />{busy ? 'Saving…' : isNew ? 'Create draft' : 'Save draft'}
          </button>
        )}
      </div>

      {notice && <p className={notice.ok ? 'notice notice--ok' : 'notice notice--error'} role={notice.ok ? 'status' : 'alert'}>{notice.text}</p>}
      {locked && <p className="notice notice--warn">This invoice is {invoice.status.toLowerCase()}, so it can’t be changed. It’s the record of what your client received.</p>}

      <fieldset className="editor-body" disabled={locked || busy}>
        <section>
          <h2 className="section-label">Bill to</h2>
          {clients.length === 0 ? (
            <div className="empty"><p>You don’t have any clients yet. Add one first, then come back.</p></div>
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
  )
}