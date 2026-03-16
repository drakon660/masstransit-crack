using Carter;
using Contracts;
using MassTransit;

namespace Publisher.Modules;

public sealed class OrdersModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/orders")
            .WithTags("Orders");

        group.MapPost("/", SubmitOrder)
            .WithName("SubmitOrder")
            .WithSummary("Publishes an OrderSubmitted message to MassTransit.")
            .Produces<SubmitOrderResponse>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem();
    }

    private static async Task<IResult> SubmitOrder(
        CreateOrderRequest request,
        IPublishEndpoint publishEndpoint,
        ILogger<OrdersModule> logger)
    {
        var message = new OrderSubmitted(
            Guid.NewGuid(),
            request.ProductName.Trim(),
            request.Quantity,
            DateTime.UtcNow);

        await publishEndpoint.Publish(message);
        logger.LogInformation(
            "Published order {OrderId}: {Quantity}x {ProductName}",
            message.OrderId,
            message.Quantity,
            message.ProductName);

        return Results.Accepted(
            $"/orders/{message.OrderId}",
            new SubmitOrderResponse(message.OrderId, "Published"));
    }
}

public sealed record CreateOrderRequest(string ProductName, int Quantity);

public sealed record SubmitOrderResponse(Guid OrderId, string Status);
