import { useState } from 'react'
import QRCode from 'qrcode'
import { Copy, DeviceMobile, DownloadSimple, Key, ShieldCheck } from '@phosphor-icons/react'
import Dialog from '../components/Dialog.jsx'
import PasswordField from '../components/PasswordField.jsx'
import TextField from '../components/TextField.jsx'
import { useAuth } from '../auth/AuthContext.js'
import { fieldErrors, postJson } from '../api.js'
import '../styles/security.css'

const CODE_COUNT = 10
const CODE_ERROR = 'That code didn’t work. Use the newest code in your app, and check your phone sets its time automatically.'
const OFF_ERROR = 'That code didn’t work. Use the newest code in your app, or a recovery code you haven’t used.'
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
  const [open, setOpen] = useState(null) // 'setup' | 'codes' | 'off' | null
  const [notice, setNotice] = useState('')
  const on = Boolean(user?.twoFactorEnabled)
  const left = user?.recoveryCodesLeft ?? 0

  function show(which) {
    setNotice('')
    setOpen(which)
  }

  // However a dialog closes, re-read the account: turning 2FA on has already happened by the time the codes show
  async function done(text = '') {
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

        {on && (
          <section className="sec-card" aria-labelledby="rc-title">
            <span className="sec-icon" aria-hidden="true"><Key size={20} /></span>
            <div className="sec-text">
              <div className="sec-title-row">
                <h2 id="rc-title" className="sec-title">Recovery codes</h2>
                <span className={left > 3 ? 'sec-status sec-status--on' : 'sec-status sec-status--low'}>{left} of {CODE_COUNT} left</span>
              </div>
              <p className="sec-body">
                {left > 3
                  ? 'If you lose your phone, sign in with one of these instead of a code. Each one works once.'
                  : left > 0
                    ? 'You’re running low. Make a new set and keep it somewhere safe. The old ones stop working.'
                    : 'You have none left. Make a new set now, so a lost phone can’t lock you out.'}
              </p>
            </div>
            <button type="button" className="btn-secondary" onClick={() => show('codes')}>New codes</button>
          </section>
        )}
      </div>

      <Dialog open={open === 'setup'} title="Set up two-factor sign-in" onClose={() => done()}>
        <SetupSteps onDone={done} onCancel={() => done()} />
      </Dialog>
      <Dialog open={open === 'codes'} title="New recovery codes" onClose={() => done()}>
        <NewCodes onDone={done} onCancel={() => done()} />
      </Dialog>
      <Dialog open={open === 'off'} title="Turn off two-factor sign-in?" onClose={() => setOpen(null)}>
        <TurnOff onDone={done} onCancel={() => setOpen(null)} />
      </Dialog>
    </>
  )
}

// Step 1: confirm it's you. Step 2: scan the secret and enter the first code. Step 3: save the recovery codes.
function SetupSteps({ onDone, onCancel }) {
  const [password, setPassword] = useState('')
  const [secret, setSecret] = useState(null) // { sharedKey, qr }
  const [code, setCode] = useState('')
  const [codes, setCodes] = useState(null)
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
    if (r.done) {
      setCodes(r.data.codes)
      setBusy(false)
      return
    }
    if (r.conflict) {
      onDone('')
      return
    }
    if (r.notice) setNotice(r.notice)
    else setFieldError(r.fieldError ?? CODE_ERROR)
    setBusy(false)
  }

  if (codes) {
    return (
      <SavedCodes codes={codes}
        intro="Two-factor sign-in is on. If you lose your phone, each of these codes lets you sign in once."
        onDone={() => onDone('Two-factor sign-in is on. Any other device that was signed in is signed out within a minute.')} />
    )
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

// A fresh set, confirmed with the app. Every old code stops working.
function NewCodes({ onDone, onCancel }) {
  const [code, setCode] = useState('')
  const [codes, setCodes] = useState(null)
  const [fieldError, setFieldError] = useState('')
  const [notice, setNotice] = useState('')
  const [busy, setBusy] = useState(false)

  async function makeNew(e) {
    e.preventDefault()
    setBusy(true)
    setFieldError('')
    setNotice('')
    const r = await submit('/api/auth/2fa/recovery-codes', { code }, 'code')
    if (r.done) {
      setCodes(r.data.codes)
      setBusy(false)
      return
    }
    if (r.conflict) {
      onDone('')
      return
    }
    if (r.notice) setNotice(r.notice)
    else setFieldError(r.fieldError ?? CODE_ERROR)
    setBusy(false)
  }

  if (codes) {
    return (
      <SavedCodes codes={codes} intro="Here’s your new set. The old codes no longer work."
        onDone={() => onDone('New recovery codes are ready. The old ones no longer work.')} />
    )
  }

  return (
    <form onSubmit={makeNew}>
      <p className="dialog-text">
        You’ll get {CODE_COUNT} new codes, and every old one stops working, used or not. To confirm, enter a current code from your authenticator app.
      </p>
      {notice && <p className="notice notice--error" role="alert">{notice}</p>}
      <div className="dialog-fields">
        <TextField id="rc-code" label="6-digit code" value={code} onChange={setCode} error={fieldError}
          inputMode="numeric" autoComplete="one-time-code" maxLength={9} required />
      </div>
      <div className="dialog-actions">
        <button type="button" className="btn-secondary" onClick={onCancel} disabled={busy}>Cancel</button>
        <button type="submit" className="btn-primary" disabled={busy}>{busy ? 'Checking…' : 'Make new codes'}</button>
      </div>
    </form>
  )
}

// Shown once. The server keeps only hashes, so these can never be shown again.
function SavedCodes({ codes, intro, onDone }) {
  const [saved, setSaved] = useState(false)
  const [copied, setCopied] = useState(false)
  const text = `Invoiceflow recovery codes\nEach code works once. Keep them somewhere safe, away from your phone.\n\n${codes.join('\n')}\n`

  async function copy() {
    try {
      await navigator.clipboard.writeText(text)
      setCopied(true)
    } catch {
      setCopied(false)
    }
  }

  function download() {
    const href = URL.createObjectURL(new Blob([text], { type: 'text/plain' }))
    const a = document.createElement('a')
    a.href = href
    a.download = 'invoiceflow-recovery-codes.txt'
    a.click()
    setTimeout(() => URL.revokeObjectURL(href), 1000)
  }

  return (
    <>
      <p className="dialog-text">{intro} Save them now: this is the only time they’re shown.</p>
      <ol className="rc-list">{codes.map((c) => <li key={c}>{c}</li>)}</ol>
      <div className="rc-actions">
        <button type="button" className="btn-secondary" onClick={download}><DownloadSimple size={16} />Download</button>
        <button type="button" className="btn-secondary" onClick={copy}><Copy size={16} />{copied ? 'Copied' : 'Copy'}</button>
      </div>
      <label className="rc-check">
        <input type="checkbox" checked={saved} onChange={(e) => setSaved(e.target.checked)} />
        <span>I’ve saved these somewhere safe</span>
      </label>
      <div className="dialog-actions">
        <button type="button" className="btn-primary" onClick={onDone} disabled={!saved}>Done</button>
      </div>
    </>
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
    else setFieldError(r.fieldError ?? OFF_ERROR)
    setBusy(false)
  }

  return (
    <form onSubmit={turnOff}>
      <p className="dialog-text">
        Your password alone will protect your account. To confirm, enter a current code from your authenticator app. Lost your phone? Use one of your recovery codes instead.
      </p>
      {notice && <p className="notice notice--error" role="alert">{notice}</p>}
      <div className="dialog-fields">
        <TextField id="tfa-off-code" label="Code from your app, or a recovery code" value={code} onChange={setCode} error={fieldError}
          autoComplete="one-time-code" autoCapitalize="characters" spellCheck={false} maxLength={32} required />
      </div>
      <div className="dialog-actions">
        <button type="button" className="btn-secondary" onClick={onCancel} disabled={busy}>Cancel</button>
        <button type="submit" className="btn-danger" disabled={busy}>{busy ? 'Checking…' : 'Turn off'}</button>
      </div>
    </form>
  )
}