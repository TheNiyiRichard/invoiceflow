import { formatDate, initials } from '../format.js'
import { formatCents } from '../money.js'
import '../styles/preview.css'

// Display only. Everything here is rendered as React text, so nothing typed can become HTML.
export default function InvoicePreview({ sender, client, number, issueDate, dueDate, lines, totals, ratePercent, notes }) {
  return (
    <article className="sheet">
      <header className="sheet-head">
        <div className="sheet-from">
          <span className="sheet-mark" aria-hidden="true">{initials(sender.name)}</span>
          <div className="sheet-from-text">
            <p className="sheet-strong">{sender.name}</p>
            <p className="sheet-muted">{sender.email}</p>
          </div>
        </div>
        <div className="sheet-title">
          <p className="sheet-word">Invoice</p>
          <p className="sheet-number">{number}</p>
        </div>
      </header>

      <dl className="sheet-meta">
        <div>
          <dt>Billed to</dt>
          <dd>
            {client ? (
              <>
                <p>{client.name}</p>
                {client.company && <p className="sheet-muted">{client.company}</p>}
                <p className="sheet-muted">{client.email}</p>
                {client.address && <p className="sheet-muted sheet-pre">{client.address}</p>}
              </>
            ) : <p className="sheet-muted">Choose a client</p>}
          </dd>
        </div>
        <div><dt>Issued</dt><dd>{issueDate ? formatDate(issueDate) : '—'}</dd></div>
        <div><dt>Due</dt><dd>{dueDate ? formatDate(dueDate) : '—'}</dd></div>
      </dl>

      <table className="sheet-lines">
        <thead>
          <tr>
            <th scope="col">Description</th>
            <th scope="col" className="num col-qty">Qty</th>
            <th scope="col" className="num col-money">Rate</th>
            <th scope="col" className="num col-money">Amount</th>
          </tr>
        </thead>
        <tbody>
          {lines.map((li) => (
            <tr key={li.key}>
              <td>{li.description.trim() || <span className="sheet-muted">Untitled item</span>}</td>
              <td className="num">{li.quantity ?? '—'}</td>
              <td className="num">{li.rate === null ? '—' : formatCents(li.rate)}</td>
              <td className="num">{li.amount === null ? '—' : formatCents(li.amount)}</td>
            </tr>
          ))}
        </tbody>
      </table>

      <dl className="sheet-totals">
        <div><dt>Subtotal</dt><dd>{formatCents(totals.subtotal)}</dd></div>
        <div><dt>GST ({ratePercent}%)</dt><dd>{formatCents(totals.tax)}</dd></div>
        <div className="sheet-due"><dt>Total due</dt><dd>{formatCents(totals.total)}</dd></div>
      </dl>

      {notes.trim() && <p className="sheet-notes">{notes}</p>}
    </article>
  )
}