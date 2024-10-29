using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Thor.Abstractions;
using Thor.Abstractions.Chats;
using Thor.Abstractions.Chats.Dtos;
using Thor.Abstractions.Exceptions;
using Thor.Abstractions.Extensions;

namespace Thor.OpenAI.Chats;

public sealed class OpenAIChatCompletionsService : IThorChatCompletionsService
{
    public OpenAIChatCompletionsService()
    {
        InitClient();
    }
    private List<HttpClient> _keyValuePairs = new List<HttpClient>();
    private void InitClient()
    {
        if (_keyValuePairs.Count > 0)
        {
            return;
        }

        // 构建多个HttpClient
        for (int i = 0; i < 5; i++)
        {
            _keyValuePairs.Add(new HttpClient(new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(10),
                EnableMultipleHttp2Connections = true,
                ConnectTimeout = TimeSpan.FromMinutes(10),
                KeepAlivePingTimeout = TimeSpan.FromMinutes(10),
                ResponseDrainTimeout = TimeSpan.FromMinutes(10),
            }));
        }
    }

    public HttpClient Client()
    {
        return _keyValuePairs.OrderBy(x => Guid.NewGuid()).First();
    }

    public async Task<ThorChatCompletionsResponse> ChatCompletionsAsync(ThorChatCompletionsRequest chatCompletionCreate,
        ThorPlatformOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await Client().PostJsonAsync(options?.Address.TrimEnd('/') + "/v1/chat/completions",
            chatCompletionCreate, options.ApiKey);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new UnauthorizedAccessException();
        }

        if (response.StatusCode == HttpStatusCode.PaymentRequired)
        {
            throw new PaymentRequiredException();
        }

        response.EnsureSuccessStatusCode();

        var result =
            await response.Content.ReadFromJsonAsync<ThorChatCompletionsResponse>(
                cancellationToken: cancellationToken).ConfigureAwait(false);

        return result;
    }

    public async IAsyncEnumerable<ThorChatCompletionsResponse> StreamChatCompletionsAsync(
        ThorChatCompletionsRequest chatCompletionCreate, ThorPlatformOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await Client().HttpRequestRaw(options?.Address.TrimEnd('/') + "/v1/chat/completions",
            chatCompletionCreate, options.ApiKey);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new UnauthorizedAccessException();
        }

        if (response.StatusCode == HttpStatusCode.PaymentRequired)
        {
            throw new PaymentRequiredException();
        }

        response.EnsureSuccessStatusCode();

        using var stream = new StreamReader(await response.Content.ReadAsStreamAsync(cancellationToken));

        using StreamReader reader = new(await response.Content.ReadAsStreamAsync(cancellationToken));
        string? line = string.Empty;
        while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
        {
            line += Environment.NewLine;

            if (line.StartsWith('{'))
            {
                // 如果是json数据则直接返回
                yield return JsonSerializer.Deserialize<ThorChatCompletionsResponse>(line,
                    ThorJsonSerializer.DefaultOptions);

                break;
            }

            if (line.StartsWith("data:"))
                line = line["data:".Length..];

            line = line.Trim();

            if (line == "[DONE]")
            {
                break;
            }

            if (line.StartsWith(":"))
            {
                continue;
            }


            if (string.IsNullOrWhiteSpace(line)) continue;

            var result = JsonSerializer.Deserialize<ThorChatCompletionsResponse>(line,
                ThorJsonSerializer.DefaultOptions);
            yield return result;
        }
    }
}