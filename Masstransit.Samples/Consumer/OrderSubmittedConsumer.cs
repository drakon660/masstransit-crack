using Contracts;
using MassTransit;

public class OrderSubmittedConsumer : IConsumer<OrderSubmitted>
{
    private readonly ILogger<OrderSubmittedConsumer> _logger;

    public OrderSubmittedConsumer(ILogger<OrderSubmittedConsumer> logger)
    {
        _logger = logger;
    }

    public Task Consume(ConsumeContext<OrderSubmitted> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "Received order {OrderId}: {Quantity}x {ProductName} at {Timestamp}",
            message.OrderId, message.Quantity, message.ProductName, message.Timestamp);

        return Task.CompletedTask;
    }
}
