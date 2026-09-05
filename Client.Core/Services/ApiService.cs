using Client.Core.Models;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace Client.Core.Services;

/// <summary>
/// Service for interacting with the DarkTunnel REST API (fetching tunnels, edge nodes, etc.).
/// </summary>
public class ApiService
{
    private readonly HttpClient _httpClient;
    private readonly AuthService _authService;
    private readonly string _baseUrl;

    public ApiService(AuthService authService)
    {
        _authService = authService;
        _baseUrl = Environment.GetEnvironmentVariable("TUNNEL_API_URL") ?? "https://tunnel.darkblue.tech/api";
        _httpClient = new HttpClient();
    }

    private async Task<HttpResponseMessage> SendWithAuthRetryAsync(Func<HttpRequestMessage> requestFactory)
    {
        var token = await _authService.GetTokenAsync();
        if (string.IsNullOrEmpty(token))
        {
            throw new UnauthorizedAccessException("No token available");
        }

        var request1 = requestFactory();
        request1.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _httpClient.SendAsync(request1);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            // Token expired on server; attempt refresh and retry
            var newToken = await _authService.RefreshTokenAsync();
            if (string.IsNullOrEmpty(newToken))
            {
                throw new UnauthorizedAccessException("Session expired");
            }

            var request2 = requestFactory();
            request2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", newToken);
            return await _httpClient.SendAsync(request2);
        }

        return response;
    }

    public async Task<List<TunnelModel>> GetTunnelsAsync()
    {
        try
        {
            var response = await SendWithAuthRetryAsync(() => new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/tunnels"));
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                throw new UnauthorizedAccessException("Session expired");
            }
            response.EnsureSuccessStatusCode();
            var tunnels = await response.Content.ReadFromJsonAsync<List<TunnelModel>>();
            return tunnels ?? new List<TunnelModel>();
        }
        catch (UnauthorizedAccessException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to fetch tunnels: {ex.Message}");
            return new List<TunnelModel>();
        }
    }

    public async Task<EdgeNodeResponse?> GetPreferredEdgeNodeAsync()
    {
        try
        {
            var response = await SendWithAuthRetryAsync(() => new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/v1/edge-nodes/preferred"));
            response.EnsureSuccessStatusCode();
            
            var result = await response.Content.ReadFromJsonAsync<EdgeNodeResponse>();
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to fetch preferred edge node: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Represents the response model for a preferred edge node.
    /// </summary>
    public class EdgeNodeResponse
    {
        public string Url { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;
        public string Hostname { get; set; } = string.Empty;
    }
}
