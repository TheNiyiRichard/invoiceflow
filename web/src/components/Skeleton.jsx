// A grey placeholder shaped like the content on its way, so nothing jumps when it arrives.
// It's empty, so screen readers skip it. Each page announces one "Loading…" instead.
export default function Skeleton({ w = '100%', h = 14, className = '' }) {
  return <span className={className ? `skeleton ${className}` : 'skeleton'} style={{ width: w, height: h }} />
}

// Rows for an invoice table that's still loading: number, client, date, amount, status
export function SkeletonRows({ rows = 5 }) {
  return (
    <div className="skeleton-rows" aria-hidden="true">
      {Array.from({ length: rows }, (_, i) => (
        <div key={i} className="skeleton-row">
          <Skeleton w={64} h={12} />
          <span className="skeleton-client">
            <Skeleton w={26} h={26} className="skeleton--round" />
            <Skeleton w={`${45 + ((i * 17) % 35)}%`} />
          </span>
          <Skeleton w={84} h={12} />
          <Skeleton w={72} className="skeleton--end" />
          <Skeleton w={64} h={24} className="skeleton--pill" />
        </div>
      ))}
    </div>
  )
}