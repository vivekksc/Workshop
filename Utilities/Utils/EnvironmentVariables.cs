namespace Utilities.Utils
{
    public class EnvironmentVariables
    {
        public string? ServiceBusTopic { get; set; }
        public string? ServiceBusTopicSubscription { get; set; }
        public int MaxMessagesToProcessPerRun { get; set; }
        public int MaxWaitTimeForMessagesInMilliSeconds { get; set; }

        public string? DatabricksInstance { get; set; }
        public string? DatabricksAccessToken { get; set; }
        public string? DatabricksWorkflowJobId_Ingest { get; set; }
    }
}
