import { LockSimple, Receipt } from '@phosphor-icons/react'
import '../styles/auth.css'

const points = [
  'Invoices with signed payment links that expire on schedule.',
  'Two-factor sign-in and an alert for every new device.',
  'Every action written to an audit log you can export.',
]

export default function AuthLayout({ topLink, children }) {
  return (
    <div className="auth">
      <aside className="auth-brand">
        <div className="auth-logo">
          <span className="auth-logo-mark"><Receipt size={17} /></span>
          <span className="auth-logo-name">Invoice<em>flow</em></span>
        </div>
        <div className="auth-pitch">
          <p className="auth-display">Get paid.<br />Stay <em>protected.</em></p>
          <ol className="auth-points">
            {points.map((text, i) => (
              <li key={text}><span>{String(i + 1).padStart(2, '0')}</span>{text}</li>
            ))}
          </ol>
        </div>
        <div className="auth-meta"><span>© 2026 Invoiceflow</span><span>Built in public · Adeniyi Richard</span></div>
      </aside>
      <main className="auth-main">
        <div className="auth-top">{topLink}</div>
        <div className="auth-center"><div className="auth-form">{children}</div></div>
        <div className="auth-meta"><span><LockSimple size={13} />HTTPS only</span><span>Privacy · Terms</span></div>
      </main>
    </div>
  )
}