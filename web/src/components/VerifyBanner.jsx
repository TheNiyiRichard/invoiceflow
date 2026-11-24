import { useState } from 'react'
import { EnvelopeSimple } from '@phosphor-icons/react'
import { postJson } from '../api.js'

const ERRORS = {
  429: 'You’ve asked for a lot of emails this hour. Try again later.',
  failed: 'Couldn’t send the email. Try again in a moment.',
}

// Shown on every page until the email is verified
export default function VerifyBanner({ email }) {
  const [state, setState] = useState('idle') // idle | sending | sent | 429 | failed

  async function resend() {
    setState('sending')
    try {
      const res = await postJson('/api/auth/resend-verification', {})
      setState(res.ok ? 'sent' : res.status === 429 ? '429' : 'failed')
    } catch {
      setState('failed')
    }
  }

  return (
    <div className="verify-banner">
      <EnvelopeSimple size={18} />
      <p><strong>Verify your email to send invoices.</strong> We sent a link to {email}. You can keep working on drafts until then.</p>
      {state === 'sent'
        ? <span role="status">Sent. Check your inbox and spam folder.</span>
        : (
          <>
            {ERRORS[state] && <span role="alert">{ERRORS[state]}</span>}
            <button type="button" onClick={resend} disabled={state === 'sending'}>{state === 'sending' ? 'Sending…' : 'Resend email'}</button>
          </>
        )}
    </div>
  )
}