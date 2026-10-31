import type { ReactNode } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/useAuth'
import { Brand } from './Brand'
import { Button } from './Button'
import { HealthPill } from './HealthPill'
import { VerificationBanner } from './VerificationBanner'

/**
 * Chrome shared by every signed-in screen. The access token is held in memory,
 * so signing out also clears the in-memory session before navigating away.
 */
export function AppShell({ children }: { children: ReactNode }) {
  const { user, logout } = useAuth()
  const navigate = useNavigate()

  async function handleLogout() {
    await logout()
    navigate('/login', { replace: true })
  }

  return (
    <div className="flex min-h-dvh flex-col">
      <header className="border-b border-slate-200 bg-white">
        <div className="mx-auto flex w-full max-w-5xl flex-wrap items-center justify-between gap-3 px-6 py-4">
          <Brand />

          <div className="flex flex-wrap items-center gap-3">
            <HealthPill />

            <div className="text-right">
              <p className="text-sm font-medium text-slate-900">
                {user?.displayName ?? 'Signed in'}
              </p>
              <p className="text-xs text-slate-500">{user?.email}</p>
            </div>

            <Button variant="secondary" onClick={handleLogout}>
              Sign out
            </Button>
          </div>
        </div>
      </header>

      <main className="mx-auto w-full max-w-5xl flex-1 px-6 py-10">
        {user && !user.emailVerified ? <VerificationBanner /> : null}
        {children}
      </main>

      <footer className="border-t border-slate-200 bg-white">
        <div className="mx-auto w-full max-w-5xl px-6 py-4 text-xs text-slate-500">
          Access tokens are kept in memory; the refresh token is an httpOnly
          cookie. Ingestion keys are shown once and stored as hashes.
        </div>
      </footer>
    </div>
  )
}
