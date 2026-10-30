import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router'
import { Receipt, SignOut } from '@phosphor-icons/react'
import { postJson } from '../api.js'
import '../styles/auth.css'

export default function Home() {
  const navigate = useNavigate()
  const [user, setUser] = useState(null)

  useEffect(() => {
    const controller = new AbortController()
    fetch('/api/auth/me', { signal: controller.signal })
      .then((res) => {
        if (!res.ok) throw new Error(`HTTP ${res.status}`)
        return res.json()
      })
      .then(setUser)
      .catch((err) => {
        if (err.name !== 'AbortError') navigate('/sign-in', { replace: true })
      })
    return () => controller.abort()
  }, [navigate])

  async function signOut() {
    await postJson('/api/auth/logout')
    navigate('/sign-in', { replace: true })
  }

  if (!user) return null

  return (
    <main className="shell">
      <header className="brand">
        <span className="brand-mark"><Receipt size={18} /></span>
        <span className="brand-name">Invoice<em>flow</em></span>
      </header>
      <section className="hero">
        <p className="eyebrow">Signed in as {user.email}</p>
        <h1 className="auth-title">Welcome, {user.fullName || user.email}</h1>
        <button className="btn-secondary" type="button" onClick={signOut}><SignOut size={16} />Sign out</button>
      </section>
    </main>
  )
}