# Readable MCP client metadata interface

## MCP client configuration

- `mcp --client <key>` remains the stable machine authorization key.
- Optional `mcp --display-name <name>` sets the human-readable client name. It is a process configuration argument and is not exposed as a tool argument.
- Without that option, MCP initialization reads `clientInfo.title`, then `clientInfo.name`; if neither is a valid name, the product name is used.
- The display name is metadata only. It does not identify or authorize a client.

## Broker protocol fields

- Requests may carry `clientName` (maximum 120 characters, non-empty, and containing no control characters). A missing field is accepted for compatibility with older clients; an explicitly empty or invalid value is rejected.
- Newly submitted grants retain `AccessInfo.clientName`; jobs retain `JobInfo.clientName` and `JobInfo.taskName`.
- A job's `taskName` uses the submitted task name when present, otherwise the associated grant's task name. Normal jobs without either keep an empty name.
- `client` remains the authorization and ownership key; `clientName` is display metadata only. MCP tool descriptions treat returned task IDs as machine references, not user-facing titles.
- Protocol version: `0.3.0`.
