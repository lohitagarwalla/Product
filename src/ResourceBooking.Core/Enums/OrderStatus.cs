using System.Text.Json.Serialization;

namespace ResourceBooking.Core.Enums;

[JsonConverter(typeof(JsonStringEnumConverter<OrderStatus>))]
public enum OrderStatus
{
    Placed,
    Shipped,
    Delivered,
    Cancelled
}
