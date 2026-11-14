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

// Dates as "2026-11-07": the same shape the API and <input type="date"> use
export function today() {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

export const addDays = (s, n) => new Date(parseDate(s) + n * 86_400_000).toISOString().slice(0, 10)

// Whole days from a to b, or null while either date is empty
export function daysBetween(a, b) {
  if (!a || !b) return null
  return Math.round((parseDate(b) - parseDate(a)) / 86_400_000)
}

const moneyShort = new Intl.NumberFormat('en-CA', { style: 'currency', currency: 'CAD', notation: 'compact', maximumFractionDigits: 1 })
export const formatMoneyShort = (amount) => moneyShort.format(amount)

// "$10,630.00" → ["$10,630", ".00"], so the cents can be drawn smaller
export function moneyParts(amount) {
  const parts = money.formatToParts(amount)
  const i = parts.findIndex((p) => p.type === 'decimal')
  const join = (list) => list.map((p) => p.value).join('')
  return i < 0 ? [join(parts), ''] : [join(parts.slice(0, i)), join(parts.slice(i))]
}

// The API sends months as "2026-11"
const months = {
  short: new Intl.DateTimeFormat('en-US', { month: 'short', timeZone: 'UTC' }),
  long: new Intl.DateTimeFormat('en-US', { month: 'long', timeZone: 'UTC' }),
  year: new Intl.DateTimeFormat('en-US', { month: 'short', year: 'numeric', timeZone: 'UTC' }),
}
export const formatMonth = (s, style = 'short') => months[style].format(parseDate(`${s}-01`))

// The user's own clock decides "morning" and "today"
export function greeting() {
  const h = new Date().getHours()
  return h < 12 ? 'Good morning' : h < 18 ? 'Good afternoon' : 'Good evening'
}
export const todayLong = () => new Intl.DateTimeFormat('en-US', { weekday: 'long', month: 'long', day: 'numeric' }).format(new Date())

// The browser's time zone, e.g. "America/Toronto"
export const timeZone = () => Intl.DateTimeFormat().resolvedOptions().timeZone