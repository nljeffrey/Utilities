using System;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using VWT.IP.KPN.SiteExportManager.Managers.Interfaces;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using Azure.Identity;
using Microsoft.Extensions.Configuration;

// Send messages to the Azure ServiceBus as a Batch with super high performance.

/// <inheritdoc />
public class ServiceBusManager : IServiceBusManager
{
    private const string ServiceBusFQDNKey = "ServiceBusNamespace";
    private readonly Queue<ServiceBusMessage> messages = new Queue<ServiceBusMessage>();
    private ServiceBusClient serviceBusClient;

    private readonly ILogger<ServiceBusManager> _logger;
    private readonly IConfiguration _configuration;

    public ServiceBusManager(ILogger<ServiceBusManager> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;

        CreateServiceBusClient();
    }

    internal ServiceBusManager(ILogger<ServiceBusManager> logger, ServiceBusClient serviceBusClient)
    {
        this.serviceBusClient = serviceBusClient;
        _logger = logger;
    }

    private void CreateServiceBusClient()
    {
        string connectionString = _configuration[ServiceBusFQDNKey];
        var security = new DefaultAzureCredential();
        serviceBusClient = new ServiceBusClient(connectionString, security);
    }

    /// <inheritdoc />
    public async Task SendMessages(string topicName, IList<string> messagesToSend, string messageTo, string messageAction, string messageType)
    {
        try
        {
            // Queue messages to send on internal queue to be able to batch them
            foreach (var msg in messagesToSend)
            {
                var sbMessage = new ServiceBusMessage(msg)
                {
                    CorrelationId = Guid.NewGuid().ToString(),
                    To = messageTo
                };
                sbMessage.ApplicationProperties.Add("action", messageAction);
                sbMessage.ApplicationProperties.Add("type", messageType);

                messages.Enqueue(sbMessage);
            }

            // Create sender
            var sender = serviceBusClient.CreateSender(topicName);

            while (messages.Count > 0)
            {
                using var messageBatch = await sender.CreateMessageBatchAsync();

                // Keep adding as much messages as possible to the batch
                while (messages.Count > 0 && messageBatch.TryAddMessage(messages.Peek()))
                {
                    // Dequeue the message from the internal queue when it has been added to the batch
                    messages.Dequeue();
                }

                // Send the batch (while loop continues if messages are left to be send/batched)
                await sender.SendMessagesAsync(messageBatch);
            }

        }
        catch (Exception ex)
        {
            _logger.LogError("Cannot send service bus messages.", ex);
            throw;
        }
    }
}
