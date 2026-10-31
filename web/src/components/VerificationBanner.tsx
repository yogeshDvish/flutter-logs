import { useState } from 'react'
import { useAuth } from '../auth/useAuth'
import { getErrorMessage } from '../lib/api'
import { Button } from './Button'

export function VerificationBanner() {
  const { resendVerification } = useAuth()
  const [state, setState] = useState<'idle' | 'sending' | 'sent' | 'error'>(
    'idle',
  )
  const [message, setMessage] = useState('')

  async function handleResend() {
    setState('sending')
    setMessage('')

    try {
      await resendVerification()
      setState('sent')
    } catch (caught) {
      setState('error')
      setMessage(getErrorMessage(caught))
    }
  }

  return (
    <div className="mb-6 rounded-xl border border-amber-200 bg-amber-50 p-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <p className="text-sm font-medium text-amber-900">
            Verify your email address
          </p>
          <p className="mt-0.5 text-xs text-amber-700">
            A verified address makes password recovery reliable. In development
            the verification link is printed to the API console.
          </p>
        </div>

        <Button
          variant="secondary"
          onClick={handleResend}
          disabled={state === 'sending' || state === 'sent'}
        >
          {state === 'sending'
            ? 'Sending…'
            : state === 'sent'
              ? 'Link sent'
              : 'Resend link'}
        </Button>
      </div>

      {state === 'sent' ? (
        <p role="status" className="mt-3 text-xs font-medium text-emerald-700">
          A fresh verification link was generated.
        </p>
      ) : null}

      {state === 'error' ? (
        <p role="alert" className="mt-3 text-xs font-medium text-rose-700">
          {message}
        </p>
      ) : null}
    </div>
  )
}
