import { useState } from 'react'
import { Link, useLocation } from 'react-router'
import { ArrowLeft, ArrowRight, DeviceMobile, ShieldWarning } from '@phosphor-icons/react'
import AuthLayout from '../components/AuthLayout.jsx'
import PasswordField from '../components/PasswordField.jsx'
import TextField from '../components/TextField.jsx'
import { useAuth } from '../auth/AuthContext.js'
import { fieldErrors, postJson } from '../api.js'

const messages = {
  400: 'Invalid email or password.',
  401: 'Invalid email or password.',
  429: 'Too many attempts. Wait a minute, then try again.',
}
const CODE_ERROR = 'That code didn’t work. Use the newest code in your app.'

export default function SignIn() {
  const { refresh } = useAuth()
  const { state } = useLocation()
  const [email, setEmail] = useState(state?.email ?? '')
  const [password, setPassword] = useState('')
  const [step, setStep] = useState('password') // 'password' | 'code'
  const [code, setCode] = useState('')
  const [trustDevice, setTrustDevice] = useState(false)
  const [codeError, setCodeError] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  const topLink = <span>New to Invoiceflow? <Link to="/register">Create an account</Link></span>

  async function handleSubmit(e) {
    e.preventDefault()
    setBusy(true)
    setError('')
    try {
      const res = await postJson('/api/auth/login', { email, password })
      if (res.status === 204) {
        await refresh() // GuestRoute then sends you to the page you came from
        return
      }
      setPassword('') // not needed again either way
      if (res.ok && res.data?.twoFactorRequired) setStep('code')
      else setError(messages[res.status] ?? 'Something went wrong. Try again.')
    } catch {
      setError('Can’t reach the server. Try again in a moment.')
    } finally {
      setBusy(false)
    }
  }

  async function handleCode(e) {
    e.preventDefault()
    setBusy(true)
    setError('')
    setCodeError('')
    try {
      const res = await postJson('/api/auth/login/2fa', { code, trustDevice })
      if (res.ok) {
        await refresh()
        return
      }
      if (res.status === 400) {
        setCode('')
        setCodeError(fieldErrors(res.data).code ?? CODE_ERROR)
      } else if (res.status === 401) {
        startOver(res.data?.title ?? 'Your sign-in timed out. Enter your email and password again.')
      } else {
        setError(messages[res.status] ?? 'Something went wrong. Try again.')
      }
    } catch {
      setError('Can’t reach the server. Try again in a moment.')
    } finally {
      setBusy(false)
    }
  }

  function startOver(message = '') {
    setStep('password')
    setCode('')
    setCodeError('')
    setError(message)
  }

  if (step === 'code') {
    return (
      <AuthLayout topLink={topLink}>
        <p className="auth-eyebrow">Two-factor sign-in</p>
        <h1 className="auth-title">Enter your code</h1>
        <p className="auth-lede">Open your authenticator app and enter the 6-digit code for Invoiceflow ({email}).</p>

        {error && <p className="notice notice--error" role="alert">{error}</p>}

        <form className="auth-fields" onSubmit={handleCode}>
          <TextField id="code" label="6-digit code" value={code} onChange={setCode} error={codeError}
            inputMode="numeric" autoComplete="one-time-code" maxLength={9} autoFocus required />
          <label className="auth-check">
            <input type="checkbox" checked={trustDevice} onChange={(e) => setTrustDevice(e.target.checked)} />
            <span>
              Trust this device for 30 days
              <span className="auth-check-hint">You’ll still need your password. Only tick this on your own device.</span>
            </span>
          </label>
          <button className="btn-primary" type="submit" disabled={busy}>
            {busy ? 'Checking…' : 'Verify'}<ArrowRight size={16} />
          </button>
        </form>
        <button type="button" className="auth-back" onClick={() => startOver()}><ArrowLeft size={14} />Back to email and password</button>

        <div className="auth-note">
          <DeviceMobile size={18} className="ok" />
          <span>Codes change every 30 seconds, and each one works only once.</span>
        </div>
      </AuthLayout>
    )
  }

  return (
    <AuthLayout topLink={topLink}>
      <p className="auth-eyebrow">Welcome back</p>
      <h1 className="auth-title">Sign in</h1>
      <p className="auth-lede">Use the email and password for your Invoiceflow account.</p>

      {state?.registered && !error && <p className="notice notice--ok" role="status">Account created. We’ve emailed a link to verify {state.email}. Sign in to continue.</p>}
      {state?.reset && !error && <p className="notice notice--ok" role="status">Password changed. Sign in with your new password.</p>}
      {error && <p className="notice notice--error" role="alert">{error}</p>}

      <form className="auth-fields" onSubmit={handleSubmit}>
        <div className="field">
          <label htmlFor="email">Email</label>
          <input id="email" className="input" type="email" value={email} onChange={(e) => setEmail(e.target.value)}
            autoComplete="email" autoCapitalize="none" spellCheck={false} maxLength={254} required />
        </div>
        <PasswordField id="password" label="Password" value={password} onChange={setPassword} autoComplete="current-password" />
        <Link className="auth-forgot" to="/forgot-password" state={{ email }}>Forgot password?</Link>
        <button className="btn-primary" type="submit" disabled={busy}>
          {busy ? 'Signing in…' : 'Sign in'}<ArrowRight size={16} />
        </button>
      </form>

      <div className="auth-note">
        <ShieldWarning size={18} />
        <span>After 5 wrong passwords or codes, sign-in locks for 15 minutes.</span>
      </div>
    </AuthLayout>
  )
}