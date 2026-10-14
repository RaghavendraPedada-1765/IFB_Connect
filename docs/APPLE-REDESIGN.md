# IFB Connect interface redesign

## Direction

The apple-design skill was adapted to native Windows WinUI rather than introducing a web view or replacing the application platform. The IFB blue accent and existing logo remain. Segoe UI Variable remains the platform font; no Apple font or proprietary Apple assets are bundled.

The interface uses warm neutral backgrounds, opaque reading surfaces, a heavier translucent navigation sidebar, rounded grouped cards, and restrained compositor motion. Materials communicate hierarchy rather than decorating every surface. Messages, captions and controls use distinct sizes and weights. Navigation has visible labels and semantic native buttons.

## Screens changed

- Sign-in: compact form, readable connection status, steady inline errors, official branding, a quieter welcome panel and a compact layout below 900 pixels. The form scrolls instead of clipping on shorter windows.
- Workspace: labelled Messages, Channels, People, Files and Settings; identity at the foot of the sidebar; separate global search; a useful empty state. The conversation column hides on other pages and returns when a conversation opens.
- Messages: generous header and bubble spacing, neutral incoming messages, pale IFB-blue outgoing messages, clearer reply context, image attachments and a rounded composer. Existing message actions and sending remain wired to the same handlers.
- People: compact identity rows with native profile pictures, department/job details only when supplied, presence status, search and group creation.
- Channels: department rows with clear channel markers and descriptions.
- Files: file icons, readable names and human-readable sizes. Verified download behavior remains intact.
- Settings: grouped appearance, notifications, availability, tray behavior and account cards. Existing saved preferences and sign-out behavior remain active.
- Administration: consistent page hierarchy, action bar, cards, native lists and forms for users, channels, announcements and audit actions.
- Search palette: wider adaptive overlay, reversible short opacity transitions, keyboard result selection, focus restoration, and solid-material fallback.
- Announcements: steady semantic priority colors instead of repeating opacity pulses or decorative gradients.

The code-generated pages are the working implementation. Their former unused XAML placeholders were removed to avoid two conflicting designs.

## Interaction and accessibility

Native button templates preserve immediate pressed feedback, keyboard operation, focus visuals and disabled states. Navigation and selected primary buttons add small critically damped compositor springs. Springs omit a forced initial value and retarget from the current presentation value. Search transitions retain the current opacity and use a revision guard so a dismissed-and-reopened palette cannot be collapsed by an old completion callback.

Windows animation preferences and solid-surface performance mode disable the extra springs/transitions. Windows reduced-transparency and high-contrast settings force solid material fallbacks. High-contrast resources use system foreground/background colors. Full-page sliding transitions, error shakes, background meshes and presence/announcement pulses were removed.

WinUI spring reference: https://learn.microsoft.com/en-us/windows/apps/develop/composition/spring-animations

## Validation

The self-contained win-x64 client publishes successfully. The isolated server suite passes 48 checks including bidirectional messages, replies, reaction add/remove, PNG attachments, announcements, authorization and token handling. Three new checks cover the actual conversation-list endpoint: latest preview, recipient unread count and exclusion of non-member conversations. The redesigned application was opened and inspected through the Windows computer-use tool, with sign-in performed by the user.

The visual walkthrough exposed a pre-existing conversation-list failure. Includes applied after a navigation projection failed, and a filtered include also required SQL APPLY unsupported by SQLite. The endpoint now queries conversations directly and fetches only the latest message per conversation using a scalar subquery. Database contents are preserved. Client list-loading failures now surface instead of silently leaving an empty list.

The review also checked compact directory rows, grouped settings, dark mode, conversation bubbles and an image-preview dialog. Dialogs now explicitly follow their owning page's theme. Inline image previews refresh when virtualized message rows change context. Sending no longer waits for the former 220 ms decorative animation before contacting the server.

Repeat end-to-end manual acceptance in the final build for sending/replying, attachments, notifications, dark mode, Windows text scaling, high contrast, reduced transparency/motion, and administrator-only pages. Automated server tests do not establish visual or screen-reader correctness.

## Open the preview

Keep the local development server running and execute `./scripts/Start-RedesignedClient.ps1` from the project folder. The final client is under artifacts/IFBConnect-apple-design-complete/Client. It uses the existing account/preferences and does not recreate the database.
