import { useEffect, useRef, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { Alert } from '../components/Button'
import { AuthLayout } from '../components/Layouts'
import { apiRequest, getErrorMessage } from '../lib/api'

type VerifyState = 'verifying' | 'verified' | 'failed'

export function VerifyEmailPage() {
  const [searchParams] = useSearchParams()
  const token = searchParams.get('token')

  const [state, setState] = useState<VerifyState>(
    token ? 'verifying' : 'failed',
  )
  const [message, setMessage] = useState(
    token ? '' : 'This verification link is missing its token.',
  )
  const startedRef = useRef(false)

  useEffect(() => {
    if (startedRef.current || !token) {
      return
    }

    startedRef.current = true

    apiRequest<void>('/auth/verify-email', {
      method: 'POST',
      body: { token },
    })
      .then(() => setState('verified'))
      .catch((error: unknown) => {
        setState('failed')
        setMessage(getErrorMessage(error))
      })
  }, [token])

  return (
    <AuthLayout
      title="Verify email"
      subtitle="Confirming the address on your account."
      footer={
        <p>
          <Link
            to="/login"
            className="font-medium text-indigo-600 hover:text-indigo-700"
          >
            Back to sign in
          </Link>
        </p>
      }
    >
      <div className="space-y-4">
        {state === 'verifying' ? (
          <div
            role="status"
            aria-live="polite"
            className="flex items-center gap-3 text-sm text-slate-600"
          >
            <span className="size-4 animate-spin rounded-full border-2 border-slate-300 border-t-slate-900" />
            Verifying your email…
          </div>
        ) : null}

        {state === 'verified' ? (
          <Alert tone="success">
            Your email is verified. You can now sign in and continue.
          </Alert>
        ) : null}

        {state === 'failed' ? (
          <>
            <Alert>{message || 'This verification link is invalid.'}</Alert>
            <p className="text-sm text-slate-600">
              Verification links expire after 24 hours and can only be used
              once. Sign in and request a new link from the dashboard.
            </p>
          </>
        ) : null}
      </div>
    </AuthLayout>
  )
}
