# ADR-0005 — SSE for live delivery with in-process fan-out

Status: Accepted · 2026-10-29

## Context

Live logs must reach an open dashboard in p95 ≤ 250 ms (single instance, LAN). Delivery is
one-directional and independent of ingestion, which is plain HTTPS.

## Decision

- Server-Sent Events over `text/event-stream`, consumed with `fetch()` streaming.
- In-process fan-out: a subscriber registry keyed by project id, each subscriber holding a bounded
  `Channel<LogEvent>`.
- Publish only after commit. Backpressure overflow emits `event: overflow`; the client backfills
  via REST from its cursor.
- `?since=<cursor>` replays up to 500 committed records before continuing live.

## Alternatives

- **WebSocket** — bidirectional capability we do not need; more connection state and more code.
- **Polling** — cannot meet the latency target economically.
- **SignalR** — convenient, but adds an abstraction and protocol negotiation for a one-way feed.
- **Postgres LISTEN/NOTIFY** — unavailable (ADR-0002) and unnecessary for one instance.

## Consequences

- Simple, proxy-friendly, auto-reconnect semantics we control.
- Single-instance only. Multi-instance requires Redis pub/sub behind the same `ILogStream`
  interface; endpoints and client do not change.
- Slow consumers get an explicit gap signal rather than silent loss or unbounded memory.
- SSE is not the source of truth: durability is the database commit, and the UI must be able to
  recover from disconnects.
