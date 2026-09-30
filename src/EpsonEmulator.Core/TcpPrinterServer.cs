using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace EpsonEmulator.Core
{
    public sealed class TcpPrinterServer : IDisposable
    {
        private TcpListener _listener;
        private CancellationTokenSource _cts;
        private readonly EscPosParser _parser = new EscPosParser();

        public bool IsRunning { get; private set; }
        public IPEndPoint LocalEndpoint => _listener?.LocalEndpoint as IPEndPoint;
        public event EventHandler<string> StatusChanged;
        public event EventHandler<ReceiptJob> JobReceived;
        public event EventHandler<Exception> ListenerError;

        public void Start(IPAddress bindAddress, int port)
        {
            if (IsRunning) return;
            if (port < 0 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port), "Port must be 0-65535.");

            _cts = new CancellationTokenSource();
            _listener = new TcpListener(bindAddress, port);
            _listener.Start();
            IsRunning = true;
            OnStatusChanged("Listening on " + bindAddress + ":" + port);
            Task.Run(() => AcceptLoop(_cts.Token));
        }

        public void Stop()
        {
            if (!IsRunning) return;
            _cts.Cancel();
            _listener.Stop();
            IsRunning = false;
            OnStatusChanged("Stopped");
        }

        public void Dispose()
        {
            Stop();
            _cts?.Dispose();
        }

        private async Task AcceptLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    _ = Task.Run(() => HandleClient(client, token));
                }
                catch (ObjectDisposedException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (SocketException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    OnListenerError(ex);
                }
            }
        }

        private async Task HandleClient(TcpClient client, CancellationToken token)
        {
            var remote = client.Client.RemoteEndPoint as IPEndPoint;
            OnStatusChanged("Connection from " + (remote == null ? "unknown" : remote.ToString()));
            using (client)
            using (var buffer = new MemoryStream())
            {
                var readBuffer = new byte[8192];
                try
                {
                    using (var stream = client.GetStream())
                    {
                        while (!token.IsCancellationRequested)
                        {
                            var read = await stream.ReadAsync(readBuffer, 0, readBuffer.Length, token).ConfigureAwait(false);
                            if (read == 0) break;
                            buffer.Write(readBuffer, 0, read);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (IOException ex)
                {
                    OnListenerError(ex);
                }

                if (buffer.Length > 0)
                {
                    var job = _parser.Parse(buffer.ToArray(), remote?.Address.ToString() ?? "", remote?.Port ?? 0, "ConnectionClosed");
                    OnJobReceived(job);
                    OnStatusChanged("Received job " + job.JobId + " (" + job.ByteCount + " bytes)");
                }
            }
        }

        private void OnStatusChanged(string message)
        {
            StatusChanged?.Invoke(this, message);
        }

        private void OnJobReceived(ReceiptJob job)
        {
            JobReceived?.Invoke(this, job);
        }

        private void OnListenerError(Exception exception)
        {
            ListenerError?.Invoke(this, exception);
        }
    }
}
