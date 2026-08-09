using Newtonsoft.Json;
using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace FortniteLauncher
{
    public sealed class HistoricalShopService
    {
        private const string ApiBaseUrl = "https://fortforge.co.uk/api/v1/shops";
        private const string HealthCheckUrl = ApiBaseUrl + "/health";
        private const string CurrentShopUrl = ApiBaseUrl + "/current";
        private static readonly HttpClient HttpClient = new();
        private static readonly object CacheLock = new();
        private static readonly Dictionary<string, HistoricalShopResult> ShopCache = new();
        private static CurrentHistoricalShopResult CachedCurrentShop;

        public async Task<bool> IsShopServiceAvailableAsync()
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var request = new HttpRequestMessage(HttpMethod.Get, HealthCheckUrl);
                using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                return response.IsSuccessStatusCode;
            }
            catch (HttpRequestException)
            {
                return false;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        public async Task<CurrentHistoricalShopResult> GetCurrentShopAsync(bool forceRefresh = false)
        {
            lock (CacheLock)
            {
                if (!forceRefresh && CachedCurrentShop?.IsSuccess == true)
                    return CachedCurrentShop;
            }

            try
            {
                var requestUrl = forceRefresh
                    ? $"{CurrentShopUrl}?refresh={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"
                    : CurrentShopUrl;
                using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                if (forceRefresh)
                {
                    request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
                    {
                        NoCache = true,
                        NoStore = true,
                    };
                }
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return new CurrentHistoricalShopResult(404, null, null, null);
                }
                if (!response.IsSuccessStatusCode)
                    return new CurrentHistoricalShopResult((int)response.StatusCode, null, null, "The item shop service could not be reached. Please try again.");

                var shopDateHeader = response.Headers.TryGetValues("X-Shop-Date", out var shopDateValues)
                    ? shopDateValues.FirstOrDefault()
                    : null;
                if (!DateTimeOffset.TryParseExact(shopDateHeader, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var shopDate))
                    return new CurrentHistoricalShopResult((int)response.StatusCode, null, null, "The current shop response did not provide a valid shop date.");

                var content = await response.Content.ReadAsStringAsync(timeout.Token);
                var shop = JsonConvert.DeserializeObject<HistoricalShopApiResponse>(content);
                var result = shop?.Data is null
                    ? new CurrentHistoricalShopResult((int)response.StatusCode, null, null, "The historical shop response was invalid.")
                    : new CurrentHistoricalShopResult((int)response.StatusCode, shopDate.Date, shop, null);
                if (result.IsSuccess)
                {
                    lock (CacheLock)
                    {
                        CachedCurrentShop = result;
                    }
                }

                return result;
            }
            catch (HttpRequestException)
            {
                return new CurrentHistoricalShopResult(0, null, null, "The item shop service could not be reached. Please try again.");
            }
            catch (OperationCanceledException)
            {
                return new CurrentHistoricalShopResult(0, null, null, "The item shop service timed out. Please try again.");
            }
        }

        public async Task<HistoricalShopResult> GetShopAsync(DateTimeOffset date, bool forceRefresh = false)
        {
            var datePath = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            lock (CacheLock)
            {
                if (!forceRefresh && ShopCache.TryGetValue(datePath, out var cachedResult))
                    return cachedResult;
            }

            try
            {
                using var response = await HttpClient.GetAsync($"{ApiBaseUrl}/{datePath}");
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    var notFoundResult = new HistoricalShopResult(404, null, null);
                    CacheResult(datePath, notFoundResult);
                    return notFoundResult;
                }
                if (!response.IsSuccessStatusCode) return new HistoricalShopResult((int)response.StatusCode, null, "The item shop service could not be reached. Please try again.");

                var content = await response.Content.ReadAsStringAsync();
                var shop = JsonConvert.DeserializeObject<HistoricalShopApiResponse>(content);
                var result = shop?.Data is null
                    ? new HistoricalShopResult((int)response.StatusCode, null, "The historical shop response was invalid.")
                    : new HistoricalShopResult((int)response.StatusCode, shop, null);
                if (result.IsSuccess) CacheResult(datePath, result);
                return result;
            }
            catch (HttpRequestException)
            {
                return new HistoricalShopResult(0, null, "The item shop service could not be reached. Please try again.");
            }
        }

        private static void CacheResult(string datePath, HistoricalShopResult result)
        {
            lock (CacheLock)
            {
                ShopCache[datePath] = result;
            }
        }
    }

    public sealed record HistoricalShopResult(int StatusCode, HistoricalShopApiResponse Shop, string ErrorMessage)
    {
        public bool IsSuccess => StatusCode is >= 200 and < 300;
    }

    public sealed record CurrentHistoricalShopResult(int StatusCode, DateTimeOffset? Date, HistoricalShopApiResponse Shop, string ErrorMessage)
    {
        public bool IsSuccess => StatusCode is >= 200 and < 300;
    }
}
