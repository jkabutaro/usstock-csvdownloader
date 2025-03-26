using System.Net.Http;

namespace USStockDownloader.Services
{
    public class HttpClientFactoryWrapper : IHttpClientFactory
    {
        private readonly HttpClient _httpClient;
        
        public HttpClientFactoryWrapper(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public HttpClient CreateClient(string name) => _httpClient;
    }
}
