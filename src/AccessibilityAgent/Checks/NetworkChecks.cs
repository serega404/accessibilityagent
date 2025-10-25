using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace AccessibilityAgent.Checks;

/// <summary>
/// Набор сетевых проверок (ICMP, DNS, TCP, UDP, HTTP), возвращающих унифицированный <see cref="CheckResult"/> с метаданными и временем выполнения.
/// </summary>
internal static class NetworkChecks
{
    /// <summary>
    /// Выполняет ICMP-пинг узла с указанным тайм-аутом.
    /// </summary>
    /// <param name="host">Доменное имя или IP-адрес.</param>
    /// <param name="timeoutMs">Тайм-аут операции в миллисекундах.</param>
    /// <returns>Результат с признаком успеха, сообщением и метаданными (статус, адрес, время RTT).</returns>
    public static async Task<CheckResult> PingAsync(string host, int timeoutMs)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, timeoutMs);
            stopwatch.Stop();

            if (reply.Status == IPStatus.Success)
            {
                return new CheckResult
                {
                    Check = $"ping:{host}",
                    Success = true,
                    Message = reply.RoundtripTime >= 0
                        ? $"Reply from {reply.Address} in {reply.RoundtripTime} ms"
                        : $"Reply from {reply.Address} (no roundtrip time reported)",
                    DurationMs = stopwatch.Elapsed.TotalMilliseconds,
                    Data = MetadataBuilder.FromPairs(
                        ("ipStatus", reply.Status.ToString()),
                        ("address", reply.Address?.ToString()),
                        ("roundtripTimeMs", reply.RoundtripTime >= 0 ? reply.RoundtripTime.ToString(CultureInfo.InvariantCulture) : null)
                    )
                };
            }

            return new CheckResult
            {
                Check = $"ping:{host}",
                Success = false,
                Message = $"Ping failed with status {reply.Status}",
                DurationMs = stopwatch.Elapsed.TotalMilliseconds,
                Data = MetadataBuilder.FromPairs(
                    ("ipStatus", reply.Status.ToString()),
                    ("address", reply.Address?.ToString())
                )
            };
        }
        catch (PingException ex)
        {
            stopwatch.Stop();
            return new CheckResult
            {
                Check = $"ping:{host}",
                Success = false,
                Message = $"Ping exception: {ex.InnerException?.Message ?? ex.Message}",
                DurationMs = stopwatch.Elapsed.TotalMilliseconds
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new CheckResult
            {
                Check = $"ping:{host}",
                Success = false,
                Message = $"Ping error: {ex.Message}",
                DurationMs = stopwatch.Elapsed.TotalMilliseconds
            };
        }
    }

    /// <summary>
    /// Выполняет DNS‑разрешение имени узла с учётом тайм-аута.
    /// </summary>
    /// <param name="host">Доменное имя для разрешения.</param>
    /// <param name="timeoutMs">Тайм-аут операции в миллисекундах.</param>
    /// <returns>Результат с перечнем найденных адресов либо описанием ошибки/тайм-аута.</returns>
    public static async Task<CheckResult> DnsAsync(string host, int timeoutMs)
    {
        var stopwatch = Stopwatch.StartNew();
        using var timeoutCts = new CancellationTokenSource(timeoutMs);

        try
        {
            var addressesTask = Dns.GetHostAddressesAsync(host);
            var completedTask = await Task.WhenAny(addressesTask, Task.Delay(Timeout.Infinite, timeoutCts.Token));

            if (completedTask != addressesTask)
            {
                stopwatch.Stop();
                return new CheckResult
                {
                    Check = $"dns:{host}",
                    Success = false,
                    Message = $"DNS resolution timed out after {timeoutMs} ms",
                    DurationMs = stopwatch.Elapsed.TotalMilliseconds
                };
            }

            var addresses = await addressesTask;
            stopwatch.Stop();

            if (addresses.Length == 0)
            {
                return new CheckResult
                {
                    Check = $"dns:{host}",
                    Success = false,
                    Message = "DNS resolution returned no addresses",
                    DurationMs = stopwatch.Elapsed.TotalMilliseconds
                };
            }

            var addressStrings = addresses.Select(a => a.ToString()).ToArray();
            return new CheckResult
            {
                Check = $"dns:{host}",
                Success = true,
                Message = $"Resolved {addressStrings.Length} address(es)",
                DurationMs = stopwatch.Elapsed.TotalMilliseconds,
                Data = MetadataBuilder.FromPairs(("addresses", string.Join(", ", addressStrings)))
            };
        }
        catch (SocketException ex)
        {
            stopwatch.Stop();
            return new CheckResult
            {
                Check = $"dns:{host}",
                Success = false,
                Message = $"DNS error: {ex.Message}",
                DurationMs = stopwatch.Elapsed.TotalMilliseconds,
                Data = MetadataBuilder.FromPairs(("socketError", ex.SocketErrorCode.ToString()))
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new CheckResult
            {
                Check = $"dns:{host}",
                Success = false,
                Message = $"DNS error: {ex.Message}",
                DurationMs = stopwatch.Elapsed.TotalMilliseconds
            };
        }
    }

    /// <summary>
    /// Пытается установить TCP‑соединение с узлом и портом в пределах тайм-аута.
    /// </summary>
    /// <param name="host">Доменное имя или IP-адрес удалённого узла.</param>
    /// <param name="port">Порт удалённого узла.</param>
    /// <param name="timeoutMs">Тайм-аут подключения в миллисекундах.</param>
    /// <returns>Результат с признаком успешного рукопожатия или причиной сбоя/тайм-аута.</returns>
    public static async Task<CheckResult> TcpAsync(string host, int port, int timeoutMs)
    {
        var stopwatch = Stopwatch.StartNew();

        using var client = new TcpClient();
        var connectTask = client.ConnectAsync(host, port);
        var delayTask = Task.Delay(timeoutMs);

        var completed = await Task.WhenAny(connectTask, delayTask);
        stopwatch.Stop();

        if (completed != connectTask)
        {
            return new CheckResult
            {
                Check = $"tcp:{host}:{port}",
                Success = false,
                Message = $"TCP connect timeout after {timeoutMs} ms",
                DurationMs = stopwatch.Elapsed.TotalMilliseconds
            };
        }

        try
        {
            await connectTask; // propagate exceptions
            return new CheckResult
            {
                Check = $"tcp:{host}:{port}",
                Success = true,
                Message = "TCP handshake successful",
                DurationMs = stopwatch.Elapsed.TotalMilliseconds,
                Data = MetadataBuilder.FromPairs(("remoteEndPoint", client.Client.RemoteEndPoint?.ToString()))
            };
        }
        catch (Exception ex)
        {
            return new CheckResult
            {
                Check = $"tcp:{host}:{port}",
                Success = false,
                Message = $"TCP error: {ex.Message}",
                DurationMs = stopwatch.Elapsed.TotalMilliseconds
            };
        }
    }

    /// <summary>
    /// Отправляет UDP‑датаграмму и, при необходимости, ожидает ответ в пределах тайм-аута.
    /// </summary>
    /// <param name="host">Доменное имя или IP-адрес получателя.</param>
    /// <param name="port">Порт получателя.</param>
    /// <param name="payload">Тело датаграммы (UTF‑8). Может быть пустым.</param>
    /// <param name="timeoutMs">Тайм-аут ожидания ответа в миллисекундах.</param>
    /// <param name="expectResponse">Признак ожидания ответа от удалённой стороны.</param>
    /// <returns>Результат с размером отправленных/полученных данных и конечной точкой, либо описанием тайм-аута/ошибки.</returns>
    public static async Task<CheckResult> UdpAsync(string host, int port, string payload, int timeoutMs, bool expectResponse)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var udpClient = new UdpClient();
            udpClient.Connect(host, port);

            var buffer = Encoding.UTF8.GetBytes(payload ?? string.Empty);
            await udpClient.SendAsync(buffer, buffer.Length);

            if (!expectResponse)
            {
                stopwatch.Stop();
                return new CheckResult
                {
                    Check = $"udp:{host}:{port}",
                    Success = true,
                    Message = buffer.Length == 0 ? "UDP datagram sent" : $"UDP datagram ({buffer.Length} bytes) sent",
                    DurationMs = stopwatch.Elapsed.TotalMilliseconds,
                    Data = MetadataBuilder.FromPairs(("bytesSent", buffer.Length.ToString(CultureInfo.InvariantCulture)))
                };
            }

            var receiveTask = udpClient.ReceiveAsync();
            var delayTask = Task.Delay(timeoutMs);
            var completedTask = await Task.WhenAny(receiveTask, delayTask);

            stopwatch.Stop();

            if (completedTask != receiveTask)
            {
                return new CheckResult
                {
                    Check = $"udp:{host}:{port}",
                    Success = false,
                    Message = $"No UDP response within {timeoutMs} ms",
                    DurationMs = stopwatch.Elapsed.TotalMilliseconds,
                    Data = MetadataBuilder.FromPairs(("bytesSent", buffer.Length.ToString(CultureInfo.InvariantCulture)))
                };
            }

            var response = await receiveTask;
            return new CheckResult
            {
                Check = $"udp:{host}:{port}",
                Success = true,
                Message = $"Received UDP response ({response.Buffer.Length} bytes)",
                DurationMs = stopwatch.Elapsed.TotalMilliseconds,
                Data = MetadataBuilder.FromPairs(
                    ("bytesSent", buffer.Length.ToString(CultureInfo.InvariantCulture)),
                    ("bytesReceived", response.Buffer.Length.ToString(CultureInfo.InvariantCulture)),
                    ("remoteEndPoint", response.RemoteEndPoint.ToString())
                )
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new CheckResult
            {
                Check = $"udp:{host}:{port}",
                Success = false,
                Message = $"UDP error: {ex.Message}",
                DurationMs = stopwatch.Elapsed.TotalMilliseconds
            };
        }
    }

    /// <summary>
    /// Выполняет HTTP‑запрос с заданным методом, телом, типом содержимого и произвольными заголовками.
    /// </summary>
    /// <param name="uri">URI ресурса.</param>
    /// <param name="method">HTTP‑метод (GET, POST и т. п.). Регистр не важен.</param>
    /// <param name="timeoutMs">Тайм-аут запроса в миллисекундах.</param>
    /// <param name="body">Тело запроса (опционально).</param>
    /// <param name="contentType">Тип содержимого тела запроса (например, application/json).</param>
    /// <param name="headerValues">Список заголовков в формате key=value или key:value.</param>
    /// <returns>Результат с кодом статуса, причиной, заголовками и флагом успеха.</returns>
    public static async Task<CheckResult> HttpAsync(Uri uri, string method, int timeoutMs, string? body, string contentType, IEnumerable<string> headerValues)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMilliseconds(timeoutMs)
            };

            using var request = new HttpRequestMessage(new HttpMethod(method.ToUpperInvariant()), uri);
            var contentHeaders = new List<KeyValuePair<string, string>>();

            foreach (var header in headerValues)
            {
                if (string.IsNullOrWhiteSpace(header))
                {
                    continue;
                }

                var separatorIndex = header.IndexOf('=');
                if (separatorIndex < 0)
                {
                    separatorIndex = header.IndexOf(':');
                }

                if (separatorIndex <= 0)
                {
                    throw new ArgumentException($"Invalid header format '{header}'. Use key=value.");
                }

                var key = header[..separatorIndex].Trim();
                var value = header[(separatorIndex + 1)..].Trim();
                if (!request.Headers.TryAddWithoutValidation(key, value))
                {
                    contentHeaders.Add(new KeyValuePair<string, string>(key, value));
                }
            }

            if (body is not null)
            {
                request.Content = new StringContent(body, Encoding.UTF8, contentType);
            }
            else if (contentHeaders.Count > 0)
            {
                request.Content = new ByteArrayContent(Array.Empty<byte>());
            }

            if (request.Content is not null && contentHeaders.Count > 0)
            {
                foreach (var headerPair in contentHeaders)
                {
                    request.Content.Headers.Remove(headerPair.Key);
                    request.Content.Headers.TryAddWithoutValidation(headerPair.Key, headerPair.Value);
                }

                if (body is null && !request.Content.Headers.Contains("Content-Type"))
                {
                    request.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
                }
            }

            using var response = await httpClient.SendAsync(request);
            stopwatch.Stop();

            var headerSummary = string.Join(
                "; ",
                response.Headers.Concat(response.Content.Headers)
                    .Select(h => $"{h.Key}: {string.Join(", ", h.Value)}")
            );

            return new CheckResult
            {
                Check = $"http:{uri}",
                Success = response.IsSuccessStatusCode,
                Message = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}",
                DurationMs = stopwatch.Elapsed.TotalMilliseconds,
                Data = MetadataBuilder.FromPairs(
                    ("statusCode", ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture)),
                    ("reasonPhrase", response.ReasonPhrase),
                    ("headers", string.IsNullOrWhiteSpace(headerSummary) ? null : headerSummary)
                )
            };
        }
        catch (TaskCanceledException ex) when (!ex.CancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return new CheckResult
            {
                Check = $"http:{uri}",
                Success = false,
                Message = $"HTTP request timed out after {timeoutMs} ms",
                DurationMs = stopwatch.Elapsed.TotalMilliseconds
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new CheckResult
            {
                Check = $"http:{uri}",
                Success = false,
                Message = $"HTTP error: {ex.Message}",
                DurationMs = stopwatch.Elapsed.TotalMilliseconds
            };
        }
    }
}
