import { useEffect, useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { CaretLeft, CaretRight } from '@phosphor-icons/react'
import { getJson } from '../api.js'
import { dueInfo, formatDate, formatMoney, initials } from '../format.js'
import '../styles/invoices.css'

const PAGE_SIZE = 20
const TABS = ['All', 'Draft', 'Sent', 'Viewed', 'Overdue', 'Paid']

// The URL is user input: only a known status and a sensible page number get through
function readQuery(params) {
  const raw = params.get('status')
  const status = TABS.includes(raw) && raw !== 'All' ? raw : null
  const page = Math.min(Math.max(Number.parseInt(params.get('page'), 10) || 1, 1), 100_000)
  return { status, page }
}

function href(status, page) {
  const q = new URLSearchParams()
  if (status) q.set('status', status)
  if (page > 1) q.set('page', page)
  const s = q.toString()
  return s ? `/invoices?${s}` : '/invoices'
}

// 1 … 4 5 6 … 12, so the pager stays short however many pages there are
function pageList(page, pages) {
  if (pages <= 7) return Array.from({ length: pages }, (_, i) => i + 1)
  const out = [1]
  const from = Math.max(2, page - 1)
  const to = Math.min(pages - 1, page + 1)
  if (from > 2) out.push('…')
  for (let n = from; n <= to; n++) out.push(n)
  if (to < pages - 1) out.push('…')
  out.push(pages)
  return out
}

export default function Invoices() {
  const [params] = useSearchParams()
  const { status, page } = readQuery(params)

  const query = new URLSearchParams({ page, pageSize: PAGE_SIZE })
  if (status) query.set('status', status)
  const url = `/api/invoices?${query}`

  // Each result remembers which URL it came from, so a slow old reply is never shown as the current one
  const [result, setResult] = useState({ url: null, data: null, failed: false })

  useEffect(() => {
    const controller = new AbortController()
    getJson(url, controller.signal)
      .then((res) => setResult({ url, data: res.ok ? res.data : null, failed: !res.ok }))
      .catch((err) => { if (err.name !== 'AbortError') setResult({ url, data: null, failed: true }) })
    return () => controller.abort()
  }, [url])

  const loading = result.url !== url
  const data = result.data
  const total = data?.totalCount ?? 0
  const pages = Math.max(1, Math.ceil(total / PAGE_SIZE))
  const first = (page - 1) * PAGE_SIZE + 1
  const last = first + (data?.items.length ?? 0) - 1

  return (
    <>
      <div className="page-head">
        <h1 className="page-title">Invoices</h1>
        {data && <span className="page-count">{total} {status ? status.toLowerCase() : 'total'}</span>}
      </div>

      <nav className="tabs" aria-label="Filter by status">
        {TABS.map((t) => {
          const value = t === 'All' ? null : t
          return (
            <Link key={t} className="tab" to={href(value, 1)} aria-current={value === status ? 'page' : undefined}>{t}</Link>
          )
        })}
      </nav>

      {result.failed && !loading && <p className="alert" role="alert">Couldn’t load your invoices. Refresh to try again.</p>}

      {data?.items.length === 0 && (
        <div className="empty">
          {total > 0
            ? <p>Nothing on this page. <Link to={href(status, 1)}>Go to the first page</Link></p>
            : <p>{status ? `No ${status.toLowerCase()} invoices.` : 'No invoices yet. You’ll create them here from Day 20.'}</p>}
        </div>
      )}

      {data?.items.length > 0 && (
        <div className={loading ? 'table-card is-loading' : 'table-card'} aria-busy={loading}>
          <div className="table-scroll">
            <table className="table">
              <thead>
                <tr>
                  <th scope="col">Invoice</th>
                  <th scope="col">Client</th>
                  <th scope="col">Issued</th>
                  <th scope="col">Due</th>
                  <th scope="col" className="num">Amount</th>
                  <th scope="col">Status</th>
                </tr>
              </thead>
              <tbody>
                {data.items.map((inv) => {
                  const due = dueInfo(inv.dueDate, inv.status)
                  return (
                    <tr key={inv.id}>
                      <td className="mono inv-number">{inv.number}</td>
                      <td>
                        <span className="client-cell">
                          <span className="avatar-sm" aria-hidden="true">{initials(inv.clientName)}</span>
                          <span className="client-name">{inv.clientName}</span>
                        </span>
                      </td>
                      <td className="muted">{formatDate(inv.issueDate)}</td>
                      <td className={due.className}>{due.text}</td>
                      <td className="num mono amount">{formatMoney(inv.total)}</td>
                      <td><span className={`pill pill--${inv.status.toLowerCase()}`}>{inv.status}</span></td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>

          <footer className="pager">
            <span>Showing {first}–{last} of {total}</span>
            {pages > 1 && (
              <nav className="pager-pages" aria-label="Pages">
                {page > 1
                  ? <Link className="page-btn" to={href(status, page - 1)} aria-label="Previous page"><CaretLeft size={14} /></Link>
                  : <span className="page-btn is-off" aria-hidden="true"><CaretLeft size={14} /></span>}
                {pageList(page, pages).map((n, i) => (n === '…'
                  ? <span key={`gap-${i}`} className="page-gap">…</span>
                  : <Link key={n} className="page-btn" to={href(status, n)} aria-current={n === page ? 'page' : undefined}>{n}</Link>))}
                {page < pages
                  ? <Link className="page-btn" to={href(status, page + 1)} aria-label="Next page"><CaretRight size={14} /></Link>
                  : <span className="page-btn is-off" aria-hidden="true"><CaretRight size={14} /></span>}
              </nav>
            )}
          </footer>
        </div>
      )}
    </>
  )
}