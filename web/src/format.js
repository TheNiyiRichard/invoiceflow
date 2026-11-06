// Display helpers only: every amount shown here was calculated by the API.
const money = new Intl.NumberFormat('en-CA', { style: 'currency', currency: 'CAD' })
const day = new Intl.DateTimeFormat('en-US', { month: 'short', day: 'numeric', year: 'numeric', timeZone: 'UTC' })
const dayShort = new Intl.DateTimeFormat('en-US', { month: 'short', day: 'numeric', timeZone: 'UTC' })

export const formatMoney = (amount) => money.format(amount)

// The API sends dates as "2026-11-17" with no time. Reading them as UTC means
// a timezone can never shift an invoice to the day before.
function parseDate(s) {
  const [y, m, d] = s.split('-').map(Number)
  return Date.UTC(y, m - 1, d)
}

export const formatDate = (s) => day.format(parseDate(s))

// "Nov 17 · in 8 days", "due today", "4 days late". Drafts and paid invoices just show the date.
export function dueInfo(dueDate, status) {
  const label = dayShort.format(parseDate(dueDate))
  if (status === 'Draft' || status === 'Paid') return { text: label, className: 'muted' }

  const now = new Date()
  const days = Math.round((parseDate(dueDate) - Date.UTC(now.getFullYear(), now.getMonth(), now.getDate())) / 86_400_000)
  if (days > 1) return { text: `${label} · in ${days} days`, className: '' }
  if (days === 1) return { text: `${label} · tomorrow`, className: 'due-soon' }
  if (days === 0) return { text: `${label} · due today`, className: 'due-soon' }
  return { text: `${label} · ${-days} ${days === -1 ? 'day' : 'days'} late`, className: 'due-late' }
}

export function initials(name) {
  const words = name.trim().split(/\s+/).filter(Boolean)
  return ((words[0]?.[0] ?? '') + (words.length > 1 ? words.at(-1)[0] : '')).toUpperCase()
}