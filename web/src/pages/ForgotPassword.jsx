import { useState } from 'react'
import { Link, useLocation } from 'react-router'
import { ArrowRight, EnvelopeSimple, ShieldCheck } from '@phosphor-icons/react'
import AuthLayout from '../components/AuthLayout.jsx'
import { postJson } from '../api.js'

const ERRORS = {
  400: 'Enter a valid email address.',
  429: 'Too many attempts. Wait a minute, then try again.',
}

export default function ForgotPassword() {
  const { state } = useLocation()
  const [email, setEmail] = useState(state?.email ?? '')
  const [sentTo, setSentTo] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  async function handleSubmit(e) {
    e.preventDefault()
    setBusy(true)
    setError('')
    try {
      const res = await postJson('/api/auth/forgot-password', { email })
      if (res.ok) setSentTo(email.trim())
      else setError(ERRORS[res.status] ?? 'Something went wrong. Try again.')
    } catch {
      setError('Can’t reach the server. Try again in a moment.')
    } finally {
      setBusy(false)
    }
  }

  const topLink = <span>Remembered it? <Link to="/sign-in">Sign in</Link></span>

  // The same page whether or not the account exists
  if (sentTo) {
    return (
      <AuthLayout topLink={topLink}>
        <p className="auth-eyebrow">Password reset</p>
        <h1 className="auth-title">Check your email</h1>
        <p className="auth-lede" role="status">
          If there’s an Invoiceflow account for <strong>{sentTo}</strong>, we’ve sent it a link to choose a new password. The link works once, for 1 hour.
        </p>
        <Link className="btn-primary" to="/sign-in" state={{ email: sentTo }}>Back to sign in<ArrowRight size={16} /></Link>
        <div className="auth-note">
          <EnvelopeSimple size={18} />
          <span>Nothing there after a few minutes? Check your spam folder, then try again.</span>
        </div>
      </AuthLayout>
    )
  }

  return (
    <AuthLayout topLink={topLink}>
      <p className="auth-eyebrow">Password reset</p>
      <h1 className="auth-title">Forgot your password?</h1>
      <p className="auth-lede">Enter the email you sign in with, and we’ll send you a link to choose a new one.</p>

      {error && <p className="notice notice--error" role="alert">{error}</p>}

      <form className="auth-fields" onSubmit={handleSubmit}>
        <div className="field">
          <label htmlFor="email">Email</label>
          <input id="email" className="input" type="email" value={email} onChange={(e) => setEmail(e.target.value)}
            autoComplete="email" autoCapitalize="none" spellCheck={false} maxLength={254} required />
        </div>
        <button className="btn-primary" type="submit" disabled={busy}>
          {busy ? 'Sending…' : 'Send reset link'}<ArrowRight size={16} />
        </button>
      </form>

      <div className="auth-note">
        <ShieldCheck size={18} className="ok" />
        <span>We give the same reply whether or not an account exists, so nobody can use this page to find out who has one.</span>
      </div>
    </AuthLayout>
  )
}