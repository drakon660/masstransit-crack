namespace Contracts;

public record OrderSubmitted(Guid OrderId, string ProductName, int Quantity, DateTime Timestamp);
