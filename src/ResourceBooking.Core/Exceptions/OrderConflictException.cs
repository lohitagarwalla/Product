namespace ResourceBooking.Core.Exceptions;

public class OrderConflictException(string message) : Exception(message);
