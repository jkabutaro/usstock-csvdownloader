namespace USStockDownloader.Options;

public class RetryOptions
{
    public int MaxRetries { get; set; } = 3;
    public int RetryDelay { get; set; } = 1000;
    public bool ExponentialBackoff { get; set; } = true;
    public int RateLimitDelay { get; set; } = 60000;
    public double JitterFactor { get; set; } = 0.2;

    public RetryOptions(
        int maxRetries = 3,
        int retryDelay = 1000,
        bool exponentialBackoff = true,
        int rateLimitDelay = 60000,
        double jitterFactor = 0.2)
    {
        MaxRetries = maxRetries;
        RetryDelay = retryDelay;
        ExponentialBackoff = exponentialBackoff;
        RateLimitDelay = rateLimitDelay;
        JitterFactor = jitterFactor;
    }
}
