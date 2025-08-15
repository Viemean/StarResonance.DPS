using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using StarResonance.DPS.Models;

namespace StarResonance.DPS.Services;

/// <summary>
///     提供与后端服务进行API交互的功能。
/// </summary>
public class ApiService : IAsyncDisposable
{
    private string _baseUrl;
    private HttpClient? _httpClient;
    private SocketIOClient.SocketIO? _socket;

    public ApiService(string baseUrl = "http://localhost:8989")
    {
        _baseUrl = baseUrl;
        InitializeClients();
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore();
        GC.SuppressFinalize(this);
    }

    public event Action<ApiResponse>? DataReceived;
    public event Action? OnConnected;
    public event Action? OnDisconnected;

    private void InitializeClients()
    {
        var httpBaseUrl = _baseUrl.Replace("ws://", "http://").Replace("wss://", "https://");
        _httpClient = new HttpClient { BaseAddress = new Uri(httpBaseUrl) };
        _socket = new SocketIOClient.SocketIO(_httpClient.BaseAddress);

        _socket.On("data", response =>
        {
            try
            {
                var data = response.GetValue<ApiResponse>();
                if (data != null) DataReceived?.Invoke(data);
            }
            catch (Exception)
            {
                // 可以在这里添加日志记录
            }
        });

        _socket.OnConnected += (_, _) => OnConnected?.Invoke();
        _socket.OnDisconnected += (_, _) => OnDisconnected?.Invoke();
    }

    public async Task ReinitializeAsync(string baseUrl)
    {
        await DisposeAsyncCore();
        _baseUrl = baseUrl;
        InitializeClients();
    }

    private async Task DisposeAsyncCore()
    {
        if (_socket != null)
        {
            if (_socket.Connected) await _socket.DisconnectAsync();
            _socket.Dispose();
        }

        _httpClient?.Dispose();
    }

    /// <summary>
    ///     异步连接到 WebSocket 服务器。
    ///     如果已经连接，则此操作无效。
    /// </summary>
    public async Task ConnectAsync()
    {
        if (_socket?.Connected == false) await _socket.ConnectAsync();
    }

    public async Task<bool> CheckServiceRunningAsync(CancellationToken ct = default)
    {
        if (_httpClient == null) return false;
        try
        {
            using var response = await _httpClient.GetAsync("/api/pause", ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    ///     从服务器获取初始的完整DPS数据。
    /// </summary>
    /// <returns>包含所有玩家数据的 ApiResponse 对象，如果失败则返回 null。</returns>
    public async Task<ApiResponse?> GetInitialDataAsync()
    {
        if (_httpClient == null) return null;
        try
        {
            return await _httpClient.GetFromJsonAsync<ApiResponse>("/api/data",
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task<SkillApiResponse?> GetSkillDataAsync(long uid, CancellationToken ct = default)
    {
        if (_httpClient == null) return null;
        try
        {
            return await _httpClient.GetFromJsonAsync<SkillApiResponse>($"/api/skill/{uid}",
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task<bool> ResetDataAsync()
    {
        if (_httpClient == null) return false;
        try
        {
            var response = await _httpClient.GetAsync("/api/clear");
            response.EnsureSuccessStatusCode();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<bool> SetPauseStateAsync(bool isPaused)
    {
        if (_httpClient == null) return false;
        try
        {
            var content = new StringContent($"{{\"paused\":{isPaused.ToString().ToLower()}}}", Encoding.UTF8,
                "application/json");
            var response = await _httpClient.PostAsync("/api/pause", content);
            response.EnsureSuccessStatusCode();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<(bool, bool)> GetPauseStateAsync(CancellationToken ct = default)
    {
        if (_httpClient == null) return (false, false);
        try
        {
            var response = await _httpClient.GetAsync("/api/pause", ct);
            if (!response.IsSuccessStatusCode) return (false, false);

            var jsonString = await response.Content.ReadAsStringAsync(ct);
            using var jsonDoc = JsonDocument.Parse(jsonString);
            return jsonDoc.RootElement.TryGetProperty("paused", out var pausedElement)
                ? (true, pausedElement.GetBoolean())
                : (false, false);
        }
        catch
        {
            return (false, false);
        }
    }
}