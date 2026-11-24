import { useEffect, useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { ArrowRight } from '@phosphor-icons/react'
import AuthLayout from '../components/AuthLayout.jsx'
import { useAuth } from '../auth/AuthContext.js'
import { postJson } from '../api.js'

const PROBLEMS = {
  missing: 'This link is incomplete. Open it again from the email, or use “Resend email” in Invoiceflow to get a new one.',
  400: 'This link is invalid or has expired. Use “Resend email” at the top of Invoiceflow to get a new one.',
  429: 'Too many attempts. Wait a minute, then refresh this page.',
}

export default function VerifyEmail() {
  const [params] = useSearchParams()
  const userId = params.get('user') ?? ''
  const code = params.get('code') ?? ''
  const key = `${userId}:${code}`
  const { user, refresh } = useAuth()
  const [result, setResult] = useState({ key: null, ok: false, problem: '' })

  // The link carries a secret: never pass it on as a referrer, and keep the page out of search results
  useEffect(() => {
    document.title = 'Verify email · Invoiceflow'
    const tags = [['referrer', 'no-referrer'], ['robots', 'noindex']].map(([name, content]) => {
      const meta = document.createElement('meta')
      meta.name = name
      meta.content = content
      document.head.appendChild(meta)
      return meta
    })
    return () => tags.forEach((meta) => meta.remove())
  }, [])

  // Verifying changes something, so the page POSTs it. Opening the link (a GET) never does.
  useEffect(() => {
    if (!userId || !code) return
    let cancelled = false
    postJson('/api/auth/verify-email', { userId, code })
      .then(async (res) => {
        if (cancelled) return
        if (res.ok) {
          await refresh() // the banner disappears straight away
          if (!cancelled) setResult({ key, ok: true, problem: '' })
        } else {
          setResult({ key, ok: false, problem: PROBLEMS[res.status] ?? 'Something went wrong. Refresh to try again.' })
        }
      })
      .catch(() => { if (!cancelled) setResult({ key, ok: false, problem: 'Can’t reach the server. Refresh to try again.' }) })
    return () => { cancelled = true }
  }, [userId, code, key, refresh])

  const problem = !userId || !code ? PROBLEMS.missing : result.key === key ? result.problem : ''
  const ok = result.key === key && result.ok
  const next = user
    ? <Link className="btn-primary" to="/">Go to your dashboard<ArrowRight size={16} /></Link>
    : <Link className="btn-primary" to="/sign-in">Sign in<ArrowRight size={16} /></Link>

  return (
    <AuthLayout topLink={user ? <Link to="/">Back to Invoiceflow</Link> : <span>Have an account? <Link to="/sign-in">Sign in</Link></span>}>
      <p className="auth-eyebrow">Email verification</p>
      {problem ? (
        <>
          <h1 className="auth-title">Couldn’t verify</h1>
          <p className="notice notice--error" role="alert">{problem}</p>
          {next}
        </>
      ) : ok ? (
        <>
          <h1 className="auth-title">Email verified</h1>
          <p className="auth-lede" role="status">Thanks. Your account can send invoices and reminders now.</p>
          {next}
        </>
      ) : (
        <>
          <h1 className="auth-title">Checking your link…</h1>
          <p className="auth-lede" role="status">This only takes a moment.</p>
        </>
      )}
    </AuthLayout>
  )
}