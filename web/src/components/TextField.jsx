export default function TextField({ id, label, optional, hint, error, onChange, ...input }) {
  const describedBy = error ? `${id}-error` : hint ? `${id}-hint` : undefined

  return (
    <div className="field">
      <label htmlFor={id}>{label}{optional && <span className="optional"> (optional)</span>}</label>
      <input id={id} className="input" onChange={(e) => onChange(e.target.value)}
        aria-invalid={Boolean(error)} aria-describedby={describedBy} {...input} />
      {error && <p id={`${id}-error`} className="field-error">{error}</p>}
      {!error && hint && <p id={`${id}-hint`} className="field-hint">{hint}</p>}
    </div>
  )
}