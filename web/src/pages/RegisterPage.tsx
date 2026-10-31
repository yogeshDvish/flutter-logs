import { useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { useAuth } from '../auth/useAuth'
import { Alert, Button } from '../components/Button'
import { FormField } from '../components/FormField'
import { AuthLayout } from '../components/Layouts'
import { getErrorMessage, getFieldErrors } from '../lib/api'

export function RegisterPage() {
  const { register } = useAuth()

  const [displayName, setDisplayName] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({})
  const [submitting, setSubmitting] = useState(false)
  const [registered, setRegistered] = useState(false)

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setError(null)
    setFieldErrors({})

    if (password !== confirmPassword) {
      setError('The passwords do not match.')
      return
    }

    setSubmitting(true)

    try {
      await register({
        email: email.trim(),
        password,
        displayName: displayName.trim() || undefined,
      })
      setRegistered(true)
    } catch (caught) {
      setError(getErrorMessage(caught))
      setFieldErrors(getFieldErrors(caught))
    } finally {
      setSubmitting(false)
    }
  }

  if (registered) {
    return (
      <AuthLayout
        title="Check your email"
        subtitle="One step left before your account is fully secured."
        footer={
          <p>
            Already verified?{' '}
            <Link
              to="/login"
              className="font-medium text-indigo-600 hover:text-indigo-700"
            >
              Sign in
            </Link>
          </p>
        }
      >
        <div className="space-y-4">
          <Alert tone="success">
            Your account was created. We sent a verification link to{' '}
            <span className="font-medium">{email.trim()}</span>.
          </Alert>
          <p className="text-sm text-slate-600">
            You can sign in now — verification is optional but recommended so
            that password resets always reach you.
          </p>
          <p className="rounded-lg border border-slate-200 bg-slate-50 px-3 py-2 text-xs text-slate-500">
            Development note: email delivery is not configured, so the
            verification link is printed to the API console.
          </p>
          <Link
            to="/login"
            className="inline-flex w-full items-center justify-center rounded-lg bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-800"
          >
            Go to sign in
          </Link>
        </div>
      </AuthLayout>
    )
  }

  return (
    <AuthLayout
      title="Create your account"
      subtitle="Register to start sending logs from your Flutter apps."
      footer={
        <p>
          Already have an account?{' '}
          <Link
            to="/login"
            className="font-medium text-indigo-600 hover:text-indigo-700"
          >
            Sign in
          </Link>
        </p>
      }
    >
      <form onSubmit={handleSubmit} className="space-y-4" noValidate>
        {error ? <Alert>{error}</Alert> : null}

        <FormField
          label="Display name"
          autoComplete="name"
          hint="Optional. Shown in the dashboard."
          value={displayName}
          onChange={(event) => setDisplayName(event.target.value)}
          error={fieldErrors.displayname?.[0]}
        />

        <FormField
          label="Email"
          type="email"
          autoComplete="email"
          required
          value={email}
          onChange={(event) => setEmail(event.target.value)}
          error={fieldErrors.email?.[0]}
        />

        <FormField
          label="Password"
          type="password"
          autoComplete="new-password"
          required
          hint="At least 12 characters. Avoid common passwords."
          value={password}
          onChange={(event) => setPassword(event.target.value)}
          error={fieldErrors.password?.[0]}
        />

        <FormField
          label="Confirm password"
          type="password"
          autoComplete="new-password"
          required
          value={confirmPassword}
          onChange={(event) => setConfirmPassword(event.target.value)}
        />

        <Button type="submit" className="w-full" disabled={submitting}>
          {submitting ? 'Creating account…' : 'Create account'}
        </Button>
      </form>
    </AuthLayout>
  )
}
