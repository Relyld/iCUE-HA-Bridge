using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace IcueHaBridge
{
    public static class Program
    {
        /// <summary>Name used for the Windows service and for the event log entries.</summary>
        public const string ServiceName = "iCUE-HA-Bridge";

        public static async Task Main(string[] args)
        {
            IHost host = Host.CreateDefaultBuilder(args)
                .UseWindowsService(options => { options.ServiceName = ServiceName; })
                .ConfigureLogging(logging =>
                {
                    // Entries land in the Windows Application log under this source, which the
                    // installer creates so that the service never has to create it itself.
                    logging.AddEventLog(settings => { settings.SourceName = ServiceName; });
                })
                .ConfigureServices(services =>
                {
                    services.AddHostedService<BridgeWorker>();
                })
                .Build();

            await host.RunAsync();
        }
    }
}
