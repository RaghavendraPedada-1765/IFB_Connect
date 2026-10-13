# Local acceptance — 7 October 2026

## Verified

45 automated checks passed, including bidirectional messaging, replies and persisted reply relationships, reaction add/remove, typing, attachment permissions and PNG bytes/media type, announcements delivered to both employees, acknowledgement, read receipt authorization, token revocation and rate limiting. Results: `docs/test-results.json`.

On the running development server, two real employee accounts (`qa.alice`, `qa.bob`) authenticated using ordinary HTTPS certificate validation. Two concurrent SignalR client connections exchanged messages, a reply, a reaction and an image attachment. The image uploaded through the configured antivirus path and downloaded with unchanged bytes and image/png media type. Results: `.work/acceptance/live-results.json` (seven checks).

These live connections test the backend; they are not two visually inspected Windows app sessions.

## Desktop acceptance status

The user subsequently reported testing these features with no issues. This is manual, user-reported acceptance; independent desktop automation remained unavailable. The checklist below is retained for the receiving developer to repeat on their own computer.

Desktop automation was rejected with “Computer Use was not approved to use client”. Image rendering, visual layout and Windows toast appearance require the following manual checks. The build passes; these visual checks have not been marked as passed.

1. Keep the development server running. Run `./scripts/Start-Acceptance.ps1` from the project folder. This opens independent qa-alice and qa-bob app profiles without sharing saved credentials or settings. Use the passwords in `.work/acceptance/TEST-ACCOUNTS.md`; that private file is excluded from distribution.
2. In each window open the Alice QA / Bob QA conversation. Confirm the existing messages, reply association, reaction and qa-preview.png render. Send a new message both ways; reply to it and add/remove a reaction. Click the image preview to enlarge it.
3. Minimize Bob's window and send from Alice. Confirm a Windows notification appears and unread status clears only after Bob views the conversation. Repeat while Bob views Directory. Check that Do Not Disturb suppresses notifications.
4. From the existing administrator session post an Important test announcement. Confirm both employee windows show it, and each can acknowledge it independently. Do not grant the test employees administrator access.
5. Check light/dark themes, narrow and maximized windows, search, reply composer, readable announcements, and reduced-motion/performance settings.

## Fixes included

- Named profiles isolate saved credentials, preferences and diagnostic logs for simultaneous sessions.
- Background windows and non-chat pages no longer suppress incoming-message notifications.
- Conversation previews update for the currently selected conversation; your own outgoing messages do not increase unread counts.
- Background chats no longer auto-mark incoming messages as read. Returning to a visible conversation sends read receipts and clears its unread count.

Do not deploy company-wide until the desktop checklist passes on a representative company computer. Disable the temporary QA accounts after acceptance. Company TLS certificates, server binding, firewall rules, authentication and backups must also follow `DEPLOYMENT.md`.
