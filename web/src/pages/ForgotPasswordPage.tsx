import { useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { Alert, Button } from '../components/Button'
import { FormField } from '../components/FormField'
import { AuthLayout } from '../components/Layouts'
import { apiRequest, getErrorMessage, getFieldErrors } from '../lib/api'

export function ForgotPasswordPage() {
  const [email, setEmail] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({})
  const [submitting, setSubmitting] = useState(false)
  const [submitted, setSubmitted] = useState(false)

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setError(null)
    setFieldErrors({})
    setSubmitting(true)

    try {
      await apiRequest<void>('/auth/forgot-password', {
        method: 'POST',
        body: { email: email.trim() },
      })
      setSubmitted(true)
    } catch (caught) {
      setError(getErrorMessage(caught))
      setFieldErrors(getFieldErrors(caught))
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <AuthLayout
      title="Forgot password"
      subtitle="We will send a single-use reset link if the address is registered."
      footer={
        <p>
          Remembered it?{' '}
          <Link
            to="/login"
            className="font-medium text-indigo-600 hover:text-indigo-700"
          >
            Back to sign in
          </Link>
        </p>
      }
    >
      {submitted ? (
        <div className="space-y-4">
          <Alert tone="success">
            If an account exists for that email, a reset link is on its way. It
            expires in one hour.
          </Alert>
          <p className="rounded-lg border border-slate-200 bg-slate-50 px-3 py-2 text-xs text-slate-500">
            Development note: email delivery is not configured, so the reset
            link is printed to the API console.
          </p>
        </div>
      ) : (
        <form onSubmit={handleSubmit} className="space-y-4" noValidate>
          {error ? <Alert>{error}</Alert> : null}

          <FormField
            label="Email"
            type="email"
            autoComplete="email"
            required
            value={email}
            onChange={(event) => setEmail(event.target.value)}
            error={fieldErrors.email?.[0]}
          />

          <Button type="submit" className="w-full" disabled={submitting}>
            {submitting ? 'Sending…' : 'Send reset link'}
          </Button>
        </form>
      )}
    </AuthLayout>
  )
}
