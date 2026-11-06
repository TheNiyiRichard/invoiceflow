import { useAuth } from '../auth/AuthContext.js'
import ComingSoon from './ComingSoon.jsx'

export default function Overview() {
  const { user } = useAuth()
  const first = user?.fullName?.trim().split(/\s+/)[0] || 'there'
  return (
    <ComingSoon title={`Welcome, ${first}`} when="Week 4">
      Your dashboard: money outstanding, paid this month and a 12-month cash-flow chart.
    </ComingSoon>
  )
}