namespace ObservabilityDemo.OrderService.Domain;

public record Order(
    string OrderId,
    string CustomerId,
    string CustomerEmail,  // PII - used in Module 4 to show PII masking
    string ProductSku,
    int Quantity);
