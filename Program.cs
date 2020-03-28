using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace TestDeviceFlow
{
    class Program
    {
        private const string TenantId = "<your tenant id>";
        private const string ClientId = "<your client id>";

        static async Task Main(string[] args)
        {
            using var client = new HttpClient();
            var authorizationResponse = await StartDeviceFlowAsync(client);
            Console.WriteLine("Please visit this URL: " + authorizationResponse.VerificationUri);
            Console.WriteLine("And enter the following code: " + authorizationResponse.UserCode);
            OpenWebPage(authorizationResponse.VerificationUri);
            var tokenResponse = await GetTokenAsync(client, authorizationResponse);
            Console.WriteLine("Access token: ");
            Console.WriteLine(tokenResponse.AccessToken);
            Console.WriteLine("ID token: ");
            Console.WriteLine(tokenResponse.IdToken);
            Console.WriteLine("Refresh token: ");
            Console.WriteLine(tokenResponse.RefreshToken);
        }

        private static void OpenWebPage(string url)
        {
            var psi = new ProcessStartInfo(url)
            {
                UseShellExecute = true
            };
            Process.Start(psi);
        }

        private static async Task<DeviceAuthorizationResponse> StartDeviceFlowAsync(HttpClient client)
        {
            string deviceEndpoint = $"https://login.microsoftonline.com/{TenantId}/oauth2/v2.0/devicecode";
            var request = new HttpRequestMessage(HttpMethod.Post, deviceEndpoint)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = ClientId,
                    ["scope"] = "openid profile offline_access"
                })
            };
            var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<DeviceAuthorizationResponse>(json);
        }

        private static async Task<TokenResponse> GetTokenAsync(HttpClient client, DeviceAuthorizationResponse authResponse)
        {
            string tokenEndpoint = $"https://login.microsoftonline.com/{TenantId}/oauth2/v2.0/token";

            // Poll until we get a valid token response or a fatal error
            int pollingDelay = authResponse.Interval;
            while (true)
            {
                var request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint)
                {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                        ["device_code"] = authResponse.DeviceCode,
                        ["client_id"] = ClientId
                    })
                };
                var response = await client.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode)
                {
                    return JsonSerializer.Deserialize<TokenResponse>(json);
                }
                else
                {
                    var errorResponse = JsonSerializer.Deserialize<TokenErrorResponse>(json);
                    switch(errorResponse.Error)
                    {
                        case "authorization_pending":
                            // Not complete yet, wait and try again later
                            break;
                        case "slow_down":
                            // Not complete yet, and we should slow down the polling
                            pollingDelay += 5;                            
                            break;
                        default:
                            // Some other error, nothing we can do but throw
                            throw new Exception(
                                $"Authorization failed: {errorResponse.Error} - {errorResponse.ErrorDescription}");
                    }

                    await Task.Delay(TimeSpan.FromSeconds(pollingDelay));
                }
            }
        }

        private class DeviceAuthorizationResponse
        {
            [JsonPropertyName("device_code")]
            public string DeviceCode { get; set; }

            [JsonPropertyName("user_code")]
            public string UserCode { get; set; }

            [JsonPropertyName("verification_uri")]
            public string VerificationUri { get; set; }

            [JsonPropertyName("expires_in")]
            public int ExpiresIn { get; set; }

            [JsonPropertyName("interval")]
            public int Interval { get; set; }
        }

        private class TokenErrorResponse
        {
            [JsonPropertyName("error")]
            public string Error { get; set; }

            [JsonPropertyName("error_description")]
            public string ErrorDescription { get; set; }
        }

        private class TokenResponse
        {
            [JsonPropertyName("access_token")]
            public string AccessToken { get; set; }

            [JsonPropertyName("id_token")]
            public string IdToken { get; set; }

            [JsonPropertyName("refresh_token")]
            public string RefreshToken { get; set; }

            [JsonPropertyName("token_type")]
            public string TokenType { get; set; }

            [JsonPropertyName("expires_in")]
            public int ExpiresIn { get; set; }

            [JsonPropertyName("scope")]
            public string Scope { get; set; }
        }
    }
}
