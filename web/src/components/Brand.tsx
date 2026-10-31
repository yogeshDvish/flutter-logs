export function Brand({ compact = false }: { compact?: boolean }) {
  return (
    <div className="flex items-center gap-3">
      <span
        aria-hidden="true"
        className="grid size-9 place-items-center rounded-lg bg-slate-900 text-xs font-semibold text-white"
      >
        FL
      </span>
      <div>
        <p
          className={`font-semibold tracking-tight ${
            compact ? 'text-sm' : 'text-base'
          }`}
        >
          Flutter Logs
        </p>
        {compact ? null : (
          <p className="text-xs text-slate-500">
            Application logging &amp; monitoring
          </p>
        )}
      </div>
    </div>
  )
}
