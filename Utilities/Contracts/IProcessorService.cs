using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;

namespace Utilities.Contracts
{
    public interface IProcessorService
    {
        Task ProcessSessionAsync(ServiceBusSessionReceiver sessionReceiver, ILogger logger, string databricksJobId, string processorName);

        Task ProcessMessageAsync(ServiceBusReceivedMessage message, ILogger logger, string databricksJobId, string processorName);

        Task<bool> WaitForJobCompletionAsync(ILogger logger, int jobRunId, string processorName);
    }
}
