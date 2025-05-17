namespace Utilities.Utils
{
    public class EnvironmentVariables
    {
        public string? ServiceBusName { get; set; }
        public string? ServiceBusTopic { get; set; }
        public string? ServiceBusTopicSubscription { get; set; }
        public int MaxConcurrentSessions { get; set; }
        public int MaxMessagesPerSession { get; set; }
        public int MaxMessagesToProcessPerRun { get; set; }
        public int MaxWaitTimeForMessagesInMilliSeconds { get; set; }

        public string? DatabricksInstance { get; set; }
        public string? DatabricksAccessToken { get; set; }
        public string? DatabricksWorkflowJobId_Ingest { get; set; }
        public int DatabricksWorkflowJobStatusPollingDelay_Seconds { get; set; }
        public int DatabricksWorkflowJobStatusPollingMaxWait_Seconds { get; set; }
    }
}
