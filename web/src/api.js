// Same-origin requests through the Vite proxy: the browser attaches the
// auth cookie itself, and JavaScript never reads or stores it.
async function request(url, options = {}) {
  const res = await fetch(url, options)
  // A 401 outside /api/auth/ means the session ended (expired or signed out elsewhere)
  if (res.status === 401 && !url.startsWith('/api/auth/')) window.dispatchEvent(new Event('auth:expired'))
  const data = res.headers.get('content-type')?.includes('json') ? await res.json() : null
  return { ok: res.ok, status: res.status, data }
}

export const getJson = (url, signal) => request(url, { signal })

const sendJson = (method) => (url, body) =>
  request(url, {
    method,
    headers: { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  })

export const postJson = sendJson('POST')
export const putJson = sendJson('PUT')
export const deleteJson = sendJson('DELETE')

// Turns ASP.NET validation errors into { email: '…', 'lineitems[0].quantity': '…' }
export function fieldErrors(data) {
  const out = {}
  for (const [key, messages] of Object.entries(data?.errors ?? {})) {
    if (messages?.length) out[key.replace(/^\$\./, '').toLowerCase()] = messages[0]
  }
  return out
}