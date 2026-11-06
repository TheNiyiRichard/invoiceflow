import { useEffect, useState } from 'react'
import { NavLink, Outlet, useLocation } from 'react-router'
import { CreditCard, List, LockSimple, Receipt, ShieldCheck, SignOut, SquaresFour, UsersThree, X } from '@phosphor-icons/react'
import { useAuth } from '../auth/AuthContext.js'
import { initials } from '../format.js'
import '../styles/app.css'

// The whole menu in one place. Adding a page = one line here + one route in App.jsx.
const sections = [
  { label: null, links: [
    { to: '/', label: 'Overview', icon: SquaresFour, end: true },
    { to: '/invoices', label: 'Invoices', icon: Receipt },
    { to: '/clients', label: 'Clients', icon: UsersThree },
    { to: '/payments', label: 'Payments', icon: CreditCard },
  ] },
  { label: 'Account', links: [
    { to: '/security', label: 'Security', icon: ShieldCheck },
  ] },
]
const links = sections.flatMap((s) => s.links)

export default function AppLayout() {
  const { user, signOut } = useAuth()
  const { pathname } = useLocation()
  const [menuOpen, setMenuOpen] = useState(false)

  const current = links.find((l) => (l.end ? pathname === l.to : pathname.startsWith(l.to)))
  const title = current?.label ?? 'Invoiceflow'
  const name = user?.fullName || user?.email || ''

  useEffect(() => { document.title = `${title} · Invoiceflow` }, [title])

  // Escape closes the phone menu
  useEffect(() => {
    if (!menuOpen) return
    const onKey = (e) => { if (e.key === 'Escape') setMenuOpen(false) }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [menuOpen])

  const close = () => setMenuOpen(false)

  return (
    <div className={menuOpen ? 'app menu-open' : 'app'}>
      <a className="skip-link" href="#content">Skip to content</a>

      <aside className="sidebar">
        <div className="sidebar-top">
          <NavLink to="/" className="logo" onClick={close}>
            <span className="logo-mark"><Receipt size={16} /></span>
            <span className="logo-name">Invoice<em>flow</em></span>
          </NavLink>
          <button type="button" className="icon-btn menu-close" aria-label="Close menu" onClick={close}><X size={18} /></button>
        </div>

        <nav className="nav" aria-label="Main">
          {sections.map((s) => (
            <div key={s.label ?? 'main'} className="nav-group">
              {s.label && <p className="nav-label">{s.label}</p>}
              {s.links.map(({ to, label, icon: Icon, end }) => (
                <NavLink key={to} to={to} end={end} className="nav-link" onClick={close}>
                  <Icon size={18} />{label}
                </NavLink>
              ))}
            </div>
          ))}
        </nav>

        <div className="sidebar-user">
          <span className="avatar" aria-hidden="true">{initials(name)}</span>
          <div className="user-text">
            <span className="user-name">{name}</span>
            <span className="user-email">{user?.email}</span>
          </div>
          <button type="button" className="icon-btn" aria-label="Sign out" title="Sign out" onClick={signOut}><SignOut size={17} /></button>
        </div>
      </aside>

      <div className="scrim" aria-hidden="true" onClick={close} />

      <div className="main">
        <header className="topbar">
          <button type="button" className="icon-btn menu-btn" aria-label="Open menu" aria-expanded={menuOpen} onClick={() => setMenuOpen(true)}><List size={20} /></button>
          <p className="topbar-title">{title}</p>
          <span className="session-pill" title="HttpOnly cookie over HTTPS, ends after 8 hours"><LockSimple size={14} />Secure session</span>
        </header>
        <main id="content" className="content" tabIndex={-1}><Outlet /></main>
      </div>
    </div>
  )
}