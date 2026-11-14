import { useEffect, useState } from 'react'
import { Link } from 'react-router'
import { ArrowRight, Plus } from '@phosphor-icons/react'
import { useAuth } from '../auth/AuthContext.js'
import { getJson } from '../api.js'
import { dueInfo, formatMoney, formatMoneyShort, formatMonth, greeting, initials, moneyParts, timeZone, todayLong } from '../format.js'
import '../styles/invoices.css'
import '../styles/dashboard.css'

const RECENT = 5

// "This month" means the user's month, so the browser sends its time zone.
// If the server doesn't know that zone (400), fall back to UTC rather than show nothing.
async function getInMyZone(path, signal) {
  const res = await getJson(`${path}?timeZone=${encodeURIComponent(timeZone())}`, signal)
  return res.status === 400 ? getJson(path, signal) : res
}

async function loadDashboard(signal) {
  const [stats, flow, recent] = await Promise.all([
    getInMyZone('/api/dashboard/stats', signal),
    getInMyZone('/api/dashboard/cash-flow', signal),
    getJson(`/api/invoices?page=1&pageSize=${RECENT}`, signal),
  ])
  if (!stats.ok || !flow.ok || !recent.ok) throw new Error('Dashboard failed to load')
  return { stats: stats.data, flow: flow.data, recent: recent.data }
}

// Rounds the tallest bar up to a friendly top, split into 3 lines: $6K, $12K, $18K
function chartTop(max) {
  if (max <= 0) return 0
  const step = max / 3
  const pow = 10 ** Math.floor(Math.log10(step))
  return [1, 2, 2.5, 5, 10].map((n) => n * pow).find((n) => n >= step) * 3
}

const count = (n, word) => `${n} ${word}${n === 1 ? '' : 's'}`

function Amount({ value }) {
  const [whole, cents] = moneyParts(value)
  return <p className="kpi-value">{whole}<span className="kpi-unit">{cents}</span></p>
}

export default function Overview() {
  const { user } = useAuth()
  const first = user?.fullName?.trim().split(/\s+/)[0]
  const [result, setResult] = useState({ data: null, failed: false })

  useEffect(() => {
    const controller = new AbortController()
    loadDashboard(controller.signal)
      .then((data) => setResult({ data, failed: false }))
      .catch((err) => { if (err.name !== 'AbortError') setResult({ data: null, failed: true }) })
    return () => controller.abort()
  }, [])

  return (
    <>
      <div className="dash-head">
        <div>
          <p className="dash-date">{todayLong()}</p>
          <h1 className="page-title">{greeting()}{first ? `, ${first}` : ''}.</h1>
        </div>
        <Link className="page-action" to="/invoices/new"><Plus size={16} />New invoice</Link>
      </div>

      {result.failed && <p className="alert" role="alert">Couldn’t load your dashboard. Refresh to try again.</p>}
      {result.data && <Dashboard {...result.data} />}
    </>
  )
}

function Dashboard({ stats, flow, recent }) {
  const thisMonth = flow.at(-1)
  const top = chartTop(Math.max(...flow.map((m) => m.paid + m.outstanding)))
  const height = (v) => ({ height: `${(v / top) * 100}%` })
  const collected = flow.reduce((sum, m) => sum + m.paid, 0)
  const days = stats.averageDaysToPay

  return (
    <>
      <section className="kpis" aria-label="Summary">
        <div className="kpi">
          <p className="kpi-label">Outstanding<span className="kpi-count">{count(stats.outstandingCount, 'invoice')}</span></p>
          <Amount value={stats.outstanding} />
          <p className="kpi-note">
            {stats.overdue > 0 ? <><span className="dot" aria-hidden="true" />{formatMoney(stats.overdue)} overdue</> : 'Nothing overdue'}
          </p>
        </div>
        <div className="kpi">
          <p className="kpi-label">Paid in {formatMonth(thisMonth.month, 'long')}</p>
          <Amount value={stats.paidThisMonth} />
          <p className="kpi-note">So far this month</p>
        </div>
        <div className="kpi">
          <p className="kpi-label">Drafts</p>
          <Amount value={stats.drafts} />
          <p className="kpi-note">{stats.draftCount ? `${count(stats.draftCount, 'draft')} not sent yet` : 'No drafts'}</p>
        </div>
        <div className="kpi">
          <p className="kpi-label">Average time to pay</p>
          {days == null
            ? <p className="kpi-value">—</p>
            : <p className="kpi-value">{days}<span className="kpi-unit"> {days === 1 ? 'day' : 'days'}</span></p>}
          <p className="kpi-note">{days == null ? 'Nothing paid in the last 90 days' : 'Invoices paid in the last 90 days'}</p>
        </div>
      </section>

      <section className="chart-card" aria-labelledby="cash-flow-title">
        <div className="card-head">
          <div className="card-title-row">
            <h2 id="cash-flow-title" className="card-title">Cash flow</h2>
            <span className="card-meta">{formatMonth(flow[0].month, 'year')} – {formatMonth(thisMonth.month, 'year')}</span>
          </div>
          <div className="legend">
            <span className="legend-item"><span className="swatch swatch--paid" />Paid</span>
            <span className="legend-item"><span className="swatch swatch--open" />Outstanding</span>
            <span className="legend-total">Collected {formatMoneyShort(collected)}</span>
          </div>
        </div>

        {/* Drawn with plain divs: no chart library. Screen readers get the table below instead. */}
        <div aria-hidden="true">
          <div className="plot">
            {top > 0 && [1, 2, 3].map((n) => (
              <span key={n} className="gridline" style={{ bottom: `${(n * 100) / 3}%` }}>
                <span className="axis-label">{formatMoneyShort((top * n) / 3)}</span>
              </span>
            ))}
            {top === 0 && <p className="chart-empty">Nothing paid or outstanding in the last 12 months yet.</p>}
            <div className="bars">
              {flow.map((m) => {
                const current = m === thisMonth
                return (
                  <div key={m.month} className={current ? 'bar is-current' : 'bar'}
                    title={`${formatMonth(m.month, 'year')}: ${formatMoney(m.paid)} paid, ${formatMoney(m.outstanding)} outstanding`}>
                    {current && top > 0 && <span className="bar-total">{formatMoneyShort(m.paid + m.outstanding)}</span>}
                    {m.outstanding > 0 && <span className="bar-open" style={height(m.outstanding)} />}
                    {m.paid > 0 && <span className="bar-paid" style={height(m.paid)} />}
                  </div>
                )
              })}
            </div>
          </div>
          <div className="months">{flow.map((m) => <span key={m.month}>{formatMonth(m.month)}</span>)}</div>
        </div>

        {/* A table won't shrink to 1px, so the wrapper div does the hiding */}
        <div className="sr-only">
          <table>
            <caption>Cash flow by month</caption>
            <thead><tr><th scope="col">Month</th><th scope="col">Paid</th><th scope="col">Outstanding</th></tr></thead>
            <tbody>
              {flow.map((m) => (
                <tr key={m.month}><th scope="row">{formatMonth(m.month, 'year')}</th><td>{formatMoney(m.paid)}</td><td>{formatMoney(m.outstanding)}</td></tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      <section className="table-card recent" aria-labelledby="recent-title">
        <div className="card-head recent-head">
          <h2 id="recent-title" className="card-title">Recent invoices</h2>
          {recent.totalCount > 0 && <Link className="card-link" to="/invoices">View all {recent.totalCount}<ArrowRight size={13} /></Link>}
        </div>
        {recent.items.length === 0
          ? <p className="card-empty">No invoices yet. <Link to="/invoices/new">Create your first one</Link></p>
          : (
            <div className="table-scroll">
              <table className="table">
                <thead>
                  <tr>
                    <th scope="col">Invoice</th>
                    <th scope="col">Client</th>
                    <th scope="col">Due</th>
                    <th scope="col" className="num">Amount</th>
                    <th scope="col">Status</th>
                  </tr>
                </thead>
                <tbody>
                  {recent.items.map((inv) => {
                    const due = dueInfo(inv.dueDate, inv.status)
                    return (
                      <tr key={inv.id}>
                        <td className="mono inv-number"><Link className="inv-link" to={`/invoices/${inv.id}`}>{inv.number}</Link></td>
                        <td>
                          <span className="client-cell">
                            <span className="avatar-sm" aria-hidden="true">{initials(inv.clientName)}</span>
                            <span className="client-name">{inv.clientName}</span>
                          </span>
                        </td>
                        <td className={due.className}>{due.text}</td>
                        <td className="num mono amount">{formatMoney(inv.total)}</td>
                        <td><span className={`pill pill--${inv.status.toLowerCase()}`}>{inv.status}</span></td>
                      </tr>
                    )
                  })}
                </tbody>
              </table>
            </div>
          )}
      </section>
    </>
  )
}