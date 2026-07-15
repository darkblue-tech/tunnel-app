using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Client.Desktop.Services;

public class AuthService
{
    public static TaskCompletionSource<string> AuthCodeCompletionSource = new();

    private readonly SecretStorage _secretStorage;
    private readonly string _authServerUrl = "https://id.darkblue.tech";
    private static readonly HttpClient _httpClient = new();
    private readonly string _clientId;

    public AuthService()
    {
        _secretStorage = new SecretStorage();
        _clientId = "j_OEiI1_qO8DGgb1k6E5zVX8FC9PUvvQLvYZwRlPhTI";
    }

    public async Task<string> GetTokenAsync()
    {
        return await _secretStorage.GetSecretAsync("access_token") ?? string.Empty;
    }

    public async Task<string> LoginAsync()
    {
        var existingToken = await GetTokenAsync();
        if (!string.IsNullOrEmpty(existingToken) && !existingToken.StartsWith("dt_mock_"))
        {
            return existingToken;
        }

        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = GenerateCodeChallenge(codeVerifier);

        AuthCodeCompletionSource = new TaskCompletionSource<string>();

        var authUrl = $"https://tunnel.darkblue.tech/api/v1/auth/app/login?challenge={codeChallenge}";
        OpenBrowser(authUrl);

        try
        {
            var code = await AuthCodeCompletionSource.Task;

            if (!string.IsNullOrEmpty(code))
            {
                var idToken = await ExchangeCodeForTokenAsync(code, codeVerifier);
                if (string.IsNullOrEmpty(idToken) || idToken.StartsWith("dt_mock_"))
                {
                    await _secretStorage.SaveSecretAsync("access_token", idToken);
                    return idToken;
                }

                var serverToken = await ExchangeForServerJwtAsync(idToken);
                await _secretStorage.SaveSecretAsync("access_token", serverToken);
                return serverToken;
            }
        }
        catch (Exception)
        {
            // Handle exceptions
        }

        return string.Empty;
    }

    private async Task<string> ExchangeCodeForTokenAsync(string code, string codeVerifier)
    {
        try
        {
            // We use the proxy endpoint on our backend to securely inject the client_secret
            var request = new HttpRequestMessage(HttpMethod.Post, "https://tunnel.darkblue.tech/api/v1/auth/app/token");
            request.Content = new FormUrlEncodedContent(new[]
            {
                new System.Collections.Generic.KeyValuePair<string, string>("grant_type", "authorization_code"),
                new System.Collections.Generic.KeyValuePair<string, string>("client_id", _clientId),
                new System.Collections.Generic.KeyValuePair<string, string>("redirect_uri", "https://tunnel.darkblue.tech/api/v1/auth/app/callback"),
                new System.Collections.Generic.KeyValuePair<string, string>("code", code),
                new System.Collections.Generic.KeyValuePair<string, string>("code_verifier", codeVerifier)
            });

            var response = await _httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                System.IO.File.WriteAllText("auth_error_body.log", "Raw response: " + json); // Dump the HTML!
                try 
                {
                    var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("id_token", out var tokenElement))
                    {
                        return tokenElement.GetString() ?? "";
                    }
                    System.IO.File.WriteAllText("auth_error.log", "Token response did not contain id_token: " + json);
                } 
                catch (Exception e) 
                {
                    System.IO.File.WriteAllText("auth_error.log", $"JSON Parse Error. See auth_error_body.log. Exception: {e.Message}");
                }
            }
            else 
            {
                var errJson = await response.Content.ReadAsStringAsync();
                System.IO.File.WriteAllText("auth_error.log", $"Token request failed with {response.StatusCode}: {errJson}");
            }
        }
        catch (Exception ex)
        {
            System.IO.File.WriteAllText("auth_error.log", $"Exception during token exchange: {ex}");
        }

        // Mock fallback
        return $"dt_mock_{Guid.NewGuid():N}";
    }

    private async Task<string> ExchangeForServerJwtAsync(string idToken)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "https://tunnel.darkblue.tech/api/v1/auth/exchange");
            request.Content = new StringContent(JsonSerializer.Serialize(new { idToken = idToken }), Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("access_token", out var tokenElement))
                {
                    return tokenElement.GetString() ?? "";
                }
                System.IO.File.WriteAllText("auth_error_exchange.log", "Server exchange response did not contain access_token: " + json);
            }
            else
            {
                var errJson = await response.Content.ReadAsStringAsync();
                System.IO.File.WriteAllText("auth_error_exchange.log", $"Server exchange failed with {response.StatusCode}: {errJson}");
            }
        }
        catch (Exception ex)
        {
            System.IO.File.WriteAllText("auth_error_exchange.log", $"Exception during server exchange: {ex}");
        }
        return idToken; // Fallback
    }

    private string GenerateCodeVerifier()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Base64UrlEncode(bytes);
    }

    private string GenerateCodeChallenge(string verifier)
    {
        var bytes = Encoding.ASCII.GetBytes(verifier);
        var hash = SHA256.HashData(bytes);
        return Base64UrlEncode(hash);
    }

    private string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }

    private void OpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                url = url.Replace("&", "^&");
                Process.Start(new ProcessStartInfo("cmd", $"/c start {url}") { CreateNoWindow = true });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("xdg-open", url);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", url);
            }
        }
    }
}
