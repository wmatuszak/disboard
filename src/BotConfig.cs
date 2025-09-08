using System;

namespace disboard
{
    public class BotConfig
    {
        public string Token { get; set; }
        public int InactivityTimeoutSeconds { get; set; }
        public int VoiceChannelTimeoutMinutes { get; set; }
        public string CommandPrefix { get; set; }
        public string Activity { get; set; }
        public string LavalinkHost { get; set; } = "lavalink"; // docker service name or host
        public int LavalinkPort { get; set; } = 2333;
        public string LavalinkPassword { get; set; } = "youshallnotpass";
        public string SoundBaseUrl { get; set; } = "http://sounds"; // base URL where /sounds are served
    }
}
