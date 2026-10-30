import { useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { ArrowRight, ShieldCheck } from '@phosphor-icons/react'
import AuthLayout from '../components/AuthLayout.jsx'
import PasswordField from '../components/PasswordField.jsx'
import TextField from '../components/TextField.jsx'
import { fieldErrors, postJson } from '../api.js'

const MIN_LENGTH = 15

export default function Register() {
  const navigate = useNavigate()
  const [form, setForm] = useState({ fullName: '', businessName: '', email: '', password: '' })
  const [errors, setErrors] = useState({})
  const [busy, setBusy] = useState(false)
  const set = (key) => (value) => setForm((f) => ({ ...f, [key]: value }))

  async function handleSubmit(e) {
    e.preventDefault()
    setBusy(true)
    setErrors({})
    try {
      const res = await postJson('/api/auth/register', form)
      if (res.ok) {
        navigate('/sign-in', { replace: true, state: { registered: true, email: form.email } })
        return
      }
      if (res.status === 429) {
        setErrors({ form: 'Too many attempts. Wait a minute, then try again.' })
      } else if (res.status === 400) {
        const found = fieldErrors(res.data)
        setErrors(Object.keys(found).length ? found : { form: 'Check your details and try again.' })
      } else {
        setErrors({ form: 'Something went wrong. Try again.' })
      }
    } catch {
      setErrors({ form: 'Can’t reach the server. Try again in a moment.' })
    } finally {
      setBusy(false)
    }
  }

  const count = Math.min(form.password.length, MIN_LENGTH)

  return (
    <AuthLayout topLink={<span>Already have an account? <Link to="/sign-in">Sign in</Link></span>}>
      <p className="auth-eyebrow">Get started</p>
      <h1 className="auth-title">Create your account</h1>
      <p className="auth-lede">Invoicing for freelancers, with security built in.</p>

      {errors.form && <p className="notice notice--error" role="alert">{errors.form}</p>}

      <form className="auth-fields" onSubmit={handleSubmit}>
        <TextField id="fullName" label="Full name" value={form.fullName} onChange={set('fullName')}
          autoComplete="name" maxLength={100} required error={errors.fullname} />
        <TextField id="businessName" label="Business name" optional value={form.businessName} onChange={set('businessName')}
          autoComplete="organization" maxLength={200} error={errors.businessname} hint="Shown on your invoices. You can add it later." />
        <TextField id="email" label="Email" type="email" value={form.email} onChange={set('email')}
          autoComplete="email" autoCapitalize="none" spellCheck={false} maxLength={254} required error={errors.email} />
        <PasswordField id="password" label="Password" value={form.password} onChange={set('password')} autoComplete="new-password"
          error={errors.password} hint={`At least ${MIN_LENGTH} characters. A few random words work well (${count}/${MIN_LENGTH}).`} />
        <button className="btn-primary" type="submit" disabled={busy}>
          {busy ? 'Creating account…' : 'Create account'}<ArrowRight size={16} />
        </button>
      </form>

      <div className="auth-note">
        <ShieldCheck size={18} className="ok" />
        <span>Your password is never stored, only a salted hash of it.</span>
      </div>
    </AuthLayout>
  )
}