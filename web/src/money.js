import { formatMoney } from './format.js'

// Exact money maths in the browser, in whole cents (BigInt), rounded the same way as the API.
// Only a preview while you type: the API works out every total again when you save.

// Up to 10 digits and 2 decimals. Also used as the pattern on the amount inputs.
export const AMOUNT_PATTERN = String.raw`\d{1,10}(\.\d{1,2})?`
const AMOUNT = new RegExp(`^${AMOUNT_PATTERN}$`)

// "12.5" → 1250n (hundredths), or null if it isn't a valid amount
function toHundredths(text) {
  const s = text.trim()
  if (!AMOUNT.test(s)) return null
  const [whole, frac = ''] = s.split('.')
  return BigInt(whole) * 100n + BigInt(frac.padEnd(2, '0'))
}

// Divide, rounding halves up (2.345 → 2.35), like MidpointRounding.AwayFromZero on the API
const divideRounded = (n, d) => (n + d / 2n) / d

export function previewTotals(lines, taxRate) {
  let subtotal = 0n
  const amounts = lines.map((li) => {
    const qty = toHundredths(li.quantity)
    const price = toHundredths(li.unitPrice)
    if (qty === null || price === null) return null
    const cents = divideRounded(qty * price, 100n) // (qty × 100) × (price in cents) ÷ 100 = cents
    subtotal += cents
    return cents
  })
  const rate = BigInt(Math.round(taxRate * 10_000)) // 0.05 → 500 ten-thousandths
  const tax = divideRounded(subtotal * rate, 10_000n)
  return { amounts, subtotal, tax, total: subtotal + tax }
}

export const formatCents = (cents) => formatMoney(Number(cents) / 100)