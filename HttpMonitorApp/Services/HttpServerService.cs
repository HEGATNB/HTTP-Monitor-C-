using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HttpMonitor.Models;

namespace HttpMonitor.Services
{
    public class HttpServerService : IDisposable
    {
        private HttpListener? _listener;
        private CancellationTokenSource? _cts;
        private readonly ConcurrentDictionary<int, ServerMessage> _messages;
        private int _nextMessageId = 1;
        private readonly ConcurrentBag<HttpRequestLog> _requestLogs;
        private readonly object _logLock = new object();
        private readonly string _logFilePath = "server_logs.txt";
        private readonly ConcurrentQueue<StatisticsViewModel.LoadPoint> _loadPoints;
        private DateTime _serverStartTime;

        public event Action<HttpRequestLog>? RequestProcessed;
        public event Action? StatisticsUpdated;

        public HttpServerService()
        {
            _messages = new ConcurrentDictionary<int, ServerMessage>();
            _requestLogs = new ConcurrentBag<HttpRequestLog>();
            _loadPoints = new ConcurrentQueue<StatisticsViewModel.LoadPoint>();
        }

        public async Task StartAsync(int port)
        {
            try
            {
                if (_listener != null && _listener.IsListening)
                    return;

                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://localhost:{port}/");
                _listener.Prefixes.Add($"http://127.0.0.1:{port}/");

                try
                {
                    _listener.Prefixes.Add($"http://+:{port}/");
                }
                catch
                {
                }

                _listener.Start();
                _cts = new CancellationTokenSource();
                _serverStartTime = DateTime.Now;
                _ = Task.Run(async () =>
                {
                    while (_cts != null && !_cts.IsCancellationRequested)
                    {
                        try
                        {
                            var context = await _listener.GetContextAsync().ConfigureAwait(false);
                            _ = Task.Run(() => ProcessRequestAsync(context));
                        }
                        catch (HttpListenerException ex)
                        {
                            if (ex.ErrorCode == 995)
                                break;
                            LogToFile($"HttpListener error: {ex.Message}");
                        }
                        catch (ObjectDisposedException)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            LogToFile($"Server error: {ex.Message}");
                        }
                    }
                }, _cts.Token);

                _ = Task.Run(MonitorLoadAsync);
            }
            catch (HttpListenerException ex)
            {
                if (ex.ErrorCode == 5)
                {
                    throw new Exception($"Нет доступа к порту {port}. Запустите от имени администратора или выполните:\n" +
                                      $"netsh http add urlacl url=http://localhost:{port}/ user=\"Все\"");
                }
                throw;
            }
            catch (Exception ex)
            {
                LogToFile($"Failed to start server: {ex.Message}");
                throw;
            }
        }

        public void Stop()
        {
            try
            {
                _cts?.Cancel();

                if (_listener != null)
                {
                    try
                    {
                        if (_listener.IsListening)
                        {
                            _listener.Stop();
                        }
                    }
                    catch (ObjectDisposedException) { }
                    catch (HttpListenerException) { }
                }
            }
            catch (Exception ex)
            {
                LogToFile($"Error stopping server: {ex.Message}");
            }
        }

        public void Dispose()
        {
            try
            {
                Stop();

                if (_listener != null)
                {
                    try
                    {
                        _listener.Close();
                    }
                    catch { }
                    _listener = null;
                }

                _cts?.Dispose();
                _cts = null;
            }
            catch (Exception ex)
            {
                LogToFile($"Error disposing server: {ex.Message}");
            }
        }

        private async Task ProcessRequestAsync(HttpListenerContext context)
        {
            var stopwatch = Stopwatch.StartNew();
            var log = new HttpRequestLog
            {
                Method = context.Request.HttpMethod,
                Url = context.Request.Url?.ToString() ?? string.Empty,
                Headers = GetHeadersAsString(context.Request.Headers),
                Timestamp = DateTime.Now,
                IsIncoming = true
            };

            try
            {
                switch (context.Request.HttpMethod)
                {
                    case "GET":
                        await HandleGetRequest(context);
                        break;
                    case "POST":
                        await HandlePostRequest(context);
                        break;
                    case "PUT":
                        await HandlePutRequest(context);
                        break;
                    case "DELETE":
                        await HandleDeleteRequest(context);
                        break;
                    default:
                        context.Response.StatusCode = 405;
                        await WriteResponse(context, "Method not allowed");
                        break;
                }

                log.ResponseStatus = context.Response.StatusCode.ToString();
            }
            catch (Exception ex)
            {
                log.ResponseStatus = "500";
                context.Response.StatusCode = 500;
                await WriteResponse(context, $"Internal error: {ex.Message}");
            }
            finally
            {
                stopwatch.Stop();
                log.ProcessingTimeMs = stopwatch.ElapsedMilliseconds;

                lock (_logLock)
                {
                    _requestLogs.Add(log);
                    LogToFile($"[{log.Timestamp:yyyy-MM-dd HH:mm:ss}] {log.Method} {log.Url} - {log.ResponseStatus} ({log.ProcessingTimeMs}ms)");
                }

                try
                {
                    context.Response.Close();
                }
                catch { }

                RequestProcessed?.Invoke(log);
                StatisticsUpdated?.Invoke();
            }
        }

        private async Task HandleGetRequest(HttpListenerContext context)
        {
            var path = context.Request.Url?.LocalPath.TrimStart('/') ?? string.Empty;

            if (path == "stats")
            {
                var stats = new
                {
                    uptime = (DateTime.Now - _serverStartTime).ToString(),
                    totalRequests = _requestLogs.Count,
                    getCount = _requestLogs.Count(x => x.Method == "GET"),
                    postCount = _requestLogs.Count(x => x.Method == "POST"),
                    putCount = _requestLogs.Count(x => x.Method == "PUT"),
                    deleteCount = _requestLogs.Count(x => x.Method == "DELETE"),
                    avgProcessingTime = _requestLogs.Count > 0 ? _requestLogs.Average(x => x.ProcessingTimeMs) : 0,
                    messages = _messages.Values.OrderByDescending(x => x.ReceivedAt).Take(10).Select(m => new { m.Id, m.Message, m.ReceivedAt })
                };

                await WriteJsonResponse(context, stats);
            }
            else if (string.IsNullOrEmpty(path))
            {
                var response = new
                {
                    message = "Server is running",
                    time = DateTime.Now,
                    uptime = (DateTime.Now - _serverStartTime).ToString(),
                    requestsProcessed = _requestLogs.Count
                };
                await WriteJsonResponse(context, response);
            }
            else if (int.TryParse(path, out int id))
            {
                if (_messages.TryGetValue(id, out var message))
                {
                    await WriteJsonResponse(context, message);
                }
                else
                {
                    context.Response.StatusCode = 404;
                    await WriteResponse(context, $"Message with id {id} not found");
                }
            }
            else
            {
                context.Response.StatusCode = 400;
                await WriteResponse(context, "Invalid path");
            }
        }

        private async Task HandlePostRequest(HttpListenerContext context)
        {
            using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding ?? Encoding.UTF8);
            var body = await reader.ReadToEndAsync();

            try
            {
                var jsonDoc = JsonDocument.Parse(body);
                if (jsonDoc.RootElement.TryGetProperty("message", out var messageElement))
                {
                    var message = new ServerMessage
                    {
                        Id = _nextMessageId++,
                        Message = messageElement.GetString() ?? string.Empty,
                        ReceivedAt = DateTime.Now
                    };

                    _messages.TryAdd(message.Id, message);

                    var response = new { id = message.Id, received = message.ReceivedAt };
                    await WriteJsonResponse(context, response, HttpStatusCode.Created);
                }
                else
                {
                    context.Response.StatusCode = 400;
                    await WriteResponse(context, "Missing 'message' field");
                }
            }
            catch (JsonException)
            {
                context.Response.StatusCode = 400;
                await WriteResponse(context, "Invalid JSON");
            }
        }

        private async Task HandlePutRequest(HttpListenerContext context)
        {
            var path = context.Request.Url?.LocalPath.TrimStart('/') ?? string.Empty;

            if (!int.TryParse(path, out int id))
            {
                context.Response.StatusCode = 400;
                await WriteResponse(context, "Missing or invalid message id in URL (use /{id})");
                return;
            }

            using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding ?? Encoding.UTF8);
            var body = await reader.ReadToEndAsync();

            try
            {
                var jsonDoc = JsonDocument.Parse(body);
                if (jsonDoc.RootElement.TryGetProperty("message", out var messageElement))
                {
                    var message = new ServerMessage
                    {
                        Id = id,
                        Message = messageElement.GetString() ?? string.Empty,
                        ReceivedAt = DateTime.Now
                    };

                    _messages.AddOrUpdate(id, message, (key, oldValue) => message);

                    var response = new { id = message.Id, updated = message.ReceivedAt };
                    await WriteJsonResponse(context, response, HttpStatusCode.OK);
                }
                else
                {
                    context.Response.StatusCode = 400;
                    await WriteResponse(context, "Missing 'message' field");
                }
            }
            catch (JsonException)
            {
                context.Response.StatusCode = 400;
                await WriteResponse(context, "Invalid JSON");
            }
        }

        private async Task HandleDeleteRequest(HttpListenerContext context)
        {
            var path = context.Request.Url?.LocalPath.TrimStart('/') ?? string.Empty;

            if (int.TryParse(path, out int id))
            {
                if (_messages.TryRemove(id, out _))
                {
                    var response = new { deleted = true, id = id };
                    await WriteJsonResponse(context, response, HttpStatusCode.OK);
                }
                else
                {
                    context.Response.StatusCode = 404;
                    await WriteResponse(context, $"Message with id {id} not found");
                }
            }
            else
            {
                context.Response.StatusCode = 400;
                await WriteResponse(context, "Missing or invalid message id in URL (use /{id})");
            }
        }

        private async Task WriteJsonResponse(HttpListenerContext context, object data, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            context.Response.StatusCode = (int)statusCode;
            context.Response.ContentType = "application/json";
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            var buffer = Encoding.UTF8.GetBytes(json);
            await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }

        private async Task WriteResponse(HttpListenerContext context, string message, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            context.Response.StatusCode = (int)statusCode;
            context.Response.ContentType = "text/plain";
            var buffer = Encoding.UTF8.GetBytes(message);
            await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }

        private string GetHeadersAsString(System.Collections.Specialized.NameValueCollection headers)
        {
            var sb = new StringBuilder();
            foreach (string? key in headers.AllKeys)
            {
                if (key != null)
                    sb.AppendLine($"{key}: {headers[key]}");
            }
            return sb.ToString();
        }

        private void LogToFile(string message)
        {
            try
            {
                File.AppendAllText(_logFilePath, message + Environment.NewLine);
            }
            catch
            {
            }
        }

        private async Task MonitorLoadAsync()
        {
            while (_cts != null && !_cts.IsCancellationRequested)
            {
                await Task.Delay(60000);
                var minuteCount = _requestLogs.Count(x => x.Timestamp > DateTime.Now.AddMinutes(-1));

                _loadPoints.Enqueue(new StatisticsViewModel.LoadPoint
                {
                    Time = DateTime.Now,
                    RequestCount = minuteCount
                });

                while (_loadPoints.Count > 60)
                    _loadPoints.TryDequeue(out _);

                StatisticsUpdated?.Invoke();
            }
        }

        public List<HttpRequestLog> GetLogs(string? filter = null)
        {
            var query = _requestLogs.AsEnumerable();

            if (!string.IsNullOrEmpty(filter))
                query = query.Where(x => x.Method == filter);

            return query.OrderByDescending(x => x.Timestamp).ToList();
        }

        public IEnumerable<StatisticsViewModel.LoadPoint> GetLoadPoints()
        {
            return _loadPoints.ToList();
        }

        public StatisticsViewModel GetStatistics()
        {
            return new StatisticsViewModel
            {
                TotalRequests = _requestLogs.Count,
                GetRequests = _requestLogs.Count(x => x.Method == "GET"),
                PostRequests = _requestLogs.Count(x => x.Method == "POST"),
                PutRequests = _requestLogs.Count(x => x.Method == "PUT"),
                DeleteRequests = _requestLogs.Count(x => x.Method == "DELETE"),
                AverageProcessingTime = _requestLogs.Count > 0 ? _requestLogs.Average(x => x.ProcessingTimeMs) : 0,
                ServerStartTime = _serverStartTime
            };
        }
    }
}