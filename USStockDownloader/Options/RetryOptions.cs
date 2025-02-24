namespace USStockDownloader.Options;

public class RetryOptions
{
    public int MaxRetries { get; }
    public int RetryDelay { get; }
    public bool ExponentialBackoff { get; }

    public RetryOptions(int maxRetries, int retryDelay, bool exponentialBackoff)
    {
        MaxRetries = maxRetries;
        RetryDelay = retryDelay;
        ExponentialBackoff = exponentialBackoff;
    }
}
