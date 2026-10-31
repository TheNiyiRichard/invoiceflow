import { Navigate, Outlet, useLocation } from 'react-router'
import { useAuth } from './AuthContext.js'

// Only paths inside this app. Never "//other-site.com" or a full URL.
const safePath = (p) => (typeof p === 'string' && p.startsWith('/') && !p.startsWith('//') ? p : '/')

export function ProtectedRoute() {
  const { user, loading } = useAuth()
  const location = useLocation()
  if (loading) return null
  if (!user) return <Navigate to="/sign-in" replace state={{ from: location.pathname + location.search }} />
  return <Outlet />
}

export function GuestRoute() {
  const { user, loading } = useAuth()
  const { state } = useLocation()
  if (loading) return null
  if (user) return <Navigate to={safePath(state?.from)} replace />
  return <Outlet />
}