import { useEffect, useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { ArrowRight } from '@phosphor-icons/react'
import AuthLayout from '../components/AuthLayout.jsx'
import PasswordField from '../components/PasswordField.jsx'
import { useAuth } from '../auth/AuthContext.js'
import { fieldErrors, postJson } from '../api.js'

const MIN_LENGTH = 15
const BAD_LINK = 'This link is invalid, has expired or has already been used. Ask for a new one below.'

export default function ResetPassword() {
  const [params] = useSearchParams()
  const userId = params.get('user') ?? ''
  const code = params.get('code') ?? ''
  const { refresh } = useAuth()
  const [password, setPassword] = useState('')
  const [fieldError, setFieldError] = useState('') // about the password, shown under the field
  const [notice, setNotice] = useState('')         // 429, network
  const [rejected, setRejected] = useState(false)  // the link itself was refused
  const [done, setDone] = useState(false)
  const [busy, setBusy] = useState(false)

  // The link carries a secret: never pass it on as a referrer, and keep the page out of search results
  useEffect(() => {
    document.title = 'Reset password · Invoiceflow'
    const tags = [['referrer', 'no-referrer'], ['robots', 'noindex']].map(([name, content]) => {
      const meta = document.createElement('meta')
      meta.name = name
      meta.content = content
      document.head.appendChild(meta)
      return meta
    })
    return () => tags.forEach((meta) => meta.remove())
  }, [])

  async function handleSubmit(e) {
    e.preventDefault()
    setBusy(true)
    setFieldError('')
    setNotice('')
    try {
      const res = await postJson('/api/auth/reset-password', { userId, code, password })
      if (res.ok) {
        await refresh() // the API signed this browser out too
        setDone(true)
        return
      }
      const found = res.status === 400 ? fieldErrors(res.data) : {}
      if (found.password) setFieldError(found.password)
      else if (res.status === 400) setRejected(true)
      else setNotice(res.status === 429 ? 'Too many attempts. Wait a minute, then try again.' : 'Something went wrong. Try again.')
    } catch {
      setNotice('Can’t reach the server. Try again in a moment.')
    } finally {
      setBusy(false)
    }
  }

  const linkBad = !userId || !code || rejected
  const count = Math.min(password.length, MIN_LENGTH)

  return (
    <AuthLayout topLink={<span>Remembered it? <Link to="/sign-in">Sign in</Link></span>}>
      <p className="auth-eyebrow">Password reset</p>
      {done ? (
        <>
          <h1 className="auth-title">Password changed</h1>
          <p className="auth-lede" role="status">Every device that was signed in is signed out within a minute, including any someone else may have been using. Sign in with your new password.</p>
          <Link className="btn-primary" to="/sign-in" state={{ reset: true }}>Sign in<ArrowRight size={16} /></Link>
        </>
      ) : linkBad ? (
        <>
          <h1 className="auth-title">Link not valid</h1>
          <p className="notice notice--error" role="alert">{BAD_LINK}</p>
          <Link className="btn-primary" to="/forgot-password">Send a new link<ArrowRight size={16} /></Link>
        </>
      ) : (
        <>
          <h1 className="auth-title">Choose a new password</h1>
          <p className="auth-lede">Afterwards you’ll be signed out everywhere, then you sign in with the new one.</p>
          {notice && <p className="notice notice--error" role="alert">{notice}</p>}
          <form className="auth-fields" onSubmit={handleSubmit}>
            <PasswordField id="password" label="New password" value={password} onChange={setPassword} autoComplete="new-password"
              error={fieldError} hint={`At least ${MIN_LENGTH} characters. A few random words work well (${count}/${MIN_LENGTH}).`} />
            <button className="btn-primary" type="submit" disabled={busy}>
              {busy ? 'Saving…' : 'Change password'}<ArrowRight size={16} />
            </button>
          </form>
        </>
      )}
    </AuthLayout>
  )
}