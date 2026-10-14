# IFB Connect design system

## Tokens and hierarchy

`Themes/theme.json` provides the primary accent, theme backgrounds/text, glass opacity, radii and spacing scales. `ColorTokens.xaml` supplies themed resource dictionaries and semantic brushes; `TypographyStyles.xaml` supplies font, padding and radius resources; `GlassStyles.xaml` supplies reusable styles. Runtime initialization applies the configured primary accent and background/text/glass/radius values. A personal accent is saved in local preferences.

The primary accent defaults to IFB blue `#0055A5`. Success, warning and urgent states use green, amber and red. Presence adds purple for Do Not Disturb and grey for offline. Dark surfaces use light primary/secondary text; light surfaces use dark text. Muted text colors have been strengthened from the prototype. Contrast still requires visual assessment on the target displays and actual translucent backgrounds.

The spacing scale is 4, 8, 12, 16, 24, 32 and 48 pixels. Standard corners are 8, 12, 16 and 20 pixels. Typography uses locally installed Segoe UI Variable with Segoe UI fallback; icons use Segoe Fluent Icons with MDL2 fallback. No online fonts are loaded.

## Components

| Component | Behavior |
|---|---|
| Navigation rail | Keyboard-focusable icon controls with names, selection, hover and press states |
| Glass cards/buttons | Shared border, surface, radius and typography styles |
| ChatListItem | Name, preview, timestamp, avatar and unread count; accessible summary |
| PresenceAvatar | Initials, local gradient and presence indicator |
| Message bubbles | Sender/content/timestamp, alignment, quoted reply, reactions, attachment actions and read state |
| AnnouncementBanner | Priority color and acknowledgement; persists until server acknowledgement |
| CommandPalette | Ctrl+K; live employee/conversation search and navigation |
| Administration | Permission-gated forms, lists, membership controls and export |

## Layout and motion

The shell has a labelled navigation sidebar (Messages, Channels, People, Files, Settings) with the signed-in identity at its foot, a conversation column that hides on non-chat pages, and a flexible main pane (see APPLE-REDESIGN.md). Native Mica/Acrylic creates the window backdrop and panel depth; solid fallback colors keep text readable without transparency. Settings apply light, dark or Windows-default appearance. RDP and disabled Windows transparency trigger performance mode.

Navigation uses native transitions. Control hover/press, send feedback, typing dots and urgent banners provide motion. Motion helpers check Windows animation settings and performance mode. Long histories use ItemsRepeater with StackLayout; directory/file/admin lists use virtualized ListView controls. Older history loads in pages rather than loading an entire conversation.

Keyboard access includes the rail, normal controls/dialogs, Enter to send, Shift+Enter for newlines, Ctrl+K for search and message action menus. Narrator, high contrast, 125–200% scaling, contrast over glass, animation preferences and shop-floor/RDP performance need interactive acceptance checks before deployment.
