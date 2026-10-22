namespace ResourceBooking.Core.Exceptions;

// Opening a connection failed before the pending database operation could execute.
public sealed class DatabaseUnavailableException(Exception? innerException)
    : Exception("The database is temporarily unavailable. Please try again later.", innerException);
