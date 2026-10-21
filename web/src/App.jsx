import { useEffect, useState } from 'react'
import { Receipt, ShieldCheck } from '@phosphor-icons/react'
import './App.css'

const swatches = [
  ['Paper', '--color-paper'],
  ['Surface', '--color-surface'],
  ['Ink', '--color-ink'],
  ['Ledger green', '--color-green'],
  ['Mint', '--color-mint'],
  ['Overdue', '--color-danger'],
]

function ApiStatus({ api }) {
  if (api.state === 'healthy') {
    return (
      <span className="status status--healthy">
        <ShieldCheck size={15} weight="bold" />
        API healthy · v{api.version}
      </span>
    )
  }
  if (api.state === 'down') {
    return (
      <span className="status status--down">
        <span className="status-dot" />
        API unreachable. Is it running?
      </span>
    )
  }
  return (
    <span className="status status--checking">
      <span className="status-dot" />
      Checking API…
    </span>
  )
}

function App() {
  const [api, setApi] = useState({ state: 'checking' })

  useEffect(() => {
    const controller = new AbortController()

    fetch('/api/health', { signal: controller.signal })
      .then((res) => {
        if (!res.ok) throw new Error(`HTTP ${res.status}`)
        return res.json()
      })
      .then((data) => setApi({ state: 'healthy', version: data.version }))
      .catch((err) => {
        if (err.name !== 'AbortError') setApi({ state: 'down' })
      })

    return () => controller.abort()
  }, [])

  return (
    <main className="shell">
      <header className="brand">
        <span className="brand-mark"><Receipt size={18} /></span>
        <span className="brand-name">Invoice<em>flow</em></span>
      </header>

      <section className="hero">
        <p className="eyebrow">Day 03 · Design tokens</p>
        <h1 className="display">Get paid.<br />Stay <em>protected.</em></h1>
        <ApiStatus api={api} />
      </section>

      <section className="swatches">
        {swatches.map(([name, token]) => (
          <div className="swatch" key={token}>
            <span className="chip" style={{ background: `var(${token})` }} />
            <span>{name}</span>
            <code>{token}</code>
          </div>
        ))}
      </section>
    </main>
  )
}

export default App