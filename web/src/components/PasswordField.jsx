import { useState } from 'react'
import { Eye, EyeSlash } from '@phosphor-icons/react'

export default function PasswordField({ id, label, value, onChange, autoComplete, error, hint }) {
  const [visible, setVisible] = useState(false)
  const describedBy = error ? `${id}-error` : hint ? `${id}-hint` : undefined

  return (
    <div className="field">
      <label htmlFor={id}>{label}</label>
      <div className="input-wrap">
        <input
          id={id}
          className="input"
          type={visible ? 'text' : 'password'}
          value={value}
          onChange={(e) => onChange(e.target.value)}
          autoComplete={autoComplete}
          maxLength={128}
          required
          aria-invalid={Boolean(error)}
          aria-describedby={describedBy}
        />
        <button
          type="button"
          className="reveal"
          onClick={() => setVisible((v) => !v)}
          aria-label={visible ? 'Hide password' : 'Show password'}
          aria-pressed={visible}
        >
          {visible ? <EyeSlash size={18} /> : <Eye size={18} />}
        </button>
      </div>
      {error && <p id={`${id}-error`} className="field-error">{error}</p>}
      {!error && hint && <p id={`${id}-hint`} className="field-hint">{hint}</p>}
    </div>
  )
}