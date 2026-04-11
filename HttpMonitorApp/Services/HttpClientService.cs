using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace HttpMonitor.Services
{
    public class HttpClientService : IDisposable
    {
        private readonly HttpClient _httpClient;

        public HttpClientService()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public async Task<string> SendGetAsync(string url)
        {
            try
            {
                var response = await _httpClient.GetAsync(url);
                var content = await response.Content.ReadAsStringAsync();

                return $"Status: {(int)response.StatusCode} {response.StatusCode}\n\n{FormatJson(content)}";
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }

        public async Task<string> SendPostAsync(string url, string jsonBody)
        {
            try
            {
                var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(url, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                return $"Status: {(int)response.StatusCode} {response.StatusCode}\n\n{FormatJson(responseContent)}";
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }

        public async Task<string> SendPutAsync(string url, string jsonBody)
        {
            try
            {
                var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                var response = await _httpClient.PutAsync(url, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                return $"Status: {(int)response.StatusCode} {response.StatusCode}\n\n{FormatJson(responseContent)}";
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }

        public async Task<string> SendDeleteAsync(string url)
        {
            try
            {
                var response = await _httpClient.DeleteAsync(url);
                var content = await response.Content.ReadAsStringAsync();

                return $"Status: {(int)response.StatusCode} {response.StatusCode}\n\n{FormatJson(content)}";
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }

        private string FormatJson(string json)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(json))
                    return "No content";

                var jsonObj = JsonDocument.Parse(json);
                return JsonSerializer.Serialize(jsonObj, new JsonSerializerOptions { WriteIndented = true });
            }
            catch
            {
                return json;
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}