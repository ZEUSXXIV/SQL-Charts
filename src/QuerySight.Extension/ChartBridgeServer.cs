using System;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace QuerySight.Extension
{
    public class ChartBridgeServer
    {
        private HttpListener _listener;
        private CancellationTokenSource _cts;
        private readonly int _port;
        
        public event Action<string> ChartDataReceived;

        public ChartBridgeServer(int port = 8080)
        {
            _port = port;
        }

        public void Start()
        {
            _cts = new CancellationTokenSource();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{_port}/chartbridge/");
            _listener.Start();
            
            // Spin off background task to listen for connections
            Task.Run(() => ListenAsync(_cts.Token));
        }

        public void Stop()
        {
            _cts?.Cancel();
            try
            {
                _listener?.Stop();
                _listener?.Close();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error stopping listener: {ex.Message}");
            }
        }

        private async Task ListenAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _listener.IsListening)
            {
                try
                {
                    HttpListenerContext context = await _listener.GetContextAsync();
                    if (context.Request.IsWebSocketRequest)
                    {
                        // Offload WebSocket connection handling to avoid blocking further requests
                        _ = Task.Run(() => HandleWebSocketRequestAsync(context, token), token);
                    }
                    else if (context.Request.HttpMethod == "POST")
                    {
                        // Handle HTTP POST request as a fallback/additional intake
                        _ = Task.Run(() => HandlePostRequestAsync(context), token);
                    }
                    else
                    {
                        context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                        context.Response.ContentType = "application/json";
                        using (var writer = new StreamWriter(context.Response.OutputStream))
                        {
                            await writer.WriteAsync("{\"status\":\"error\",\"message\":\"Please use a WebSocket connection or HTTP POST to /chartbridge/ to push chart data.\"}");
                        }
                        context.Response.Close();
                    }
                }
                catch (HttpListenerException)
                {
                    if (token.IsCancellationRequested) break;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error in HTTP listener: {ex.Message}");
                }
            }
        }

        private async Task HandlePostRequestAsync(HttpListenerContext context)
        {
            try
            {
                string payload;
                using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding))
                {
                    payload = await reader.ReadToEndAsync();
                }

                if (!string.IsNullOrEmpty(payload))
                {
                    // Raise event on background thread (UI thread marshalling happens in subscriber)
                    ChartDataReceived?.Invoke(payload);
                    
                    context.Response.StatusCode = (int)HttpStatusCode.OK;
                    context.Response.ContentType = "application/json";
                    using (var writer = new StreamWriter(context.Response.OutputStream))
                    {
                        await writer.WriteAsync("{\"status\":\"success\",\"message\":\"Chart data accepted\"}");
                    }
                }
                else
                {
                    context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                }
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                context.Response.ContentType = "application/json";
                using (var writer = new StreamWriter(context.Response.OutputStream))
                {
                    await writer.WriteAsync($"{{\"status\":\"error\",\"message\":\"{ex.Message}\"}}");
                }
            }
            finally
            {
                context.Response.Close();
            }
        }

        private async Task HandleWebSocketRequestAsync(HttpListenerContext context, CancellationToken token)
        {
            WebSocket webSocket = null;
            try
            {
                HttpListenerWebSocketContext wsContext = await context.AcceptWebSocketAsync(subProtocol: null);
                webSocket = wsContext.WebSocket;

                byte[] buffer = new byte[1024 * 32]; // 32KB buffer size
                while (webSocket.State == WebSocketState.Open && !token.IsCancellationRequested)
                {
                    WebSocketReceiveResult result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing connection", token);
                        break;
                    }
                    else if (result.MessageType == WebSocketMessageType.Text)
                    {
                        string messageChunk = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        StringBuilder sb = new StringBuilder(messageChunk);
                        
                        // Loop until the entire logical frame is read
                        while (!result.EndOfMessage)
                        {
                            result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                            sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                        }
                        
                        string fullPayload = sb.ToString();
                        
                        // Fire event
                        ChartDataReceived?.Invoke(fullPayload);
                        
                        // Send simple acknowledgement back to client
                        byte[] ack = Encoding.UTF8.GetBytes("{\"status\":\"success\",\"message\":\"Render command received\"}");
                        await webSocket.SendAsync(new ArraySegment<byte>(ack), WebSocketMessageType.Text, true, token);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WebSocket error: {ex.Message}");
            }
            finally
            {
                webSocket?.Dispose();
            }
        }
    }
}
