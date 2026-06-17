## Why

Improving the connection resilience, lifecycle management, and user onboarding (room join automation) of the `nexus-chat` Matrix AppService ensures that Thoor can reliably chat with multiple agents from a standard Matrix client without manual CLI commands or service crashes.

## What Changes

- Add connection loss detection and exponential backoff retry loops in the AppService registration.
- Add an invitation handler that automatically joins rooms when an agent account is invited by `@thoor:localhost`.
- Register standard POSIX signal handlers (`SIGINT`, `SIGTERM`) in Node.js to gracefully release resources, terminate subprocesses, and flush PTY outputs.

## Capabilities

### New Capabilities
- `matrix-auto-join`: Automatically accepts invitations to join rooms from authenticated users.
- `matrix-connection-recovery`: Gracefully recovers sessions and message delivery when Synapse goes offline.

### Modified Capabilities
- `matrix-appservice-routing`: Ensure PTY routing handles transient socket disconnection safely.

## Impact

- `nexus-chat/src/server.js` (reconnect logic and signal listeners)
- `nexus-chat/src/agent-router.js` (error boundaries for writing/reading PTY)
- `nexus-chat/src/transport-adapters/pty-attach.js` (process cleanup)

