// Same-origin requests through the Vite proxy: the browser attaches the
// auth cookie itself, and JavaScript never reads or stores it.
export async function postJson(url, body) {
  const res = await fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  const data = res.headers.get('content-type')?.includes('json') ? await res.json() : null
  return { ok: res.ok, status: res.status, data }
}

// Turns ASP.NET validation errors into { email: '…', password: '…' }
export function fieldErrors(data) {
  const out = {}
  for (const [key, messages] of Object.entries(data?.errors ?? {})) {
    if (messages?.length) out[key.replace(/^\$\./, '').toLowerCase()] = messages[0]
  }
  return out
}