namespace USStockDownloader.Exceptions;

public class DataParsingException : Exception
{
    public DataParsingException(string message) : base(message)
    {
    }

    public DataParsingException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
