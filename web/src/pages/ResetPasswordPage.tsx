import { useState, type FormEvent } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { Alert, Button } from '../components/Button'
import { FormField } from '../components/FormField'
import { AuthLayout } from '../components/Layouts'
import { apiRequest, getErrorMessage, getFieldErrors } from '../lib/api'

export function ResetPasswordPage() {
  const [searchParams] = useSearchParams()
  const token = searchParams.get('token')

  const [password, setPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({})
  const [submitting, setSubmitting] = useState(false)
  const [completed, setCompleted] = useState(false)

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setError(null)
    setFieldErrors({})

    if (!token) {
      setError('This reset link is missing its token.')
      return
    }

    if (password !== confirmPassword) {
      setError('The passwords do not match.')
      return
    }

    setSubmitting(true)

    try {
      await apiRequest<void>('/auth/reset-password', {
        method: 'POST',
        body: { token, newPassword: password },
      })
      setCompleted(true)
    } catch (caught) {
      setError(getErrorMessage(caught))
      setFieldErrors(getFieldErrors(caught))
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <AuthLayout
      title="Set a new password"
      subtitle="Choose a strong password you have not used here before."
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
      {completed ? (
        <div className="space-y-4">
          <Alert tone="success">
            Your password has been updated. Every existing session was signed
            out.
          </Alert>
          <Link
            to="/login"
            className="inline-flex w-full items-center justify-center rounded-lg bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-800"
          >
            Sign in with the new password
          </Link>
        </div>
      ) : (
        <form onSubmit={handleSubmit} className="space-y-4" noValidate>
          {error ? <Alert>{error}</Alert> : null}

          {token ? null : (
            <Alert>This reset link is missing its token.</Alert>
          )}

          <FormField
            label="New password"
            type="password"
            autoComplete="new-password"
            required
            hint="At least 12 characters. Avoid common passwords."
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            error={fieldErrors.newpassword?.[0]}
          />

          <FormField
            label="Confirm new password"
            type="password"
            autoComplete="new-password"
            required
            value={confirmPassword}
            onChange={(event) => setConfirmPassword(event.target.value)}
          />

          <Button
            type="submit"
            className="w-full"
            disabled={submitting || !token}
          >
            {submitting ? 'Updating…' : 'Update password'}
          </Button>
        </form>
      )}
    </AuthLayout>
  )
}
