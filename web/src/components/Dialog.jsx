import { useEffect, useEffectEvent, useId, useRef } from 'react'
import '../styles/dialog.css'

// Native <dialog>: focus moves inside and stays there, and the page behind can't be clicked
export default function Dialog({ open, title, onClose, children }) {
  const ref = useRef(null)
  const titleId = useId()
  const closed = useEffectEvent(() => onClose())

  // If the browser closes it some other way, React still hears about it
  useEffect(() => {
    const dialog = ref.current
    const handle = () => closed()
    dialog.addEventListener('close', handle)
    return () => dialog.removeEventListener('close', handle)
  }, [])

  useEffect(() => {
    const dialog = ref.current
    if (open && !dialog.open) dialog.showModal()
    if (!open && dialog.open) dialog.close()
  }, [open])

  // Escape is handled here directly, so it works the same in every browser
  function handleKeyDown(e) {
    if (e.key !== 'Escape') return
    e.preventDefault()
    onClose()
  }

  return (
    <dialog ref={ref} className="dialog" aria-labelledby={titleId} onKeyDown={handleKeyDown}>
      {/* Only rendered while open, so every time it opens it starts fresh */}
      {open && (
        <>
          <h2 id={titleId} className="dialog-title">{title}</h2>
          {children}
        </>
      )}
    </dialog>
  )
}