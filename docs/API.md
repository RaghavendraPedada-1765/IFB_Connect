# API reference

The HTTPS base path is `/api/v1`. Requests use JSON except multipart uploads; protected endpoints require `Authorization: Bearer <access token>`. Conversation operations enforce membership and administrative operations enforce the Admin role. IDs are server-issued strings. Timestamps use UTC ISO 8601. Validation/access errors use HTTP 400/401/403/404; excessive requests use 429.

| Resource | Operations |
|---|---|
| Authentication | `POST auth/login` with username/password; `POST auth/refresh` with refreshToken; `POST auth/logout` with refreshToken; `GET auth/me`; optional `POST auth/windows` using Negotiate. |
| Directory | `GET users?q=&dept=&limit=&offset=` returns users/total; `GET users/{id}`; `GET users/me`; `GET users/departments`; `PATCH users/me/profile`; `PATCH users/me/presence`; Admin `POST users` and `PATCH users/{id}/disable`. |
| Conversations | `GET conversations`; `GET conversations/{id}`; `POST conversations/dm` with otherUserId; `POST conversations/group` with name/memberIds/description; `DELETE conversations/{id}/leave`. |
| Membership | `GET conversations/{id}/members`; conversation/global admin `POST` or `DELETE conversations/{id}/members/{userId}`. |
| Channels | `GET conversations/channels`; `POST conversations/{id}/join`. |
| History | `GET conversations/{id}/messages?before=&beforeId=&limit=&q=` returns messages/hasMore. Use the oldest message's SentAt and Id together for the next page. |
| Message actions | `POST conversations/{id}/messages/read` with messageId; `PATCH conversations/{id}/messages/{messageId}` with newContent; `DELETE` the same path; `POST .../{messageId}/react` with emoji. Edits, deletes, reactions and receipts broadcast over the hub. |
| Files | `POST files/upload` multipart file/conversationId; `GET files/{id}` downloads with SHA-256 response header; `GET files/{id}/info`; `GET files`; `GET conversations/{id}/files?limit=&offset=`. |
| Announcements | `GET announcements`; Admin `POST announcements` with title/content/priority/expiresAt; `POST announcements/{id}/acknowledge`; Admin `DELETE announcements/{id}` and `GET announcements/{id}/acks`. |
| Administration | `GET admin/users`, `connections`, `conversations`, `audit`, `audit/export`; `PATCH admin/users/{id}`; `POST admin/users/{id}/logout`; `POST admin/channels`; `PATCH admin/conversations/{id}`. |

The SignalR endpoint is `/hubs/chat`. Method and event contracts are in `src/IFB.Shared/Contracts/HubContracts.cs`. Methods include SendMessageAsync, EditMessageAsync, DeleteMessageAsync, ToggleReactionAsync, MarkReadAsync, SendTypingAsync, UpdatePresenceAsync, JoinConversationAsync and LeaveConversationAsync. The server checks membership and active account/token version on invocations and enforces per-user message limits.

Access tokens expire and refresh tokens rotate once. Store refresh tokens in Windows Credential Manager, never configuration files. Administrators can invalidate all outstanding sessions for an account. Message content is plain text; render it without interpreting HTML.

`GET /health` supports local service readiness checks. Runtime operation does not require Internet access or API exploration tooling.
