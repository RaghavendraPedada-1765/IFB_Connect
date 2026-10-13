using IFB.Client.Services;
using System.Net.Http.Headers;

namespace IFB.Client;

/// <summary>
/// DelegatingHandler that attaches the current JWT Bearer token to every
/// outgoing request to the IFB Server REST API.
/// On 401, attempts a silent token refresh before retrying once.
/// </summary>
public sealed class AuthHeaderHandler : DelegatingHandler
{
    private readonly IClientAuthService _auth;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public AuthHeaderHandler(IClientAuthService auth) => _auth = auth;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_auth.AccessToken != null && !_auth.IsAuthenticated) await _auth.TryRefreshAsync();
        if (request.Content != null) await request.Content.LoadIntoBufferAsync();
        SetToken(request, _auth.AccessToken);

        var response = await base.SendAsync(request, cancellationToken);

        // If 401 — try silent token refresh then retry once
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            var refreshed = false;
            await _refreshLock.WaitAsync(cancellationToken);
            try
            {
                // Double-check token hasn't already been refreshed by another thread
                if (_auth.AccessToken != request.Headers.Authorization?.Parameter && _auth.IsAuthenticated)
                    refreshed = true;
                else
                    refreshed = await _auth.TryRefreshAsync(force: true);
            }
            finally { _refreshLock.Release(); }

            if (refreshed)
            {
                response.Dispose();
                // Clone the request (can't reuse a consumed HttpRequestMessage)
                using var retry = CloneRequest(request);
                SetToken(retry, _auth.AccessToken);
                response = await base.SendAsync(retry, cancellationToken);
            }
        }

        return response;
    }

    private static void SetToken(HttpRequestMessage request, string? token)
    {
        if (token != null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private static HttpRequestMessage CloneRequest(HttpRequestMessage original)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri);
        foreach (var header in original.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        if (original.Content != null)
        {
            // Content is buffered before the first send so uploads can be retried.
            clone.Content = original.Content;
        }
        return clone;
    }
}

