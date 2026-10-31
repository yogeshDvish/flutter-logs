import type { ReactNode } from 'react'
import { Navigate } from 'react-router-dom'
import { useAuth } from '../auth/useAuth'
import { FullPageLoader } from '../components/Layouts'

export function ProtectedRoute({ children }: { children: ReactNode }) {
  const { status } = useAuth()

  if (status === 'booting') {
    return <FullPageLoader label="Restoring session…" />
  }

  if (status !== 'authenticated') {
    return <Navigate to="/login" replace />
  }

  return <>{children}</>
}

export function GuestOnlyRoute({ children }: { children: ReactNode }) {
  const { status } = useAuth()

  if (status === 'booting') {
    return <FullPageLoader label="Checking session…" />
  }

  if (status === 'authenticated') {
    return <Navigate to="/" replace />
  }

  return <>{children}</>
}
