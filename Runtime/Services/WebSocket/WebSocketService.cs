using System;
using System.Threading.Tasks;
using UnityEngine;
using NativeWebSocket;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Runtime.CompilerServices;

namespace UG.Services.WebSocket
{
    public class WebSocketService : IDisposable
    {
        private NativeWebSocket.WebSocket _webSocket;
        private readonly string _host;
        private string _bearerToken;
        private bool _isConnected = false;
        private readonly Queue<string> _messageQueue = new Queue<string>();
        private readonly object _queueLock = new object();
        private TaskCompletionSource<bool> _connectionTcs;
        private CancellationTokenSource _dispatcherCts;
        private Dictionary<string, string> _headers = new Dictionary<string, string>();
        private bool _disposed = false;
        
        // Event handler references to prevent memory leaks
        private NativeWebSocket.WebSocketOpenEventHandler _onOpenHandler;
        private NativeWebSocket.WebSocketErrorEventHandler _onErrorHandler;
        private NativeWebSocket.WebSocketCloseEventHandler _onCloseHandler;
        private NativeWebSocket.WebSocketMessageEventHandler _onMessageHandler;
        
        public WebSocketState State => _webSocket?.State ?? WebSocketState.Closed;

        public WebSocketService(string host, string bearerToken)
        {
            // Replace the protocol at the beginning of the string only.
            if (host.StartsWith("https://"))
            {
                _host = "wss://" + host.Substring("https://".Length);
            }
            else if (host.StartsWith("http://"))
            {
                _host = "ws://" + host.Substring("http://".Length);
            }
            else
            {
                _host = host;
            }
            _bearerToken = bearerToken;
        }

        public void SetBearerToken(string bearerToken)
        {
            _bearerToken = bearerToken;
        }

        public async Task Connect(string endPointUrl)
        {
            ThrowIfDisposed();
            
            UGLog.Log($"[WS] Connecting to {_host}/{endPointUrl}");
            string fullUrl = $"{_host}/{endPointUrl}";
            var headers = new Dictionary<string, string>
            {
                { "Authorization", $"Bearer {_bearerToken}" }
            };
            // Add custom headers
            foreach (var header in _headers)
            {
                headers[header.Key] = header.Value;
            }

            if ((_webSocket != null && _webSocket.State == WebSocketState.Open) || _isConnected)
            {
                UGLog.LogError("[WS] Still open!");
                await Close(); // Close existing connection first
            }

            // Clean up previous resources
            CleanupPreviousConnection();

            _webSocket = new NativeWebSocket.WebSocket(fullUrl, headers);
            _connectionTcs = new TaskCompletionSource<bool>();
            _dispatcherCts = new CancellationTokenSource();

            var connectionStartTime = DateTime.UtcNow;

            // Store event handler references to prevent memory leaks
            _onOpenHandler = () =>
            {
                var connectionDuration = DateTime.UtcNow - connectionStartTime;
                UGLog.Log($"[WS] Time Connected in {connectionDuration.TotalMilliseconds:F0}ms");
                _isConnected = true;
                _connectionTcs.TrySetResult(true);

                // Start message dispatcher on background thread
#if !UNITY_WEBGL || UNITY_EDITOR
                StartMessageDispatcher();
#endif
            };

            _onErrorHandler = (e) =>
            {
                var connectionDuration = DateTime.UtcNow - connectionStartTime;
                UGLog.LogError($"[WS] WebSocket error after {connectionDuration.TotalMilliseconds:F0}ms: {e} url: {fullUrl}");
                _connectionTcs.TrySetException(new Exception($"WebSocket connection error: {e}"));
                _isConnected = false;
            };

            _onCloseHandler = async (e) =>
            {
                UGLog.LogError($"[WS] WebSocket connection closed: {e} code: {(int)e}");
                // If we cancel dispatcher too quick, we "miss" the last message, e.g. error message on disconnect
                if (_messageQueue.Count > 0)
                {
                    UGLog.Log($"[WS] Waiting for leftover messages on close: {_messageQueue.Count}");
                    var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5)); // 5 second timeout
                    try
                    {
                        while (_messageQueue.Count > 0 && !timeoutCts.Token.IsCancellationRequested)
                        {
                            await Task.Delay(10, timeoutCts.Token);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        UGLog.LogWarning("[WS] Timeout waiting for leftover messages");
                    }
                }

                _dispatcherCts?.Cancel();
                _isConnected = false;
            };

            _onMessageHandler = (bytes) =>
            {
                string message = Encoding.UTF8.GetString(bytes);
                UGLog.Log($"[WS] <-- Received message: {message}");

                lock (_queueLock)
                {
                    _messageQueue.Enqueue(message);
                }
            };

            // Subscribe to events
            _webSocket.OnOpen += _onOpenHandler;
            _webSocket.OnError += _onErrorHandler;
            _webSocket.OnClose += _onCloseHandler;
            _webSocket.OnMessage += _onMessageHandler;

            _ = _webSocket.Connect();
            await _connectionTcs.Task;
        }

        private void CleanupPreviousConnection()
        {
            if (_webSocket != null)
            {
                // Unsubscribe from events to prevent memory leaks
                if (_onOpenHandler != null) _webSocket.OnOpen -= _onOpenHandler;
                if (_onErrorHandler != null) _webSocket.OnError -= _onErrorHandler;
                if (_onCloseHandler != null) _webSocket.OnClose -= _onCloseHandler;
                if (_onMessageHandler != null) _webSocket.OnMessage -= _onMessageHandler;
                
                _webSocket = null;
            }
            
            _dispatcherCts?.Cancel();
            _dispatcherCts?.Dispose();
            _dispatcherCts = null;
            
            _connectionTcs = null;
            _isConnected = false;
        }

        public async Task<(Dictionary<string, string> headers, IAsyncEnumerable<string> stream)>
            PostStreamingRequestStreamedAsyncV2(
                IAsyncEnumerable<string> requestStream,
                CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            
            if (!_isConnected)
            {
                throw new InvalidOperationException("[WebSocket] WebSocket is not connected");
            }

            await Task.Delay(5);

            // Start sending streaming request messages asynchronously
            _ = Task.Run(async () =>
            {
                try
                {
                    await foreach (var requestMessage in requestStream.WithCancellation(cancellationToken))
                    {
                        if (_webSocket == null || _webSocket.State != WebSocketState.Open || !_isConnected)
                        {
                            UGLog.LogError("[WebSocket] WebSocket is already closed");
                            break;
                        }
                        UGLog.Log("[WS] --> Sending message: " + requestMessage + " ws state: " + _webSocket.State);
                        await _webSocket.SendText(requestMessage);
                    }
                }
                catch (Exception ex)
                {
                    UGLog.LogError($"[WebSocket] Error sending streaming request: {ex.Message}");
                }
            }, cancellationToken);

            var headers = new Dictionary<string, string>(); //TODO get actual headers
            return (headers, ReceiveStreamingResponse(cancellationToken));
        }

        public async Task SendMessageAsync(string message)
        {
            ThrowIfDisposed();
            
            if (!_isConnected || _webSocket?.State != WebSocketState.Open)
            {
                throw new InvalidOperationException("[WebSocket] WebSocket is not connected");
            }

            UGLog.Log("[WebSocket] --> Sending message: " + message + " ws state: " + _webSocket.State);

            await _webSocket.SendText(message);
        }

#if !UNITY_WEBGL || UNITY_EDITOR
        private void StartMessageDispatcher()
        {
            // Run the message dispatcher on a background thread
            Task.Run(async () =>
            {
                try
                {
                    while (!_dispatcherCts.Token.IsCancellationRequested && _isConnected)
                    {
                        _webSocket.DispatchMessageQueue();
                        await Task.Delay(10, _dispatcherCts.Token);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancellation is requested
                    UGLog.Log("[WebSocket] Dispatcher:Message dispatcher cancelled");
                }
                catch (Exception ex)
                {
                    UGLog.LogError($"[WebSocket] Dispatcher: Error in WebSocket message dispatcher: {ex.Message}");
                }
            }, _dispatcherCts.Token);
        }
#endif

        public async IAsyncEnumerable<string> ReceiveStreamingResponse([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            
            while (!cancellationToken.IsCancellationRequested && _isConnected)
            {
                string message = null;

                // Thread-safe access to the message queue
                lock (_queueLock)
                {
                    if (_messageQueue.Count > 0)
                    {
                        message = _messageQueue.Dequeue();
                    }
                }

                if (message != null)
                {
                    UGLog.Log("[WS] <-- Received message: " + message);
                    yield return message;
                }
                else
                {
                    // Wait a bit before checking again
                    await Task.Delay(10, cancellationToken);
                }
            }
        }

        public async Task Close()
        {
            if (_disposed || !_isConnected) return;

            if (_webSocket != null)
            {
                var closeStartTime = DateTime.UtcNow;
                try
                {
                    await _webSocket.Close();
                    _dispatcherCts?.Cancel();
                    _messageQueue.Clear();
                    _isConnected = false;
                    var closeDuration = DateTime.UtcNow - closeStartTime;
                    UGLog.Log($"[WS] Time Closed in {closeDuration.TotalMilliseconds:F0}ms");
                }
                catch (Exception ex)
                {
                    var closeDuration = DateTime.UtcNow - closeStartTime;
                    UGLog.LogError($"[WS] Error closing WebSocket after {closeDuration.TotalMilliseconds:F0}ms: {ex.Message}");
                    // throw; // don't throw errors on 1006 code on manual close
                }
            }

            // Wait for _isConnected flag with timeout to prevent infinite waiting
            var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10)); // 10 second timeout
            try
            {
                while ((_webSocket?.State == WebSocketState.Open || _isConnected) && !timeoutCts.Token.IsCancellationRequested)
                {
                    await Task.Delay(10, timeoutCts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                UGLog.LogWarning("[WS] Timeout waiting for WebSocket to close completely");
            }
            
            UGLog.Log("[WS] Closed");
        }

        public void SetHeader(string key, string value)
        {
            ThrowIfDisposed();
            _headers[key] = value;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(WebSocketService));
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                try
                {
                    // Close connection if still open
                    if (_isConnected)
                    {
                        _ = Close(); // Fire and forget, but log any errors
                    }
                    
                    // Clean up resources
                    CleanupPreviousConnection();
                    
                    // Clear message queue
                    lock (_queueLock)
                    {
                        _messageQueue.Clear();
                    }
                    
                    // Clear headers
                    _headers.Clear();
                }
                catch (Exception ex)
                {
                    UGLog.LogError($"[WS] Error during disposal: {ex.Message}");
                }
                finally
                {
                    _disposed = true;
                }
            }
        }

        ~WebSocketService()
        {
            Dispose(false);
        }
    }
}