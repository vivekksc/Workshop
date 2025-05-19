using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.DependencyInjection;

namespace Utilities.Utils
{
    public static class DIExtensions
    {
        public static IServiceCollection AddServiceBusClient(this IServiceCollection services,
                                                                  bool useManagedIdentity,
                                                                  string serviceBusName,
                                                                  string serviceBusConnectionString
                                                                  )
        {
            services.AddAzureClients(builder =>
            {
                if (useManagedIdentity)
                {
                    // Adding using managed identity
                    builder.AddServiceBusClientWithNamespace($"{serviceBusName}.servicebus.windows.net").WithName(serviceBusName);
                }
                else
                {
                    // Adding using connection string
                    builder.AddServiceBusClient(serviceBusConnectionString).WithName(serviceBusName);
                }
            });

            return services;
        }

        public static IServiceCollection AddServiceBusClientAndSender(this IServiceCollection services,
                                                                  bool useManagedIdentity,
                                                                  string topicName,
                                                                  string? serviceBusName,
                                                                  string? serviceBusConnectionString
                                                                  )
        {
            services.AddAzureClients(builder =>
            {
                if (useManagedIdentity)
                {
                    // Adding using managed identity
                    builder.AddServiceBusClientWithNamespace($"{serviceBusName}.servicebus.windows.net");
                }
                else
                {
                    // Adding using connection string
                    builder.AddServiceBusClient(serviceBusConnectionString);
                }

                builder.AddClient<ServiceBusSender, ServiceBusClientOptions>((_, _, provider) =>
                    provider
                        .GetRequiredService<ServiceBusClient>()
                        .CreateSender(topicName)
                )
                .WithName(topicName);
            });

            return services;
        }

        public static IServiceCollection AddServiceBusClientAndReceiverV2(this IServiceCollection services,
                                                                  bool useManagedIdentity,
                                                                  string topicName,
                                                                  string subscriptionName,
                                                                  string serviceBusName,
                                                                  string serviceBusConnectionString
                                                                  )
        {
            // Register the ServiceBusClient
            services.AddSingleton(provider =>
            {
                if (useManagedIdentity)
                {
                    var credential = new DefaultAzureCredential();
                    return new ServiceBusClient($"{serviceBusName}.servicebus.windows.net", credential);
                }
                else
                {
                    return new ServiceBusClient(serviceBusConnectionString);
                }
            });

            // Register the ServiceBusReceiver
            services.AddSingleton(provider =>
            {
                var serviceBusClient = provider.GetRequiredService<ServiceBusClient>();
                ServiceBusReceiverOptions receiverOptions = new() { PrefetchCount = 20 };
                return serviceBusClient.CreateReceiver(topicName, subscriptionName, receiverOptions);
            });

            return services;
        }

        public static IServiceCollection AddServiceBusClientAndReceiver(this IServiceCollection services,
                                                                  bool useManagedIdentity,
                                                                  string topicName,
                                                                  string subscriptionName,
                                                                  string serviceBusName,
                                                                  string serviceBusConnectionString
                                                                  )
        {
            services.AddAzureClients(builder =>
            {
                if (useManagedIdentity)
                {
                    // Adding using managed identity
                    builder.AddServiceBusClientWithNamespace($"{serviceBusName}.servicebus.windows.net").WithName(serviceBusName);
                }
                else
                {
                    // Adding using connection string
                    builder.AddServiceBusClient(serviceBusConnectionString).WithName(serviceBusName);
                }

                ServiceBusReceiverOptions receiverOptions = new()
                {
                    PrefetchCount = 20
                };
                builder.AddClient<ServiceBusReceiver, ServiceBusClientOptions>((_, _, provider) =>
                    provider
                        .GetRequiredService<ServiceBusClient>()
                        .CreateReceiver(topicName, subscriptionName, receiverOptions)
                )
                .WithName(subscriptionName);
            });

            return services;
        }
    }
}
