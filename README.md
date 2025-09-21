# DisBoard - Discord Soundboard Bot

This project is a Discord bot that functions as a soundboard with a button-based UI system. It allows users to play sounds through a simple interface.

Note: The bot now uses Discord.Net for gateway and interactions. Audio playback is handled by Lavalink (Victoria v7 client), which avoids direct voice issues and improves stability.

## Project Structure

```
disboard
├── src
│   ├── Bot.cs
│   ├── BotConfig.cs
│   ├── UI
│   │   └── CommandHandler.cs
│   ├── Services
│   │   └── SoundService.cs
│   └── Models
│       └── Sound.cs
├── Program.cs
├── Dockerfile
├── disboard.csproj
└── README.md
```

## Setup Instructions

1. **Clone the repository:**
   ```
   git clone <repository-url>
   cd disboard
   ```

2. **Build the Docker image:**
   ```
   docker build -t disboard .
   ```

3. **Run with Docker Compose (recommended):**
   See `docker-compose.yaml` which starts:
   - `lavalink`: Lavalink v4 server on port 2333
   - `sounds`: nginx static server serving your `/sounds` folder
   - `disboard`: the bot container

   Update `config/config.json` with your token and Lavalink settings, then:
   ```
   docker compose up -d
   ```

## Configuration
The bot uses a JSON configuration file located at config.json. Here is an example file:
```
{
    "Token": "YOUR_BOT_TOKEN_HERE",
    "InactivityTimeoutSeconds": 60,
    "VoiceChannelTimeoutMinutes": 10,
    "CommandPrefix": "!",
    "Activity": "Playing sounds"
}
```


## Usage

- Invite the bot to your Discord server using the OAuth2 URL generated in the Discord Developer Portal.
- Join a voice channel.
- Send the command `!soundboard` in a text channel the bot can see.
- Use the button interface to play sounds by clicking the corresponding buttons.

Optional: You can add new sounds with `!add` via two methods:

1) Upload: attach an `.mp3` or `.wav` and the bot saves and indexes it.
2) YouTube: the bot DMs you to collect a link and times, clips the audio, sends a preview for approval, then saves once you name it.

Details below.

### Add Sounds

Start with `!add`.

- In a DM, choose:
  - Upload a file: send an `.mp3` or `.wav`. Then provide a name like `category_sound` (no extension).
  - Add from YouTube: send a YouTube URL, then:
    - Enter start time and end time (formats: seconds like `12.5` or `HH:MM:SS(.ms)` like `0:32.250`).
    - The bot downloads and clips the audio, then sends you the clip to preview.
    - Approve to proceed to naming, Re-enter times to try again, or Cancel to abort.

Notes:
- Names must be `category_sound` and must not include an extension.
- If Discord refuses the preview attachment (too large), the bot still shows Approve/Redo/Cancel buttons.

### File Management

- `!delete` (`!rm`, `!remove`): Starts a DM with a button that opens a text input to enter the sound name (no extension). If it resolves to an existing sound, you’ll be asked to confirm deletion.
- `!rename` (`!mv`): Starts a DM. First choose the existing sound via a text input, then provide the new name via a second text input. The file is renamed on disk and the bot’s index is updated.

Notes:
- Enter names without extensions; category-prefixed names like `memes_airhorn` are supported. If you omit a category and the suffix uniquely matches one sound, it resolves automatically.
- The bot preserves the original file extension; only the base name changes.

## Lavalink Notes
- The bot streams local files by URL via the `sounds` service. Ensure `SoundBaseUrl` in `config/config.json` matches the compose service (default `http://sounds`).
- Lavalink credentials and host are configured in `config/config.json` and should match `docker-compose.yaml`.

## Features

- Button-based UI for easy sound playback.
- Supports multiple sound files.
- Add from YouTube with preview confirmation and naming flow in DMs.
- Configurable inactivity timeout.
- Configurable voice channel timeout.
- Configurable command prefix.
- Configurable activity status.

## Contributing

Feel free to submit issues or pull requests for improvements and new features.
