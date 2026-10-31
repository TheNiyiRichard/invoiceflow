import { useEffect, useState } from 'react'
import { Receipt, SignOut } from '@phosphor-icons/react'
import { useAuth } from '../auth/AuthContext.js'
import { getJson } from '../api.js'
import '../styles/auth.css'

export default function Home() {
  const { user, signOut } = useAuth()
  const [clients, setClients] = useState(null)

  useEffect(() => {
    const controller = new AbortController()
    getJson('/api/clients', controller.signal)
      .then((res) => { if (res.ok) setClients(res.data) })
      .catch(() => {})
    return () => controller.abort()
  }, [])

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
      <section>
        <p className="auth-eyebrow">Your clients</p>
        {clients?.length === 0 && <p className="field-hint">No clients yet.</p>}
        {clients?.length > 0 && (
          <ul className="client-list">
            {clients.map((c) => <li key={c.id}>{c.name}<span>{c.email}</span></li>)}
          </ul>
        )}
      </section>
    </main>
  )
}