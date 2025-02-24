namespace USStockDownloader.Exceptions;

public class StockDataException : Exception
{
    public string Symbol { get; }
    public DateTime? StartDate { get; }
    public DateTime? EndDate { get; }
    public string ErrorType { get; }

    public StockDataException(string message, string symbol, string errorType, Exception? innerException = null) 
        : base(message, innerException)
    {
        Symbol = symbol;
        ErrorType = errorType;
    }

    public StockDataException(string message, string symbol, DateTime startDate, DateTime endDate, string errorType, Exception? innerException = null) 
        : base(message, innerException)
    {
        Symbol = symbol;
        StartDate = startDate;
        EndDate = endDate;
        ErrorType = errorType;
    }
}
