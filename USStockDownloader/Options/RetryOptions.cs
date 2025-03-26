namespace USStockDownloader.Options;

public class RetryOptions
{
    public int MaxRetries { get; set; } = 5;
    public int RetryDelay { get; set; } = 5000;
    public bool ExponentialBackoff { get; set; } = true;
    public int RateLimitDelay { get; set; } = 120000;
    public double JitterFactor { get; set; } = 0.2;
    public System.TimeSpan Delay { get; set; } = System.TimeSpan.FromSeconds(2);
    public System.TimeSpan Timeout { get; set; } = System.TimeSpan.FromSeconds(30);

    public RetryOptions()
    {
    }

    public RetryOptions(
        int maxRetries,
        int retryDelay,
        bool exponentialBackoff,
        int rateLimitDelay,
        double jitterFactor = 0.2)
    {
        MaxRetries = maxRetries;
        RetryDelay = retryDelay;
        ExponentialBackoff = exponentialBackoff;
        RateLimitDelay = rateLimitDelay;
        JitterFactor = jitterFactor;
    }
}
