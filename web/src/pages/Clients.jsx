import { useEffect, useState } from 'react'
import { getJson } from '../api.js'

export default function Clients() {
  const [clients, setClients] = useState(null)
  const [failed, setFailed] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    getJson('/api/clients', controller.signal)
      .then((res) => (res.ok ? setClients(res.data) : setFailed(true)))
      .catch((err) => { if (err.name !== 'AbortError') setFailed(true) })
    return () => controller.abort()
  }, [])

  return (
    <>
      <h1 className="page-title">Clients</h1>
      <p className="page-lede">Only the clients you added. No other account can see them.</p>

      {failed && <p className="alert" role="alert">Couldn’t load your clients. Refresh to try again.</p>}
      {clients?.length === 0 && <div className="empty"><p>No clients yet.</p></div>}
      {clients?.length > 0 && (
        <ul className="list">
          {clients.map((c) => (
            <li key={c.id}>
              <span className="list-main">{c.name}{c.company && <span className="list-sub">{c.company}</span>}</span>
              <span className="list-meta">{c.email}</span>
            </li>
          ))}
        </ul>
      )}
    </>
  )
}