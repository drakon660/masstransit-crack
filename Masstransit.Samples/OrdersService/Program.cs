using System.Collections.Concurrent;
using Contracts;

var builder = WebApplication.CreateBuilder(args);
var starterOrder = new OrderSubmitted(Guid.NewGuid(), "Starter Kit", 2, DateTime.UtcNow.AddMinutes(-15));

builder.Services.AddOpenApi();
builder.Services.AddSingleton(new ConcurrentDictionary<Guid, OrderSubmitted>(new[]
{
    new KeyValuePair<Guid, OrderSubmitted>(
        starterOrder.OrderId,
        starterOrder)
}));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGet("/", () => TypedResults.Ok(new ServiceStatus("OrdersService", "Running")))
    .WithName("GetOrdersServiceStatus");

var orders = app.MapGroup("/orders")
    .WithTags("Orders");

orders.MapGet("/", (ConcurrentDictionary<Guid, OrderSubmitted> store) =>
        TypedResults.Ok(store.Values.OrderByDescending(order => order.Timestamp).ToArray()))
    .WithName("GetOrders");

orders.MapGet("/{orderId:guid}", GetOrderById)
    .WithName("GetOrderById");

orders.MapPost("/", CreateOrder)
.WithName("CreateOrder");

app.Run();

static IResult GetOrderById(Guid orderId, ConcurrentDictionary<Guid, OrderSubmitted> store) =>
    store.TryGetValue(orderId, out var order)
        ? TypedResults.Ok(order)
        : TypedResults.NotFound();

static IResult CreateOrder(CreateOrderRequest request, ConcurrentDictionary<Guid, OrderSubmitted> store)
{
    if (string.IsNullOrWhiteSpace(request.ProductName) || request.Quantity <= 0)
    {
        return TypedResults.ValidationProblem(new Dictionary<string, string[]>
        {
            ["productName"] = string.IsNullOrWhiteSpace(request.ProductName)
                ? ["Product name is required."]
                : [],
            ["quantity"] = request.Quantity <= 0
                ? ["Quantity must be greater than zero."]
                : []
        }.Where(pair => pair.Value.Length > 0)
         .ToDictionary(pair => pair.Key, pair => pair.Value));
    }

    var order = new OrderSubmitted(
        Guid.NewGuid(),
        request.ProductName.Trim(),
        request.Quantity,
        DateTime.UtcNow);

    store[order.OrderId] = order;

    return TypedResults.Created($"/orders/{order.OrderId}", order);
}

internal sealed record CreateOrderRequest(string ProductName, int Quantity);

internal sealed record ServiceStatus(string Service, string Status);
