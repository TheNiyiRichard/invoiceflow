import { useState } from 'react'
import { Link, useLocation, useNavigate } from 'react-router'
import { ArrowRight, ShieldWarning } from '@phosphor-icons/react'
import AuthLayout from '../components/AuthLayout.jsx'
import PasswordField from '../components/PasswordField.jsx'
import { postJson } from '../api.js'

const messages = {
  400: 'Invalid email or password.',
  401: 'Invalid email or password.',
  429: 'Too many attempts. Wait a minute, then try again.',
}

export default function SignIn() {
  const navigate = useNavigate()
  const { state } = useLocation()
  const [email, setEmail] = useState(state?.email ?? '')
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  async function handleSubmit(e) {
    e.preventDefault()
    setBusy(true)
    setError('')
    try {
      const res = await postJson('/api/auth/login', { email, password })
      if (res.ok) {
        navigate('/', { replace: true })
        return
      }
      setPassword('')
      setError(messages[res.status] ?? 'Something went wrong. Try again.')
    } catch {
      setError('Can’t reach the server. Try again in a moment.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <AuthLayout topLink={<span>New to Invoiceflow? <Link to="/register">Create an account</Link></span>}>
      <p className="auth-eyebrow">Welcome back</p>
      <h1 className="auth-title">Sign in</h1>
      <p className="auth-lede">Use the email and password for your Invoiceflow account.</p>

      {state?.registered && !error && <p className="notice notice--ok" role="status">Account created. Sign in to continue.</p>}
      {error && <p className="notice notice--error" role="alert">{error}</p>}

      <form className="auth-fields" onSubmit={handleSubmit}>
        <div className="field">
          <label htmlFor="email">Email</label>
          <input id="email" className="input" type="email" value={email} onChange={(e) => setEmail(e.target.value)}
            autoComplete="email" autoCapitalize="none" spellCheck={false} maxLength={254} required />
        </div>
        <PasswordField id="password" label="Password" value={password} onChange={setPassword} autoComplete="current-password" />
        <button className="btn-primary" type="submit" disabled={busy}>
          {busy ? 'Signing in…' : 'Sign in'}<ArrowRight size={16} />
        </button>
      </form>

      <div className="auth-note">
        <ShieldWarning size={18} />
        <span>After 5 wrong passwords, sign-in locks for 15 minutes.</span>
      </div>
    </AuthLayout>
  )
}