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
    }
}