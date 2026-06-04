using Shared.Contracts.Events;

namespace Shared.Contracts.Messaging;

public interface IEventPublisher
{
    Task PublishAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken = default);
}
