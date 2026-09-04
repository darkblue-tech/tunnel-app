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

    public async Task<List<TunnelModel>> GetTunnelsAsync()
    {
        var token = await _authService.GetTokenAsync();
        if (string.IsNullOrEmpty(token)) return new List<TunnelModel>();

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/tunnels");
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                throw new UnauthorizedAccessException("Token is dead");
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
        var token = await _authService.GetTokenAsync();
        if (string.IsNullOrEmpty(token)) return null;

        _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/v1/edge-nodes/preferred");
            response.EnsureSuccessStatusCode();
            
            var result = await System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsync<EdgeNodeResponse>(response.Content);
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
