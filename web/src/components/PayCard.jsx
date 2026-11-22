import { useEffect, useState } from 'react'
import { loadStripe } from '@stripe/stripe-js'
import { Elements, PaymentElement, useElements, useStripe } from '@stripe/react-stripe-js'
import { CheckCircle, LockSimple, ShieldCheck } from '@phosphor-icons/react'
import { postJson } from '../api.js'
import { formatMoney } from '../format.js'
import Skeleton from './Skeleton.jsx'

// Stripe.js always loads from Stripe's own servers, and only on this page, never in the signed-in app
const stripes = new Map()
const stripeFor = (key) => {
  if (!stripes.has(key)) stripes.set(key, loadStripe(key))
  return stripes.get(key)
}

// One request per link, even when React runs effects twice in development
const intents = new Map()
const intentFor = (token) => {
  if (!intents.has(token)) intents.set(token, postJson(`/api/pay/${encodeURIComponent(token)}/intent`, {}))
  return intents.get(token)
}

const APPEARANCE = { theme: 'stripe', variables: { colorPrimary: '#1E5B43', colorText: '#151A17', borderRadius: '8px' } }

// The Pay card: Stripe's card form. Card details go from this form straight to Stripe.
// Paying here never marks the invoice Paid. Only Stripe's signed webhook can do that (Day 33).
export default function PayCard({ token, total }) {
  const [result, setResult] = useState(null)
  const [done, setDone] = useState(null)

  useEffect(() => {
    let ignore = false
    intentFor(token)
      .then((res) => { if (!ignore) setResult({ token, res }) })
      .catch(() => { if (!ignore) setResult({ token, res: null }) })
    return () => { ignore = true }
  }, [token])

  const current = result?.token === token ? result : null
  const intent = current?.res?.ok ? current.res.data : null

  if (done || current?.res?.status === 409) return <Received processing={done === 'processing'} />

  return (
    <>
      <h2 id="pay-card-title" className="pay-card-title">Pay invoice</h2>
      <p className="pay-card-text">Pay by card. It takes a minute.</p>
      {!current && <Skeleton h={180} className="pay-element" />}
      {current && !intent && (
        <p className="pay-error" role="alert">
          {current.res?.status === 422 ? 'This invoice is too small to pay by card.' : 'Card payment isn’t available right now. Refresh to try again.'}
        </p>
      )}
      {intent && (
        <Elements stripe={stripeFor(intent.publishableKey)} options={{ clientSecret: intent.clientSecret, appearance: APPEARANCE }}>
          <CheckoutForm total={total} onDone={setDone} />
        </Elements>
      )}
      <p className="pay-note"><ShieldCheck size={15} />Card details go straight to Stripe. Invoiceflow never sees or stores your card number.</p>
    </>
  )
}

function CheckoutForm({ total, onDone }) {
  const stripe = useStripe()
  const elements = useElements()
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  async function handleSubmit(e) {
    e.preventDefault()
    if (!stripe || !elements) return
    setBusy(true)
    setError('')
    // No redirect: the page (and the token in its URL) stays here. 3-D Secure opens in a pop-up.
    const { error: failed, paymentIntent } = await stripe.confirmPayment({ elements, redirect: 'if_required' })
    setBusy(false)
    if (failed) {
      // Card and form problems come with messages written for the cardholder. Anything else gets a plain one.
      setError(failed.type === 'card_error' || failed.type === 'validation_error' ? failed.message : 'Payment didn’t go through. Try again.')
      return
    }
    onDone(paymentIntent.status)
  }

  return (
    <form onSubmit={handleSubmit}>
      <div className="pay-element"><PaymentElement /></div>
      {error && <p className="pay-error" role="alert">{error}</p>}
      <button type="submit" className="pay-btn" disabled={!stripe || busy}>
        <LockSimple size={16} />{busy ? 'Processing…' : `Pay ${formatMoney(total)}`}
      </button>
    </form>
  )
}

function Received({ processing }) {
  return (
    <>
      <CheckCircle size={28} weight="fill" className="pay-paid-icon" />
      <h2 id="pay-card-title" className="pay-card-title">{processing ? 'Payment processing' : 'Payment received'}</h2>
      <p className="pay-card-text">Thank you. A receipt is on its way to your inbox, and the invoice will show as paid shortly.</p>
    </>
  )
}