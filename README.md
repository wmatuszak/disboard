# DisBoard - Discord Soundboard Bot

This project is a Discord bot that functions as a soundboard with a button-based UI system. It allows users to play sounds through a simple interface.

## Project Structure

```
disboard
├── src
│   ├── Bot.cs
│   ├── UI
│   │   └── ButtonHandler.cs
│   ├── Services
│   │   └── SoundService.cs
│   └── Models
│       └── Sound.cs
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
   docker run -d disboard
   ```

## Usage

- Invite the bot to your Discord server using the OAuth2 URL generated in the Discord Developer Portal.
- Use the button interface to play sounds by clicking the corresponding buttons.

## Features

- Button-based UI for easy sound playback.
- Supports multiple sound files.
- Easy to extend with additional sounds and functionalities.

## Contributing

Feel free to submit issues or pull requests for improvements and new features.