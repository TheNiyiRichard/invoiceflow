import { useEffect, useState } from 'react'
import { useParams } from 'react-router'
import { CheckCircle, LockSimple, SealCheck, WarningCircle } from '@phosphor-icons/react'
import PayCard from '../components/PayCard.jsx'
import { getJson, postJson } from '../api.js'
import { formatDate, formatMoney, initials, moneyParts } from '../format.js'
import Skeleton from '../components/Skeleton.jsx'
import '../styles/app.css'
import '../styles/invoices.css'
import '../styles/pay.css'

const PROBLEMS = {
  404: ['This payment link isn’t valid', 'Make sure you opened the whole link from the email. If it still doesn’t work, reply to the email and ask for a new one.'],
  410: ['This payment link has expired', 'Payment links work for 30 days. Reply to the invoice email and ask for a new one.'],
  429: ['Too many attempts', 'Wait a minute, then refresh the page.'],
  failed: ['Something went wrong', 'Couldn’t load this invoice. Refresh the page to try again.'],
}

// The signed-out page a client reaches from the invoice email. No app shell and no sign-in:
// the signed link is the only permission, and the server checks it on every request.
export default function PayPage() {
  const { token } = useParams()
  const [result, setResult] = useState(null)

  // The token is in the address bar: never send this page's URL to another site, and keep it out of search engines
  useEffect(() => {
    const tags = [['referrer', 'no-referrer'], ['robots', 'noindex']].map(([name, content]) =>
      Object.assign(document.createElement('meta'), { name, content }))
    document.head.append(...tags)
    document.title = 'Pay invoice · Invoiceflow'
    return () => tags.forEach((t) => t.remove())
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    const url = `/api/pay/${encodeURIComponent(token)}`
    getJson(url, controller.signal)
      .then((res) => {
        setResult({ token, res })
        // "Viewed" is a separate POST, sent only once the invoice is on screen.
        // Email scanners that pre-open links never count as the client.
        if (res.ok && res.data.status === 'Sent') postJson(`${url}/viewed`).catch(() => {})
      })
      .catch((err) => { if (err.name !== 'AbortError') setResult({ token, res: null }) })
    return () => controller.abort()
  }, [token])

  // Only the answer for the link that's in the address bar now
  const current = result?.token === token ? result : null
  const invoice = current?.res?.ok ? current.res.data : null
  const problem = current && !invoice ? PROBLEMS[current.res?.status] ?? PROBLEMS.failed : null

  return (
    <div className="pay">
      <header className="pay-top">
        {invoice ? (
          <span className="pay-from">
            <span className="pay-mark" aria-hidden="true">{initials(invoice.from)}</span>
            <span className="pay-from-name">{invoice.from}</span>
          </span>
        ) : <span />}
        <span className="pay-secure"><LockSimple size={14} />Secured by <span className="pay-logo">Invoice<em>flow</em></span></span>
      </header>

      <main className="pay-main">
        {!current && <PaySkeleton />}
        {problem && (
          <div className="pay-problem" role="alert">
            <WarningCircle size={28} />
            <h1 className="pay-problem-title">{problem[0]}</h1>
            <p>{problem[1]}</p>
          </div>
        )}
        {invoice && <PayInvoice invoice={invoice} token={token} />}
      </main>

      <footer className="pay-foot">Questions? Reply to the invoice email.</footer>
    </div>
  )
}

function PayInvoice({ invoice, token }) {
  const [whole, cents] = moneyParts(invoice.total)
  const paid = invoice.status === 'Paid'
  const ratePercent = Number((invoice.taxRate * 100).toFixed(2))

  return (
    <div className="pay-grid">
      <section className="pay-details" aria-labelledby="pay-title">
        <h1 id="pay-title" className="pay-eyebrow">Invoice {invoice.number} · from {invoice.from}</h1>
        <p className="pay-amount">{whole}<span className="pay-cents">{cents}</span></p>
        <p className="pay-due">
          {paid ? 'Paid' : `Due ${formatDate(invoice.dueDate)}`}
          <span className="pay-sep" aria-hidden="true" />CAD
          {invoice.status === 'Overdue' && <span className="pill pill--overdue">Overdue</span>}
        </p>

        <ul className="pay-lines" aria-label="Items">
          {invoice.lines.map((li) => (
            <li key={li.position}>
              <span className="pay-line-text">
                <span className="pay-line-desc">{li.description}</span>
                {li.quantity !== 1 && <span className="pay-line-sub">{li.quantity} × {formatMoney(li.unitPrice)}</span>}
              </span>
              <span className="pay-line-amount">{formatMoney(li.amount)}</span>
            </li>
          ))}
        </ul>
        <dl className="pay-totals">
          <div><dt>Subtotal</dt><dd>{formatMoney(invoice.subtotal)}</dd></div>
          <div><dt>GST ({ratePercent}%)</dt><dd>{formatMoney(invoice.tax)}</dd></div>
        </dl>

        <div className="pay-verified">
          <SealCheck size={22} />
          <div>
            <p className="pay-verified-title">Verified link</p>
            <p className="pay-verified-text">Invoiceflow checked this link’s signature before showing anything. It opens this one invoice only, and it expires after 30 days.</p>
          </div>
        </div>
      </section>

      <aside className="pay-card" aria-labelledby="pay-card-title">
        {paid ? (
          <>
            <CheckCircle size={28} weight="fill" className="pay-paid-icon" />
            <h2 id="pay-card-title" className="pay-card-title">Paid in full</h2>
            <p className="pay-card-text">Thank you. There’s nothing left to pay on this invoice.</p>
          </>
        ) : (
          <PayCard token={token} total={invoice.total} />
        )}
      </aside>
    </div>
  )
}

function PaySkeleton() {
  return (
    <>
      <p className="sr-only" role="status">Loading invoice…</p>
      <div className="pay-grid" aria-hidden="true">
        <div className="pay-details">
          <Skeleton w={260} h={12} />
          <Skeleton w={320} h={96} className="pay-skeleton-amount" />
          <Skeleton w={200} h={14} />
        </div>
        <Skeleton h={300} className="pay-skeleton-card" />
      </div>
    </>
  )
}