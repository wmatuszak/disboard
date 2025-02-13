using System.Threading.Tasks;

namespace disboard
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var bot = new Bot();
            await bot.RunAsync();
        }
    }
}