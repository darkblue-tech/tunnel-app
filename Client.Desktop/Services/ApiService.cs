using Client.Desktop.Models;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace Client.Desktop.Services;

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
}
