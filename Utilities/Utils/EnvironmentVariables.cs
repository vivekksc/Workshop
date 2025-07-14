namespace Utilities.Utils
{
    public class EnvironmentVariables
    {
        public string? StorageAccountName { get; set; }
        public string? StorageAccountContainer_Ingestion { get; set; }
        public bool StorageAccount_UseManagedIdentity { get; set; }
        public string? StorageAccount_ConnectionString { get; set; }
        public string? ServiceBusName { get; set; }
        public string? ServiceBusTopic_MDS { get; set; }
        public string? ServiceBusTopic_BW { get; set; }
        public string? ServiceBusTopicSubscription_Update { get; set; }
        public string? ServiceBusTopicSubscription_Comment { get; set; }
        public string? ServiceBusTopicSubscription_Kudos { get; set; }
        public string? ServiceBusTopicSubscription_Event { get; set; }
        public string? ServiceBusTopicSubscription_Bookmark { get; set; }
        public string? ServiceBusTopicSubscription_Article { get; set; }
        public string? ServiceBusTopicSubscriptionBW { get; set; }
        public int MaxConcurrentSessions { get; set; }
        public int MaxMessagesPerSession { get; set; }
        public int MaxMessagesToProcessPerRun { get; set; }
        public int MaxConcurrentSessions_BW { get; set; }
        public int MaxMessagesToProcessPerRun_BW { get; set; }
        public int MaxWaitTimeForMessagesInMilliSeconds { get; set; }

        public string? DatabricksInstance { get; set; }
        public string? DatabricksAccessToken { get; set; }
        public string? DatabricksWorkflowJobId_Ingest { get; set; }
        public string? DatabricksWorkflowJobId_PublishBW{ get; set; }
        public int DatabricksWorkflowJobStatusPollingDelay_Seconds { get; set; }
        public int DatabricksWorkflowJobStatusPollingMaxWait_Seconds_Ingest { get; set; }
        public int DatabricksWorkflowJobStatusPollingMaxWait_Seconds_PublishBW { get; set; }
    }
}
