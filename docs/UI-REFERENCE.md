# Video reference and interface changes

Reference: user-supplied `gemini_generated_video_927c2c9c.mp4`, 1280 × 720, 24 fps, approximately 10 seconds.

The clip shows a compact Windows glass messenger. At 0–2 seconds the layout has a slim icon rail, conversation list, centered global search, priority notice and a rounded composer. At about 3–4 seconds it shows white received bubbles, pale green sent bubbles, avatars, a typing indicator and an image card. At 5–6 seconds a floating frosted search palette opens. At 7–9 seconds an amber announcement appears above the conversation and a reaction menu opens beside a message. Text and some controls in the generated clip are inconsistent; the implementation keeps real names, labels and accessible actions.

Changes implemented:

- Desktop Acrylic backdrop, lighter translucent panel tints, and a restrained background wash; solid fallback remains available for performance mode.
- Slimmer navigation and conversation columns, meaningful avatar initials, centered global search, and rounded sidebar search.
- White received and pale green sent bubbles with compact timestamps, read status and a small keyboard-accessible action menu.
- Rounded composer with one attachment action, emoji and circular send button; earlier history appears only in the header.
- Verified inline image previews for supported images up to 10 MB; larger files retain download and explicit preview actions.
- Full-width priority notice above the conversation, including an amber gradient for important announcements.
- Theme-aware floating command palette, short reveal motion and reduced-motion support.
- No placeholder conversation header or composer before a chat is selected; the empty state offers a real start-conversation action.
- The supplied IFB Connect logo remains on the login page and title bar.

The Windows backdrop uses the user's desktop and transparency settings. It does not ship a screenshot of the reference wallpaper. Desktop interaction and visual comparison on the target machine are still required; compilation verifies the interface source, not its final rendering.
