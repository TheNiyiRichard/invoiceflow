import { useState } from 'react'
import QRCode from 'qrcode'
import { DeviceMobile, ShieldCheck } from '@phosphor-icons/react'
import Dialog from '../components/Dialog.jsx'
import PasswordField from '../components/PasswordField.jsx'
import TextField from '../components/TextField.jsx'
import { useAuth } from '../auth/AuthContext.js'
import { fieldErrors, postJson } from '../api.js'
import '../styles/security.css'

const CODE_ERROR = 'That code didn’t work. Use the newest code in your app, and check your phone sets its time automatically.'
const TROUBLE = {
  429: 'Too many attempts. Wait a minute, then try again.',
  offline: 'Can’t reach the server. Try again in a moment.',
  failed: 'Something went wrong. Try again.',
}

// Posts, then sorts the answer: done, a message for the field, a notice, or "already changed in another tab"
async function submit(url, body, field) {
  try {
    const res = await postJson(url, body)
    if (res.ok) return { done: true, data: res.data }
    if (res.status === 409) return { conflict: true }
    if (res.status === 400) return { fieldError: fieldErrors(res.data)[field] ?? null }
    return { notice: TROUBLE[res.status] ?? TROUBLE.failed }
  } catch {
    return { notice: TROUBLE.offline }
  }
}

export default function Security() {
  const { user, refresh } = useAuth()
  const [open, setOpen] = useState(null) // 'setup' | 'off' | null
  const [notice, setNotice] = useState('')
  const on = Boolean(user?.twoFactorEnabled)

  function show(which) {
    setNotice('')
    setOpen(which)
  }

  async function done(text) {
    setOpen(null)
    await refresh()
    setNotice(text)
  }

  return (
    <>
      <h1 className="page-title">Security</h1>
      <p className="page-lede">How you sign in to Invoiceflow.</p>

      {notice && <p className="notice notice--ok" role="status">{notice}</p>}

      <div className="sec-list">
        <section className="sec-card" aria-labelledby="tfa-title">
          <span className="sec-icon" aria-hidden="true"><DeviceMobile size={20} /></span>
          <div className="sec-text">
            <div className="sec-title-row">
              <h2 id="tfa-title" className="sec-title">Two-factor authentication</h2>
              <span className={on ? 'sec-status sec-status--on' : 'sec-status'}>{on ? 'On' : 'Off'}</span>
            </div>
            <p className="sec-body">
              {on
                ? 'Signing in takes your password and a code from your authenticator app.'
                : 'Add a code from an authenticator app to your sign-in, so a stolen password isn’t enough to get in.'}
            </p>
          </div>
          {on
            ? <button type="button" className="btn-secondary" onClick={() => show('off')}>Turn off</button>
            : <button type="button" className="btn-primary" onClick={() => show('setup')}><ShieldCheck size={16} />Set up</button>}
        </section>
      </div>

      <Dialog open={open === 'setup'} title="Set up two-factor sign-in" onClose={() => setOpen(null)}>
        <SetupSteps onDone={done} onCancel={() => setOpen(null)} />
      </Dialog>
      <Dialog open={open === 'off'} title="Turn off two-factor sign-in?" onClose={() => setOpen(null)}>
        <TurnOff onDone={done} onCancel={() => setOpen(null)} />
      </Dialog>
    </>
  )
}

// Step 1: confirm it's you. Step 2: scan the secret and enter the first code.
function SetupSteps({ onDone, onCancel }) {
  const [password, setPassword] = useState('')
  const [secret, setSecret] = useState(null) // { sharedKey, qr }
  const [code, setCode] = useState('')
  const [fieldError, setFieldError] = useState('')
  const [notice, setNotice] = useState('')
  const [busy, setBusy] = useState(false)

  async function confirmPassword(e) {
    e.preventDefault()
    setBusy(true)
    setFieldError('')
    setNotice('')
    const r = await submit('/api/auth/2fa/setup', { password }, 'password')
    if (r.done) {
      try {
        // Drawn here in the browser: the secret is never sent to a QR-code website.
        // Ink on sheet, from tokens.css (the QR library needs plain hex).
        const qr = await QRCode.toDataURL(r.data.authenticatorUri, { width: 176, margin: 1, color: { dark: '#151A17', light: '#FFFEFB' } })
        setSecret({ sharedKey: r.data.sharedKey, qr })
        setPassword('')
      } catch {
        setNotice(TROUBLE.failed)
      }
    } else if (r.conflict) {
      onDone('')
      return
    } else if (r.notice) {
      setNotice(r.notice)
    } else {
      setFieldError(r.fieldError ?? 'That password isn’t right.')
    }
    setBusy(false)
  }

  async function turnOn(e) {
    e.preventDefault()
    setBusy(true)
    setFieldError('')
    setNotice('')
    const r = await submit('/api/auth/2fa/enable', { code }, 'code')
    if (r.done || r.conflict) {
      onDone(r.done ? 'Two-factor sign-in is on. Any other device that was signed in is signed out within a minute.' : '')
      return
    }
    if (r.notice) setNotice(r.notice)
    else setFieldError(r.fieldError ?? CODE_ERROR)
    setBusy(false)
  }

  if (!secret) {
    return (
      <form onSubmit={confirmPassword}>
        <p className="dialog-text">
          First, confirm it’s you. Then you’ll scan a QR code with an authenticator app, like Google Authenticator, Microsoft Authenticator or 1Password.
        </p>
        {notice && <p className="notice notice--error" role="alert">{notice}</p>}
        <div className="dialog-fields">
          <PasswordField id="tfa-password" label="Your password" value={password} onChange={setPassword}
            autoComplete="current-password" error={fieldError} />
        </div>
        <div className="dialog-actions">
          <button type="button" className="btn-secondary" onClick={onCancel} disabled={busy}>Cancel</button>
          <button type="submit" className="btn-primary" disabled={busy}>{busy ? 'Checking…' : 'Continue'}</button>
        </div>
      </form>
    )
  }

  return (
    <form onSubmit={turnOn}>
      <p className="dialog-text">Scan this with your authenticator app, then enter the 6-digit code it shows.</p>
      <div className="tfa-scan">
        <img className="tfa-qr" src={secret.qr} width="176" height="176" alt="QR code that adds Invoiceflow to your authenticator app" />
        <div>
          <p className="tfa-key-label">Can’t scan it? Type this key</p>
          <code className="tfa-key">{secret.sharedKey}</code>
        </div>
      </div>
      {notice && <p className="notice notice--error" role="alert">{notice}</p>}
      <div className="dialog-fields">
        <TextField id="tfa-code" label="6-digit code" value={code} onChange={setCode} error={fieldError}
          inputMode="numeric" autoComplete="one-time-code" maxLength={9} required />
      </div>
      <div className="dialog-actions">
        <button type="button" className="btn-secondary" onClick={onCancel} disabled={busy}>Cancel</button>
        <button type="submit" className="btn-primary" disabled={busy}>{busy ? 'Checking…' : 'Turn on'}</button>
      </div>
    </form>
  )
}

function TurnOff({ onDone, onCancel }) {
  const [code, setCode] = useState('')
  const [fieldError, setFieldError] = useState('')
  const [notice, setNotice] = useState('')
  const [busy, setBusy] = useState(false)

  async function turnOff(e) {
    e.preventDefault()
    setBusy(true)
    setFieldError('')
    setNotice('')
    const r = await submit('/api/auth/2fa/disable', { code }, 'code')
    if (r.done) {
      onDone('Two-factor sign-in is off. Any other device that was signed in is signed out within a minute.')
      return
    }
    if (r.notice) setNotice(r.notice)
    else setFieldError(r.fieldError ?? CODE_ERROR)
    setBusy(false)
  }

  return (
    <form onSubmit={turnOff}>
      <p className="dialog-text">
        Your password alone will protect your account. To confirm, enter a current code from your authenticator app.
      </p>
      {notice && <p className="notice notice--error" role="alert">{notice}</p>}
      <div className="dialog-fields">
        <TextField id="tfa-off-code" label="6-digit code" value={code} onChange={setCode} error={fieldError}
          inputMode="numeric" autoComplete="one-time-code" maxLength={9} required />
      </div>
      <div className="dialog-actions">
        <button type="button" className="btn-secondary" onClick={onCancel} disabled={busy}>Cancel</button>
        <button type="submit" className="btn-danger" disabled={busy}>{busy ? 'Checking…' : 'Turn off'}</button>
      </div>
    </form>
  )
}