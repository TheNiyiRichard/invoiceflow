import { useCallback, useEffect, useMemo, useState } from 'react'
import { AuthContext } from './AuthContext.js'
import { postJson } from '../api.js'

// Asks the API who's signed in. The cookie itself stays invisible to JavaScript.
async function fetchMe(signal) {
  const res = await fetch('/api/auth/me', { signal })
  return res.ok ? res.json() : null
}

export default function AuthProvider({ children }) {
  const [user, setUser] = useState(null)
  const [loading, setLoading] = useState(true)

  // First check on page load; state only changes once the response arrives
  useEffect(() => {
    const controller = new AbortController()
    fetchMe(controller.signal)
      .then((me) => {
        setUser(me)
        setLoading(false)
      })
      .catch((err) => {
        if (err.name === 'AbortError') return
        setUser(null)
        setLoading(false)
      })

    const expired = () => setUser(null)
    window.addEventListener('auth:expired', expired)
    return () => {
      controller.abort()
      window.removeEventListener('auth:expired', expired)
    }
  }, [])

  // Called by the sign-in page after a successful login
  const refresh = useCallback(async () => {
    try {
      setUser(await fetchMe())
    } catch {
      setUser(null)
    }
  }, [])

  const signOut = useCallback(async () => {
    try {
      await postJson('/api/auth/logout')
    } finally {
      setUser(null)
    }
  }, [])

  const value = useMemo(() => ({ user, loading, refresh, signOut }), [user, loading, refresh, signOut])
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}