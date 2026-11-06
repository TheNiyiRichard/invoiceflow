import { Hammer } from '@phosphor-icons/react'

// Placeholder for sections not built yet, saying when they arrive
export default function ComingSoon({ title, when, children }) {
  return (
    <>
      <h1 className="page-title">{title}</h1>
      <div className="empty">
        <Hammer size={20} />
        <p><strong>Arrives {when}.</strong> {children}</p>
      </div>
    </>
  )
}