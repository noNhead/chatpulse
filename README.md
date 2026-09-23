# ChatPulse

Realtime Twitch chat analytics for Windows. Connects to any public channel anonymously and
tracks, per chatter, how fast they type and how much of what they type is original.

## Download

Download [ChatPulse.exe](../../releases/latest/download/ChatPulse.exe) from the latest
[release](../../releases/latest). It is a single self-contained file: no installer, no .NET
runtime to install. Windows 10 or 11, x64.

Type a channel name in the title bar and press **Connect**.

The exe is not code-signed, so SmartScreen may warn on first launch. Every release ships a
SHA-256 checksum and a build provenance attestation:

```bash
gh attestation verify ChatPulse.exe --owner noNhead
```

## What it measures

For every chatter, over the retention window (24h by default):

- **Speed** — messages per second, minute or hour, averaged over a sliding window (1 min by default).
- **Counted** — messages inside the retention window.
- **Unique** — how many of those are distinct, and the resulting originality %.
- **Session** — everything written since the session started; never trimmed.

Messages that differ only in case, whitespace or invisible padding (the U+E0000 tag block,
zero-width characters, BOM — what spam scripts add to slip past Twitch's duplicate filter) count
as the same message.

The **Originality** card is chat-wide: distinct lines over all lines within the originality
window (1 min by default). It catches what per-chatter numbers cannot — forty chatters each
posting the same emote once.

Click a row for the chatter's details: originality, speed over the last 15 minutes, most
repeated lines, recent messages.

## Overlay

The overlay button in the title bar (or `--overlay`) opens a frameless window with the top
chatters, for a second monitor or an OBS window capture. Drag to move, resize from the
bottom-right corner; row count and opacity are in Settings. Hovering shows the pin, compact and
close buttons: unpinned, the overlay can sit behind other windows and OBS still captures it.

If OBS captures the overlay as black, set the source's capture method to
**Windows 10 (1903 and up)**.

## Privacy

- Reads chat through Twitch's IRC WebSocket gateway (`wss://irc-ws.chat.twitch.tv`) as an
  anonymous `justinfan` user: read-only, no account, no OAuth, no tokens. The app cannot post.
- Talks to nothing else and sends no telemetry.
- Chat stays in memory. The only file written is `%APPDATA%\ChatPulse\settings.json`.
- No third-party dependencies: the .NET base library and WPF only.

## Memory

Three settings bound memory on large channels:

| Setting | Default | Bounds |
| --- | --- | --- |
| Retention | 24h | How long messages stay counted |
| History cap per chatter | 5000 | Messages one chatter can hold — this is what actually bounds memory |
| Message log | 200 | Raw messages kept per chatter for the detail panel |

Settings shows how many messages the current configuration holds and roughly how much memory
that takes.

## Command line

| Flag | Effect |
| --- | --- |
| `--channel <name>` | Connect to the channel on startup |
| `--connect` | Connect to the last used channel on startup |
| `--overlay` | Open the overlay on startup |
| `--demo` | Synthetic chat feed, no network |
| `--selftest [channel]` | Connect to Twitch once, print the result, exit |
| `--screenshot <dir>` | Render every screen from the demo feed into `<dir>` as PNG, exit |

## Building

Requires the .NET 8 SDK.

```bat
build-exe.bat
```

Builds the app, draws its icon with the app itself, and publishes the self-contained single file
to `dist\ChatPulse.exe`.

CI builds and smoke-tests every push and pull request. Publishing a GitHub release with a
`vX.Y.Z` tag builds the exe and attaches it to the release.

```
src/ChatPulse/
  Chat/     IRC parser, anonymous WebSocket client, demo feed
  Stats/    StatsEngine: counting, windows, snapshots
  Core/     AppState: owns the connection, funnels every change through one channel
  Config/   settings persistence
  Ui/       WPF: theme, custom-drawn charts, dashboard, overlay, settings
  Tools/    screenshot harness, self-test
```

## License

[MIT](LICENSE)
