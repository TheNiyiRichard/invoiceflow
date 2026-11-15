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

// Downloads a file (like an invoice PDF) with the same cookie and sign-out handling as every other request
export async function downloadFile(url, fileName) {
  const res = await fetch(url)
  if (res.status === 401) window.dispatchEvent(new Event('auth:expired'))
  if (!res.ok) return { ok: false, status: res.status }
  const href = URL.createObjectURL(await res.blob())
  const a = document.createElement('a')
  a.href = href
  a.download = fileName
  a.click()
  setTimeout(() => URL.revokeObjectURL(href), 1000)
  return { ok: true, status: res.status }
}