using System.Net.Http.Json;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Shared.Contracts.Events;

namespace Shared.Contracts.Messaging;

public sealed class ConfiguredEventPublisher(
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    ILogger<ConfiguredEventPublisher> logger) : IEventPublisher, IAsyncDisposable
{
    private readonly string? _serviceBusConnectionString = configuration["ServiceBus:ConnectionString"];
    private readonly string _topicName = configuration["ServiceBus:TopicName"] ?? "social-events";
    private readonly string? _localEventSinkUrl = configuration["Messaging:LocalEventSinkUrl"];
    private ServiceBusClient? _serviceBusClient;

    public async Task PublishAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(_serviceBusConnectionString))
        {
            _serviceBusClient ??= new ServiceBusClient(_serviceBusConnectionString);
            var sender = _serviceBusClient.CreateSender(_topicName);
            var json = JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType());
            var message = new ServiceBusMessage(json)
            {
                Subject = integrationEvent.EventName,
                MessageId = integrationEvent.EventId.ToString()
            };
            message.ApplicationProperties["eventName"] = integrationEvent.EventName;
            await sender.SendMessageAsync(message, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(_localEventSinkUrl))
        {
            var client = httpClientFactory.CreateClient(nameof(ConfiguredEventPublisher));
            var url = $"{_localEventSinkUrl.TrimEnd('/')}/events/{integrationEvent.EventName}";
            using var content = JsonContent.Create(integrationEvent, integrationEvent.GetType());
            var response = await client.PostAsync(url, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Local event sink rejected {EventName} with status {StatusCode}.",
                    integrationEvent.EventName,
                    response.StatusCode);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_serviceBusClient is not null)
        {
            await _serviceBusClient.DisposeAsync();
        }
    }
}
