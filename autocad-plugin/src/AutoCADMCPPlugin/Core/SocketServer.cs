using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace AutoCADMCPPlugin.Core
{
    /// <summary>
    /// TCP socket server that listens on localhost for incoming MCP connections.
    /// Implements JSON-RPC 2.0 protocol over newline-delimited TCP.
    /// Each client connection is handled on a separate thread.
    /// </summary>
    public class SocketServer
    {
        private TcpListener _listener;
        private Thread _listenerThread;
        private volatile bool _running;
        private readonly ConcurrentDictionary<int, TcpClient> _clients = new ConcurrentDictionary<int, TcpClient>();
        private int _clientIdCounter;

        public int Port { get; }
        public bool IsRunning => _running;
        public int ActiveConnections => _clients.Count;

        public SocketServer(int port)
        {
            Port = port;
        }

        public void Start()
        {
            if (_running) return;

            _listener = new TcpListener(IPAddress.Loopback, Port);
            _listener.Start();
            _running = true;

            _listenerThread = new Thread(ListenForClients)
            {
                IsBackground = true,
                Name = "MCP-SocketListener"
            };
            _listenerThread.Start();
        }

        public void Stop()
        {
            _running = false;

            try { _listener?.Stop(); } catch { }

            foreach (var kvp in _clients)
            {
                try { kvp.Value?.Close(); } catch { }
            }
            _clients.Clear();
        }

        private void ListenForClients()
        {
            while (_running)
            {
                try
                {
                    TcpClient client = _listener.AcceptTcpClient();
                    int clientId = Interlocked.Increment(ref _clientIdCounter);
                    _clients.TryAdd(clientId, client);

                    Thread clientThread = new Thread(() => HandleClient(clientId, client))
                    {
                        IsBackground = true,
                        Name = $"MCP-Client-{clientId}"
                    };
                    clientThread.Start();
                }
                catch (SocketException) when (!_running)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log($"Listener error: {ex.Message}");
                }
            }
        }

        private void HandleClient(int clientId, TcpClient client)
        {
            Log($"Client {clientId} connected");
            try
            {
                NetworkStream stream = client.GetStream();
                byte[] buffer = new byte[1024 * 64];
                StringBuilder sb = new StringBuilder();

                while (_running && client.Connected)
                {
                    int bytesRead;
                    try
                    {
                        bytesRead = stream.Read(buffer, 0, buffer.Length);
                    }
                    catch (IOException)
                    {
                        break;
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }

                    if (bytesRead == 0) break;

                    sb.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));

                    // Process all complete lines (newline-delimited JSON-RPC)
                    string accumulated = sb.ToString();
                    int lastNewline = accumulated.LastIndexOf('\n');
                    if (lastNewline < 0) continue; // No complete line yet

                    // Extract complete lines, keep remainder
                    string complete = accumulated.Substring(0, lastNewline + 1);
                    sb.Clear();
                    if (lastNewline + 1 < accumulated.Length)
                        sb.Append(accumulated.Substring(lastNewline + 1));

                    string[] lines = complete.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

                    foreach (string line in lines)
                    {
                        string trimmed = line.Trim();
                        if (string.IsNullOrEmpty(trimmed)) continue;

                        try
                        {
                            string responseJson = JsonRpcHandler.ProcessRequest(trimmed);

                            if (!string.IsNullOrEmpty(responseJson))
                            {
                                byte[] responseBytes = Encoding.UTF8.GetBytes(responseJson + "\n");
                                stream.Write(responseBytes, 0, responseBytes.Length);
                                stream.Flush();
                            }
                        }
                        catch (Exception ex)
                        {
                            Log($"Client {clientId} request error: {ex}");
                            // Send error response
                            string errorJson = "{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32603,\"message\":\"" +
                                ex.Message.Replace("\"", "'").Replace("\\", "\\\\") +
                                "\"},\"id\":null}\n";
                            try
                            {
                                byte[] errorBytes = Encoding.UTF8.GetBytes(errorJson);
                                stream.Write(errorBytes, 0, errorBytes.Length);
                                stream.Flush();
                            }
                            catch { }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Client {clientId} fatal error: {ex}");
            }
            finally
            {
                Log($"Client {clientId} disconnected");
                try { client?.Close(); } catch { }
                _clients.TryRemove(clientId, out _);
            }
        }

        private static void Log(string message)
        {
            // KEMET: debug output only. Upstream also wrote to AutoCAD's command line here, but this
            // runs on the socket threads and the AutoCAD API is not thread-safe; every tool call
            // opens a connection, so it ran on every call.
            System.Diagnostics.Debug.WriteLine($"[MCP] {message}");
        }
    }
}
