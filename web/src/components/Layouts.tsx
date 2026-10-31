import type { ReactNode } from 'react'
import { Brand } from './Brand'

export function FullPageLoader({ label = 'Loading…' }: { label?: string }) {
  return (
    <div className="grid min-h-dvh place-items-center bg-slate-50">
      <div
        role="status"
        aria-live="polite"
        className="flex items-center gap-3 text-sm text-slate-600"
      >
        <span className="size-4 animate-spin rounded-full border-2 border-slate-300 border-t-slate-900" />
        {label}
      </div>
    </div>
  )
}

export function AuthLayout({
  title,
  subtitle,
  children,
  footer,
}: {
  title: string
  subtitle?: string
  children: ReactNode
  footer?: ReactNode
}) {
  return (
    <div className="flex min-h-dvh flex-col bg-slate-50">
      <header className="border-b border-slate-200 bg-white">
        <div className="mx-auto flex w-full max-w-5xl items-center px-6 py-4">
          <Brand />
        </div>
      </header>

      <main className="flex flex-1 items-start justify-center px-6 py-10 sm:items-center">
        <div className="w-full max-w-md">
          <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
          {subtitle ? (
            <p className="mt-1 text-sm text-slate-600">{subtitle}</p>
          ) : null}

          <div className="mt-6 rounded-xl border border-slate-200 bg-white p-6 shadow-sm">
            {children}
          </div>

          {footer ? (
            <div className="mt-4 text-sm text-slate-600">{footer}</div>
          ) : null}
        </div>
      </main>

      <footer className="border-t border-slate-200 bg-white">
        <div className="mx-auto w-full max-w-5xl px-6 py-4 text-xs text-slate-500">
          Local development build — access tokens are held in memory only.
        </div>
      </footer>
    </div>
  )
}
