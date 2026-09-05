using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Client.Core.Services;

/// <summary>
/// Service responsible for managing user authentication, token acquisition, and OAuth2 flow.
/// </summary>
public class AuthService
{
    public static TaskCompletionSource<string> AuthCodeCompletionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly SecretStorage _secretStorage;
    private static readonly HttpClient _httpClient = new();
    private readonly string _clientId;

    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public AuthService()
    {
        _secretStorage = new SecretStorage();
        _clientId = "j_OEiI1_qO8DGgb1k6E5zVX8FC9PUvvQLvYZwRlPhTI";
    }

    /// <summary>
    /// Checks if a given JWT access token is expired or expiring within the specified skew window.
    /// </summary>
    public static bool IsTokenExpired(string token, int skewSeconds = 60)
    {
        if (string.IsNullOrWhiteSpace(token)) return true;

        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2) return false;

            var payloadStr = parts[1].Replace('-', '+').Replace('_', '/');
            switch (payloadStr.Length % 4)
            {
                case 2: payloadStr += "=="; break;
                case 3: payloadStr += "="; break;
            }

            var payloadBytes = Convert.FromBase64String(payloadStr);
            using var doc = JsonDocument.Parse(payloadBytes);
            if (doc.RootElement.TryGetProperty("exp", out var expEl) && expEl.TryGetInt64(out var exp))
            {
                var expTime = DateTimeOffset.FromUnixTimeSeconds(exp);
                return DateTimeOffset.UtcNow.AddSeconds(skewSeconds) >= expTime;
            }
        }
        catch
        {
            // If token parsing fails, do not assume expired statically; let the API validate it
        }

        return false;
    }

    /// <summary>
    /// Returns true if a refresh token is present in secure storage, indicating an existing session.
    /// </summary>
    public async Task<bool> HasSavedSessionAsync()
    {
        var refreshToken = await _secretStorage.GetSecretAsync("refresh_token");
        return !string.IsNullOrEmpty(refreshToken);
    }

    /// <summary>
    /// Retrieves a valid access token. If the stored access token is missing or expired,
    /// it automatically attempts to refresh it using the stored refresh token.
    /// </summary>
    public async Task<string> GetTokenAsync(bool forceRefresh = false)
    {
        var token = await _secretStorage.GetSecretAsync("access_token") ?? string.Empty;

        if (forceRefresh || string.IsNullOrEmpty(token) || IsTokenExpired(token))
        {
            var refreshToken = await _secretStorage.GetSecretAsync("refresh_token");
            if (!string.IsNullOrEmpty(refreshToken))
            {
                var refreshedToken = await RefreshTokenAsync();
                if (!string.IsNullOrEmpty(refreshedToken))
                {
                    return refreshedToken;
                }
            }
        }

        return token;
    }

    public async Task LogoutAsync()
    {
        await _secretStorage.ClearSecretAsync("access_token");
        await _secretStorage.ClearSecretAsync("refresh_token");
        await _secretStorage.ClearSecretAsync("profile_name");
    }

    public async Task<string?> LoginAsync()
    {
        var existingToken = await GetTokenAsync();
        if (!string.IsNullOrEmpty(existingToken))
        {
            return existingToken;
        }

        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = GenerateCodeChallenge(codeVerifier);

        await _secretStorage.SaveSecretAsync("pending_code_verifier", codeVerifier);

        if (AuthCodeCompletionSource != null && !AuthCodeCompletionSource.Task.IsCompleted)
        {
            AuthCodeCompletionSource.TrySetCanceled();
        }
        AuthCodeCompletionSource = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        var authUrl = $"https://tunnel.darkblue.tech/api/v1/auth/app/login?challenge={codeChallenge}";
        OpenBrowser(authUrl);

        try
        {
            var code = await AuthCodeCompletionSource.Task;

            if (!string.IsNullOrEmpty(code))
            {
                var idToken = await ExchangeCodeForTokenAsync(code, codeVerifier);
                if (string.IsNullOrEmpty(idToken))
                {
                    return string.Empty;
                }

                var serverToken = await ExchangeForServerJwtAsync(idToken);
                await _secretStorage.SaveSecretAsync("access_token", serverToken);
                await ExtractAndSaveProfileAsync(idToken);
                return serverToken;
            }
        }
        catch (TaskCanceledException)
        {
            return null; // Indicates it was cancelled by a new login attempt
        }
        catch (Exception)
        {
            // Handle other exceptions
        }
        finally
        {
            await _secretStorage.ClearSecretAsync("pending_code_verifier");
        }

        return string.Empty;
    }

    /// <summary>
    /// Completes authentication when a deep link is received during a cold start or after process recovery.
    /// Uses the securely stored code verifier if available.
    /// </summary>
    public async Task<string?> CompletePendingLoginAsync(string code)
    {
        try
        {
            var codeVerifier = await _secretStorage.GetSecretAsync("pending_code_verifier");
            if (string.IsNullOrEmpty(codeVerifier)) return null;

            var idToken = await ExchangeCodeForTokenAsync(code, codeVerifier);
            if (string.IsNullOrEmpty(idToken)) return null;

            var serverToken = await ExchangeForServerJwtAsync(idToken);
            if (!string.IsNullOrEmpty(serverToken))
            {
                await _secretStorage.SaveSecretAsync("access_token", serverToken);
                await ExtractAndSaveProfileAsync(idToken);
                await _secretStorage.ClearSecretAsync("pending_code_verifier");
                return serverToken;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AuthService] CompletePendingLoginAsync error: {ex.Message}");
        }
        return null;
    }

    public async Task<string?> RefreshTokenAsync(CancellationToken cancellationToken = default)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            // Check if another concurrent call already refreshed the token
            var existingToken = await _secretStorage.GetSecretAsync("access_token");
            if (!string.IsNullOrEmpty(existingToken) && !IsTokenExpired(existingToken))
            {
                return existingToken;
            }

            var refreshToken = await _secretStorage.GetSecretAsync("refresh_token");
            if (string.IsNullOrEmpty(refreshToken)) return null;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(15));

            var request = new HttpRequestMessage(HttpMethod.Post, "https://tunnel.darkblue.tech/api/v1/auth/app/token");
            request.Content = new FormUrlEncodedContent(new[]
            {
                new System.Collections.Generic.KeyValuePair<string, string>("grant_type", "refresh_token"),
                new System.Collections.Generic.KeyValuePair<string, string>("client_id", _clientId),
                new System.Collections.Generic.KeyValuePair<string, string>("refresh_token", refreshToken)
            });

            var response = await _httpClient.SendAsync(request, cts.Token);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cts.Token);
                var doc = JsonDocument.Parse(json);
                
                if (doc.RootElement.TryGetProperty("refresh_token", out var refreshElement))
                {
                    var newRefreshToken = refreshElement.GetString();
                    if (!string.IsNullOrEmpty(newRefreshToken))
                    {
                        await _secretStorage.SaveSecretAsync("refresh_token", newRefreshToken);
                    }
                }

                if (doc.RootElement.TryGetProperty("id_token", out var tokenElement))
                {
                    var idToken = tokenElement.GetString() ?? "";
                    var serverToken = await ExchangeForServerJwtAsync(idToken);
                    if (!string.IsNullOrEmpty(serverToken))
                    {
                        await _secretStorage.SaveSecretAsync("access_token", serverToken);
                        await ExtractAndSaveProfileAsync(idToken);
                        return serverToken;
                    }
                }
            }
            else if (response.StatusCode == HttpStatusCode.BadRequest || response.StatusCode == HttpStatusCode.Unauthorized)
            {
                // Refresh token is revoked or permanently invalid -> clear credentials
                await LogoutAsync();
            }
            else
            {
                // 5xx server error or other temporary failure - do not logout
                Debug.WriteLine($"Token refresh endpoint returned {response.StatusCode}");
            }
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine("Token refresh timed out or was cancelled");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Exception during token refresh: {ex}");
        }
        finally
        {
            _refreshLock.Release();
        }

        return null;
    }

    private async Task ExtractAndSaveProfileAsync(string idToken)
    {
        try
        {
            var parts = idToken.Split('.');
            if (parts.Length >= 2)
            {
                var payloadStr = parts[1].Replace('-', '+').Replace('_', '/');
                switch (payloadStr.Length % 4)
                {
                    case 2: payloadStr += "=="; break;
                    case 3: payloadStr += "="; break;
                }

                var payloadBytes = Convert.FromBase64String(payloadStr);
                var payloadJson = Encoding.UTF8.GetString(payloadBytes);
                
                using var doc = JsonDocument.Parse(payloadJson);
                if (doc.RootElement.TryGetProperty("name", out var nameEl))
                {
                    await _secretStorage.SaveSecretAsync("profile_name", nameEl.GetString() ?? "Unknown User");
                }
                else if (doc.RootElement.TryGetProperty("preferred_username", out var userEl))
                {
                    await _secretStorage.SaveSecretAsync("profile_name", userEl.GetString() ?? "Unknown User");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error parsing id_token: {ex}");
        }
    }

    private async Task<string> ExchangeCodeForTokenAsync(string code, string codeVerifier)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
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

            var response = await _httpClient.SendAsync(request, cts.Token);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"[AuthService] Token response received, length: {json.Length}");
                try 
                {
                    var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("refresh_token", out var refreshElement))
                    {
                        await _secretStorage.SaveSecretAsync("refresh_token", refreshElement.GetString() ?? "");
                    }
                    
                    if (doc.RootElement.TryGetProperty("id_token", out var tokenElement))
                    {
                        return tokenElement.GetString() ?? "";
                    }
                    Debug.WriteLine("Token response did not contain id_token");
                } 
                catch (Exception e) 
                {
                    Debug.WriteLine($"JSON Parse Error. Exception: {e.Message}");
                }
            }
            else 
            {
                var errJson = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"Token request failed with {response.StatusCode}: {errJson}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Exception during token exchange: {ex}");
        }

        return string.Empty;
    }

    private async Task<string> ExchangeForServerJwtAsync(string idToken)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var request = new HttpRequestMessage(HttpMethod.Post, "https://tunnel.darkblue.tech/api/v1/auth/exchange");
            request.Content = new StringContent(JsonSerializer.Serialize(new { idToken = idToken }), Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request, cts.Token);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("access_token", out var tokenElement))
                {
                    return tokenElement.GetString() ?? "";
                }
                Debug.WriteLine("Server exchange response did not contain access_token");
            }
            else
            {
                var errJson = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"Server exchange failed with {response.StatusCode}: {errJson}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Exception during server exchange: {ex}");
        }
        return string.Empty; // Fail if exchange failed
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
