import { useState } from 'react'
import type { IssuedProjectKey } from '../lib/types'
import { Button } from './Button'

/**
 * Displays a plaintext ingestion key exactly once. The parent must clear the
 * key from state when this panel is dismissed; the API cannot return it again.
 */
export function IssuedKeyPanel({
  issuedKey,
  projectName,
  title = 'Save your ingestion key',
  onDismiss,
}: {
  issuedKey: IssuedProjectKey
  projectName?: string
  title?: string
  onDismiss: () => void
}) {
  const [copyState, setCopyState] = useState<'idle' | 'copied' | 'failed'>(
    'idle',
  )

  async function handleCopy() {
    try {
      if (!navigator.clipboard) {
        throw new Error('Clipboard is unavailable.')
      }

      await navigator.clipboard.writeText(issuedKey.plaintext)
      setCopyState('copied')
    } catch {
      setCopyState('failed')
    }
  }

  return (
    <section className="rounded-xl border border-emerald-300 bg-emerald-50 p-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="text-base font-semibold text-emerald-950">{title}</h2>
          <p className="mt-1 text-sm text-emerald-800">
            Copy this key now. It is shown once and cannot be recovered.
            {projectName ? ` Project: ${projectName}.` : ''}
          </p>
        </div>
        <span className="rounded-full border border-emerald-300 bg-white px-2.5 py-1 text-xs font-medium text-emerald-700">
          {issuedKey.prefix}
        </span>
      </div>

      <div className="mt-4 flex flex-wrap items-center gap-2">
        <code className="min-w-0 flex-1 break-all rounded-lg border border-emerald-200 bg-white px-3 py-2 font-mono text-xs text-slate-800">
          {issuedKey.plaintext}
        </code>
        <Button variant="secondary" onClick={handleCopy}>
          {copyState === 'copied' ? 'Copied' : 'Copy key'}
        </Button>
      </div>

      {copyState === 'failed' ? (
        <p role="alert" className="mt-2 text-xs font-medium text-rose-700">
          Copy failed. Select the key and copy it manually.
        </p>
      ) : null}

      <div className="mt-4">
        <Button onClick={onDismiss}>I&apos;ve saved it</Button>
      </div>
    </section>
  )
}
