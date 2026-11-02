namespace OpenDataDownloader.Core;

/// <summary>
/// Exception thrown when a data fetcher encounters an unrecoverable error during HTTP communication or parsing.
/// </summary>
public class DataFetcherException : Exception
{
    /// <summary>
    /// Creates a new DataFetcherException with a custom message.
    /// </summary>
    public DataFetcherException(string message) : base(message) { }

    /// <summary>
    /// Creates a new DataFetcherException with a custom message and inner exception.
    /// </summary>
    public DataFetcherException(string message, Exception innerException) : base(message, innerException) { }
}
