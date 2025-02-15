# DisBoard - Discord Soundboard Bot

This project is a Discord bot that functions as a soundboard with a button-based UI system. It allows users to play sounds through a simple interface.

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

3. **Run the Docker container:**
   ```
   docker run -d --name disboard-bot -v /path/to/config:/config -v /path/to/sounds:/sounds disboard
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
- Send the command "!soundboard" in a text channel the bot can see.
- Use the button interface to play sounds by clicking the corresponding buttons.

## Features

- Button-based UI for easy sound playback.
- Supports multiple sound files.
- Configurable inactivity timeout.
- Configurable voice channel timeout.
- Configurable command prefix.
- Configurable activity status.

## Contributing

Feel free to submit issues or pull requests for improvements and new features.