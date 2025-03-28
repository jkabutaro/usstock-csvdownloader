using System;

namespace USStockDownloader.Exceptions;

public class StockDataException : Exception
{
    public StockDataException(string message) : base(message) { }
    public StockDataException(string message, Exception inner) : base(message, inner) { }
}

public class InvalidSymbolException : StockDataException
{
    public InvalidSymbolException(string message) : base(message) { }
}

public class NoDataException : StockDataException
{
    public NoDataException(string message) : base(message) { }
}

public class DownloadException : StockDataException
{
    public DownloadException(string message) : base(message) { }
    public DownloadException(string message, Exception inner) : base(message, inner) { }
}

public class RateLimitException : StockDataException
{
    public RateLimitException(string message) : base(message) { }
    public RateLimitException(string message, Exception inner) : base(message, inner) { }
}

public class InternalServerErrorException : StockDataException
{
    public InternalServerErrorException(string message) : base(message) { }
    public InternalServerErrorException(string message, Exception inner) : base(message, inner) { }
}
