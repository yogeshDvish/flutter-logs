import { useQuery } from '@tanstack/react-query'
import { getHealth } from '../lib/api'

export function HealthPill() {
  const health = useQuery({
    queryKey: ['health'],
    queryFn: ({ signal }) => getHealth(signal),
    retry: false,
    refetchInterval: 30_000,
  })

  if (health.isPending) {
    return (
      <span
        role="status"
        aria-live="polite"
        className="inline-flex items-center gap-2 rounded-full border border-slate-200 bg-slate-50 px-3 py-1 text-xs font-medium text-slate-600"
      >
        <span className="size-2 animate-pulse rounded-full bg-slate-400" />
        Checking API
      </span>
    )
  }

  if (health.isError) {
    return (
      <span
        role="status"
        aria-live="polite"
        title="The API or database could not be reached."
        className="inline-flex items-center gap-2 rounded-full border border-amber-200 bg-amber-50 px-3 py-1 text-xs font-medium text-amber-700"
      >
        <span className="size-2 rounded-full bg-amber-500" />
        API unavailable
      </span>
    )
  }

  return (
    <span
      role="status"
      aria-live="polite"
      className="inline-flex items-center gap-2 rounded-full border border-emerald-200 bg-emerald-50 px-3 py-1 text-xs font-medium text-emerald-700"
    >
      <span className="size-2 rounded-full bg-emerald-500" />
      API connected
      <span className="font-normal text-emerald-600">
        · db {health.data?.db ?? 'unknown'}
      </span>
    </span>
  )
}
