import { useEffect, useState } from 'react'
import { useLocation } from 'react-router'
import { PencilSimple, Plus, Trash } from '@phosphor-icons/react'
import { deleteJson, fieldErrors, getJson, postJson, putJson } from '../api.js'
import Dialog from '../components/Dialog.jsx'
import TextField from '../components/TextField.jsx'
import '../styles/invoices.css'
import '../styles/clients.css'

const byName = (a, b) => a.name.localeCompare(b.name)

export default function Clients() {
  const { state } = useLocation()
  const [clients, setClients] = useState(null)
  const [failed, setFailed] = useState(false)
  const [editing, setEditing] = useState(state?.add ? 'new' : null) // 'new', the client being edited, or null
  const [deleting, setDeleting] = useState(null) // the client being deleted, or null
  const [notice, setNotice] = useState('')

  useEffect(() => {
    const controller = new AbortController()
    getJson('/api/clients', controller.signal)
      .then((res) => (res.ok ? setClients(res.data) : setFailed(true)))
      .catch((err) => { if (err.name !== 'AbortError') setFailed(true) })
    return () => controller.abort()
  }, [])

  function open(setter, value) {
    setNotice('')
    setter(value)
  }

  function saved(client) {
    setClients((list) => [...(list ?? []).filter((c) => c.id !== client.id), client].sort(byName))
    setNotice(`${client.name} ${editing === 'new' ? 'added' : 'updated'}.`)
    setEditing(null)
  }

  function deleted(client) {
    setClients((list) => (list ?? []).filter((c) => c.id !== client.id))
    setNotice(`${client.name} deleted.`)
    setDeleting(null)
  }

  return (
    <>
      <div className="page-head">
        <h1 className="page-title">Clients</h1>
        {clients && <span className="page-count">{clients.length} total</span>}
        <button type="button" className="page-action" onClick={() => open(setEditing, 'new')}><Plus size={16} />Add client</button>
      </div>
      <p className="page-lede">Only the clients you added. No other account can see them.</p>

      {notice && <p className="notice notice--ok page-notice" role="status">{notice}</p>}
      {failed && <p className="alert" role="alert">Couldn’t load your clients. Refresh to try again.</p>}
      {clients?.length === 0 && <div className="empty"><p>No clients yet. Add your first one to start invoicing.</p></div>}
      {clients?.length > 0 && (
        <ul className="list">
          {clients.map((c) => (
            <li key={c.id}>
              <span className="list-main">{c.name}{c.company && <span className="list-sub">{c.company}</span>}</span>
              <span className="list-meta">{c.email}</span>
              <span className="list-actions">
                <button type="button" className="icon-btn" title="Edit" aria-label={`Edit ${c.name}`}
                  onClick={() => open(setEditing, c)}><PencilSimple size={16} /></button>
                <button type="button" className="icon-btn danger" title="Delete" aria-label={`Delete ${c.name}`}
                  onClick={() => open(setDeleting, c)}><Trash size={16} /></button>
              </span>
            </li>
          ))}
        </ul>
      )}

      <Dialog open={editing !== null} title={editing === 'new' ? 'Add client' : 'Edit client'} onClose={() => setEditing(null)}>
        <ClientForm client={editing === 'new' ? null : editing} onSaved={saved} onCancel={() => setEditing(null)} />
      </Dialog>

      <Dialog open={deleting !== null} title={`Delete ${deleting?.name}?`} onClose={() => setDeleting(null)}>
        <DeleteClient client={deleting} onDeleted={deleted} onCancel={() => setDeleting(null)} />
      </Dialog>
    </>
  )
}

function ClientForm({ client, onSaved, onCancel }) {
  const [form, setForm] = useState(() => ({
    name: client?.name ?? '', email: client?.email ?? '', company: client?.company ?? '', address: client?.address ?? '',
  }))
  const [errors, setErrors] = useState({})
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const set = (key) => (value) => setForm((f) => ({ ...f, [key]: value }))

  async function handleSubmit(e) {
    e.preventDefault()
    setBusy(true)
    setErrors({})
    setError('')
    try {
      // Only these four fields are sent. The owner comes from your session, never from the form.
      const res = client ? await putJson(`/api/clients/${client.id}`, form) : await postJson('/api/clients', form)
      if (res.ok) {
        onSaved(res.data)
        return
      }
      if (res.status === 400) setErrors(fieldErrors(res.data))
      else if (res.status === 404) setError('This client no longer exists. Refresh the page.')
      else setError('Something went wrong. Try again.')
    } catch {
      setError('Can’t reach the server. Try again in a moment.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <form onSubmit={handleSubmit}>
      {error && <p className="notice notice--error" role="alert">{error}</p>}
      <fieldset className="dialog-fields" disabled={busy}>
        <TextField id="client-name" label="Name" value={form.name} onChange={set('name')}
          maxLength={200} required autoComplete="off" error={errors.name} />
        <TextField id="client-email" label="Email" type="email" value={form.email} onChange={set('email')}
          maxLength={254} required autoComplete="off" autoCapitalize="none" spellCheck={false} error={errors.email} />
        <TextField id="client-company" label="Company" optional value={form.company} onChange={set('company')}
          maxLength={200} autoComplete="off" error={errors.company} />
        <div className="field">
          <label htmlFor="client-address">Address <span className="optional">(optional)</span></label>
          <textarea id="client-address" className="input" rows={3} maxLength={500} value={form.address}
            onChange={(e) => set('address')(e.target.value)} aria-invalid={Boolean(errors.address)} />
          {errors.address && <p className="field-error">{errors.address}</p>}
        </div>
      </fieldset>
      <div className="dialog-actions">
        <button type="button" className="btn-secondary" onClick={onCancel}>Cancel</button>
        <button type="submit" className="btn-primary" disabled={busy}>
          {busy ? 'Saving…' : client ? 'Save changes' : 'Add client'}
        </button>
      </div>
    </form>
  )
}

function DeleteClient({ client, onDeleted, onCancel }) {
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  async function remove() {
    setBusy(true)
    setError('')
    try {
      const res = await deleteJson(`/api/clients/${client.id}`)
      // 404: it's already gone, which is what you wanted
      if (res.ok || res.status === 404) {
        onDeleted(client)
        return
      }
      setError(res.status === 409
        ? 'This client has invoices, so it can’t be deleted. Invoices need to keep their client.'
        : 'Something went wrong. Try again.')
    } catch {
      setError('Can’t reach the server. Try again in a moment.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      <p className="dialog-text">This can’t be undone.</p>
      {error && <p className="notice notice--error" role="alert">{error}</p>}
      <div className="dialog-actions">
        <button type="button" className="btn-secondary" onClick={onCancel} disabled={busy}>Cancel</button>
        <button type="button" className="btn-danger" onClick={remove} disabled={busy}>
          <Trash size={16} />{busy ? 'Deleting…' : 'Delete client'}
        </button>
      </div>
    </>
  )
}