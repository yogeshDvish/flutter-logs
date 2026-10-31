import { fireEvent, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import type {
  IssuedProjectKey,
  ProjectKey,
  ProjectSummary,
} from './lib/types'
import { installFetchMock, type MockRoute } from './test/fetchMock'
import { renderApp } from './test/renderApp'

const anonymousRefresh = {
  method: 'POST',
  path: '/auth/refresh',
  status: 401,
  body: {
    title: 'The refresh token cookie is missing.',
    code: 'missing_refresh_token',
  },
}

const authenticatedSession = {
  accessToken: 'test-access-token',
  tokenType: 'Bearer',
  expiresIn: 900,
  user: {
    id: '11111111-1111-1111-1111-111111111111',
    email: 'ana@example.com',
    displayName: 'Ana Lovelace',
    emailVerified: true,
    createdAt: '2026-01-01T00:00:00Z',
  },
}

const projectId = '33333333-3333-4333-8333-333333333333'
const activeKeyId = '44444444-4444-4444-8444-444444444444'

const project: ProjectSummary = {
  id: projectId,
  name: 'Mobile app',
  description: 'Production Flutter app',
  ingestEnabled: true,
  createdAt: '2026-10-01T10:00:00Z',
  updatedAt: '2026-10-01T10:00:00Z',
  archivedAt: null,
  activeKeyCount: 1,
  stats: {
    last24Hours: { total: 120, errors: 3 },
    last7Days: { total: 840, errors: 12 },
    lastLogAt: null,
  },
}

const activeKey: ProjectKey = {
  id: activeKeyId,
  prefix: 'Ab3dE9f1',
  label: 'Primary',
  status: 'active',
  createdAt: '2026-10-01T10:00:00Z',
  lastUsedAt: null,
  graceUntil: null,
  revokedAt: null,
}

const issuedKey: IssuedProjectKey = {
  id: '55555555-5555-4555-8555-555555555555',
  prefix: 'Zx9Yw8Vu',
  label: 'Secondary',
  createdAt: '2026-10-30T12:00:00Z',
  graceUntil: null,
  plaintext: 'flk_live_Zx9Yw8Vu_abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQ',
}

function withSession(routes: MockRoute[]): MockRoute[] {
  return [
    {
      method: 'POST',
      path: '/auth/refresh',
      status: 200,
      body: authenticatedSession,
    },
    {
      path: '/health',
      status: 200,
      body: { status: 'Healthy', db: 'ok', version: '0.3.0' },
    },
    ...routes,
  ]
}

describe('authentication shell', () => {
  it('redirects anonymous visitors from the dashboard to sign in', async () => {
    installFetchMock([anonymousRefresh])

    renderApp('/')

    expect(
      await screen.findByRole('heading', { name: 'Sign in' }),
    ).toBeInTheDocument()
  })

  it('shows the API message when sign in fails', async () => {
    installFetchMock([
      anonymousRefresh,
      {
        method: 'POST',
        path: '/auth/login',
        status: 401,
        body: {
          title: 'Email or password is incorrect.',
          code: 'invalid_credentials',
        },
      },
    ])

    renderApp('/login')

    await screen.findByRole('heading', { name: 'Sign in' })

    fireEvent.change(screen.getByLabelText('Email'), {
      target: { value: 'ana@example.com' },
    })
    fireEvent.change(screen.getByLabelText('Password'), {
      target: { value: 'CorrectHorseBattery9!' },
    })
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Email or password is incorrect.',
    )
  })

  it('confirms registration and points the user to sign in', async () => {
    installFetchMock([
      anonymousRefresh,
      {
        method: 'POST',
        path: '/auth/register',
        status: 201,
        body: {
          id: '22222222-2222-2222-2222-222222222222',
          email: 'new@example.com',
          displayName: null,
          emailVerified: false,
          createdAt: '2026-01-01T00:00:00Z',
        },
      },
    ])

    renderApp('/register')

    await screen.findByRole('heading', { name: 'Create your account' })

    fireEvent.change(screen.getByLabelText('Email'), {
      target: { value: 'new@example.com' },
    })
    fireEvent.change(screen.getByLabelText('Password'), {
      target: { value: 'CorrectHorseBattery9!' },
    })
    fireEvent.change(screen.getByLabelText('Confirm password'), {
      target: { value: 'CorrectHorseBattery9!' },
    })
    fireEvent.click(screen.getByRole('button', { name: 'Create account' }))

    expect(
      await screen.findByRole('heading', { name: 'Check your email' }),
    ).toBeInTheDocument()
  })

  it('verifies an email token from the URL', async () => {
    installFetchMock([
      anonymousRefresh,
      {
        method: 'POST',
        path: '/auth/verify-email',
        status: 204,
      },
    ])

    renderApp('/verify-email?token=test-token')

    expect(await screen.findByText(/your email is verified/i)).toBeInTheDocument()
  })
})

describe('projects and ingestion keys', () => {
  it('renders projects with activity and key counts', async () => {
    installFetchMock(
      withSession([{ path: '/projects', status: 200, body: [project] }]),
    )

    renderApp('/')

    expect(
      await screen.findByRole('heading', { name: 'Projects' }),
    ).toBeInTheDocument()
    expect(await screen.findByText('Mobile app')).toBeInTheDocument()
    expect(screen.getByText('120 logs')).toBeInTheDocument()
    expect(screen.getByText('3 errors')).toBeInTheDocument()
    expect(screen.getByText('1 active key')).toBeInTheDocument()
    expect(screen.getByText('Ana Lovelace')).toBeInTheDocument()
    expect(screen.getByText('ana@example.com')).toBeInTheDocument()
    expect(await screen.findByText(/api connected/i)).toBeInTheDocument()
  })

  it('shows an empty state before the first project exists', async () => {
    installFetchMock(withSession([{ path: '/projects', status: 200, body: [] }]))

    renderApp('/')

    expect(
      await screen.findByRole('heading', { name: 'No projects yet' }),
    ).toBeInTheDocument()
  })

  it('reveals archived projects when requested', async () => {
    const archivedProject: ProjectSummary = {
      ...project,
      archivedAt: '2026-10-30T00:00:00Z',
      ingestEnabled: false,
      activeKeyCount: 0,
    }

    installFetchMock(
      withSession([
        {
          path: '/projects?includeArchived=true',
          status: 200,
          body: [archivedProject],
        },
        { path: '/projects', status: 200, body: [] },
      ]),
    )

    renderApp('/')

    await screen.findByRole('heading', { name: 'No projects yet' })
    fireEvent.click(screen.getByLabelText('Show archived'))

    expect(await screen.findByText('Mobile app')).toBeInTheDocument()
    expect(screen.getByText('Archived')).toBeInTheDocument()
    expect(screen.getByText('0 active keys')).toBeInTheDocument()
  })

  it('shows a newly created key once and clears it when dismissed', async () => {
    installFetchMock(
      withSession([
        { path: '/projects', method: 'GET', status: 200, body: [] },
        {
          path: '/projects',
          method: 'POST',
          status: 201,
          body: { project, key: issuedKey },
        },
      ]),
    )

    renderApp('/')

    await screen.findByRole('heading', { name: 'No projects yet' })

    fireEvent.change(screen.getByLabelText('Project name'), {
      target: { value: 'Mobile app' },
    })
    fireEvent.change(screen.getByLabelText('Description (optional)'), {
      target: { value: 'Production Flutter app' },
    })
    fireEvent.click(screen.getByRole('button', { name: 'Create project' }))

    expect(
      await screen.findByRole('heading', { name: 'Save your ingestion key' }),
    ).toBeInTheDocument()
    expect(screen.getByText(issuedKey.plaintext)).toBeInTheDocument()
    expect(screen.getByText(/copy this key now/i)).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: "I've saved it" }))

    expect(screen.queryByText(issuedKey.plaintext)).not.toBeInTheDocument()
  })

  it('renders project details, stats, keys, and integration instructions', async () => {
    installFetchMock(
      withSession([
        { path: `/projects/${projectId}/keys`, status: 200, body: [activeKey] },
        { path: `/projects/${projectId}`, status: 200, body: project },
      ]),
    )

    renderApp(`/projects/${projectId}`)

    expect(
      await screen.findByRole('heading', { name: 'Mobile app' }),
    ).toBeInTheDocument()
    expect(screen.getByText('120 logs')).toBeInTheDocument()
    expect(screen.getByText('No logs yet')).toBeInTheDocument()
    expect(await screen.findByText('Ab3dE9f1')).toBeInTheDocument()
    expect(screen.getByText('Active')).toBeInTheDocument()
    expect(
      screen.getAllByText(/\/api\/v1\/ingest/).length,
    ).toBeGreaterThan(0)
  })

  it('rotates a key and shows the new plaintext once', async () => {
    installFetchMock(
      withSession([
        {
          path: `/projects/${projectId}/keys`,
          method: 'GET',
          status: 200,
          body: [activeKey],
        },
        {
          path: `/projects/${projectId}/keys`,
          method: 'POST',
          status: 201,
          body: issuedKey,
        },
        { path: `/projects/${projectId}`, status: 200, body: project },
      ]),
    )

    renderApp(`/projects/${projectId}`)

    await screen.findByRole('heading', { name: 'Mobile app' })
    fireEvent.click(screen.getByRole('button', { name: 'Rotate key' }))

    expect(
      screen.getByRole('heading', { name: 'Rotate ingestion key' }),
    ).toBeInTheDocument()

    fireEvent.change(screen.getByLabelText('Key label (optional)'), {
      target: { value: 'Secondary' },
    })
    fireEvent.click(screen.getByRole('button', { name: 'Issue new key' }))

    expect(
      await screen.findByRole('heading', { name: 'Save your ingestion key' }),
    ).toBeInTheDocument()
    expect(screen.getByText(issuedKey.plaintext)).toBeInTheDocument()
  })

  it('revokes a key after an explicit confirmation', async () => {
    let keys = [activeKey]

    installFetchMock(
      withSession([
        {
          path: `/projects/${projectId}/keys/${activeKeyId}`,
          method: 'DELETE',
          status: 204,
          onRequest: () => {
            keys = [
              {
                ...activeKey,
                status: 'revoked',
                revokedAt: '2026-10-30T13:00:00Z',
              },
            ]
          },
        },
        { path: `/projects/${projectId}/keys`, status: 200, body: () => keys },
        { path: `/projects/${projectId}`, status: 200, body: project },
      ]),
    )

    renderApp(`/projects/${projectId}`)

    expect(await screen.findByText('Ab3dE9f1')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Revoke' }))
    fireEvent.click(screen.getByRole('button', { name: 'Confirm revoke' }))

    expect(await screen.findByText('Revoked')).toBeInTheDocument()
  })

  it('archives a project and returns to the list', async () => {
    installFetchMock(
      withSession([
        { path: `/projects/${projectId}/keys`, status: 200, body: [activeKey] },
        { path: `/projects/${projectId}`, method: 'DELETE', status: 204 },
        { path: `/projects/${projectId}`, status: 200, body: project },
        { path: '/projects', status: 200, body: [] },
      ]),
    )

    renderApp(`/projects/${projectId}`)

    await screen.findByRole('heading', { name: 'Mobile app' })
    fireEvent.click(screen.getByRole('button', { name: 'Archive project' }))
    fireEvent.click(screen.getByRole('button', { name: 'Confirm archive' }))

    expect(
      await screen.findByRole('heading', { name: 'No projects yet' }),
    ).toBeInTheDocument()
  })

  it('permanently deletes a project and returns to the list', async () => {
    installFetchMock(
      withSession([
        { path: `/projects/${projectId}/keys`, status: 200, body: [activeKey] },
        {
          path: `/projects/${projectId}?purge=true`,
          method: 'DELETE',
          status: 204,
        },
        { path: `/projects/${projectId}`, status: 200, body: project },
        { path: '/projects', status: 200, body: [] },
      ]),
    )

    renderApp(`/projects/${projectId}`)

    await screen.findByRole('heading', { name: 'Mobile app' })
    fireEvent.click(screen.getByRole('button', { name: 'Delete permanently' }))
    fireEvent.click(
      screen.getByRole('button', { name: 'Confirm permanent delete' }),
    )

    expect(
      await screen.findByRole('heading', { name: 'No projects yet' }),
    ).toBeInTheDocument()
  })
})
