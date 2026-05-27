using System.Text;
using System.Text.Json;

namespace ForexBot.Services;

public class MetaApiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _accountId;
    private string _clientBaseUrl = "";

    private const string ProvisioningUrl = "https://mt-provisioning-api-v1.agiliumtrade.agiliumtrade.ai";
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public MetaApiClient(string token, string accountId)
    {
        _accountId = accountId;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.Add("auth-token", token);
    }

    public async Task InitializeAsync()
    {
        Console.WriteLine("  Connecting to MetaAPI...");
        var json = await GetStringAsync($"{ProvisioningUrl}/users/current/accounts/{_accountId}");
        var account = Parse(json);
        var region = account.GetProperty("region").GetString() ?? "new-york";
        _clientBaseUrl = $"https://mt-client-api-v1.{region}.agiliumtrade.ai";
        Console.WriteLine($"  MetaAPI region: {region}");
    }

    public async Task WaitForDeployedAsync(int timeoutSeconds = 90)
    {
        Console.Write("  Waiting for MT5 connection");
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var json = await GetStringAsync($"{ProvisioningUrl}/users/current/accounts/{_accountId}");
                var account = Parse(json);
                if (account.TryGetProperty("state", out var state) && state.GetString() == "DEPLOYED")
                {
                    Console.WriteLine(" connected.");
                    return;
                }
            }
            catch { /* retry */ }

            Console.Write(".");
            await Task.Delay(5000);
        }

        Console.WriteLine(" timed out.");
        throw new TimeoutException("MT5 account did not connect within the timeout. Check MetaAPI dashboard.");
    }

    public async Task<JsonElement> GetAccountInfoAsync()
    {
        var json = await GetStringAsync($"{_clientBaseUrl}/users/current/accounts/{_accountId}/account-information");
        return Parse(json);
    }

    public async Task<JsonElement> GetCurrentPriceAsync(string symbol)
    {
        var json = await GetStringAsync($"{_clientBaseUrl}/users/current/accounts/{_accountId}/symbols/{symbol}/current-price");
        return Parse(json);
    }

    public async Task<List<JsonElement>> GetCandlesAsync(string symbol, string timeframe, int limit)
    {
        var url = $"{_clientBaseUrl}/users/current/accounts/{_accountId}/historical-market-data/symbols/{symbol}/timeframes/{timeframe}/candles?limit={limit}";
        var json = await GetStringAsync(url);
        return JsonSerializer.Deserialize<List<JsonElement>>(json, JsonOpts) ?? [];
    }

    public async Task<List<JsonElement>> GetPositionsAsync()
    {
        var json = await GetStringAsync($"{_clientBaseUrl}/users/current/accounts/{_accountId}/positions");
        return JsonSerializer.Deserialize<List<JsonElement>>(json, JsonOpts) ?? [];
    }

    public async Task<JsonElement> PlaceTradeAsync(object tradeParams)
    {
        var body = new StringContent(JsonSerializer.Serialize(tradeParams), Encoding.UTF8, "application/json");
        var response = await _http.PostAsync($"{_clientBaseUrl}/users/current/accounts/{_accountId}/trade", body);
        var json = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Trade failed ({response.StatusCode}): {json}");
        return Parse(json);
    }

    private async Task<string> GetStringAsync(string url)
    {
        var response = await _http.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"GET {url} failed ({response.StatusCode}): {body}");
        return body;
    }

    private static JsonElement Parse(string json) =>
        JsonSerializer.Deserialize<JsonElement>(json, JsonOpts);

    public void Dispose() => _http.Dispose();
}
