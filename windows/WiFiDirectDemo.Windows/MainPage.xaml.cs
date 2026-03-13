using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WiFiDirectDemo.Protocol;
using Windows.Devices.Enumeration;
using Windows.Devices.WiFiDirect;
using Windows.Networking;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace WiFiDirectDemo.Windows;

public sealed partial class MainPage : Page
{
    private const string DemoPort = "50001";
    private const int MaxLogItems = 250;

    private DeviceWatcher? _deviceWatcher;
    private WiFiDirectAdvertisementPublisher? _publisher;
    private WiFiDirectConnectionListener? _listener;
    private WiFiDirectDevice? _connectedDevice;
    private StreamSocketListener? _serverListener;
    private StreamSocket? _clientSocket;
    private DataWriter? _writer;
    private DataReader? _reader;

    private StreamSocketListener? _portAccessListener;
    private PortAccessConfig? _portAccessConfig;

    public ObservableCollection<PeerInfoViewModel> Peers { get; } = new();

    public ObservableCollection<string> Logs { get; } = new();

    public MainPage()
    {
        InitializeComponent();
        AppendLog("Ready.");
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        StopPortAccessCore();
        CleanupConnection();
    }

    private void StartHost_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_publisher is null)
            {
                _publisher = new WiFiDirectAdvertisementPublisher();
                _publisher.Advertisement.ListenStateDiscoverability = WiFiDirectAdvertisementListenStateDiscoverability.Normal;
                _publisher.Advertisement.IsAutonomousGroupOwnerEnabled = true;
                _publisher.StatusChanged += Publisher_StatusChanged;
            }

            if (_listener is null)
            {
                _listener = new WiFiDirectConnectionListener();
                _listener.ConnectionRequested += Listener_ConnectionRequested;
            }

            _publisher.Start();
            AppendLog("Host mode started. Advertising Wi-Fi Direct presence.");
        }
        catch (Exception ex)
        {
            AppendLog("StartHost failed: " + ex.Message);
        }
    }

    private void Publisher_StatusChanged(WiFiDirectAdvertisementPublisher sender, WiFiDirectAdvertisementPublisherStatusChangedEventArgs args)
    {
        _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
        {
            AppendLog($"Advertiser status: {args.Status}");
        });
    }

    private async void Listener_ConnectionRequested(WiFiDirectConnectionListener sender, WiFiDirectConnectionRequestedEventArgs args)
    {
        try
        {
            using var request = args.GetConnectionRequest();
            AppendLog($"Incoming connection request from: {request.DeviceInformation.Name}");

            ReplaceConnectedDevice(await WiFiDirectDevice.FromIdAsync(request.DeviceInformation.Id));
            if (_connectedDevice is null)
            {
                AppendLog("Failed to accept Wi-Fi Direct connection.");
                return;
            }

            var pairs = _connectedDevice.GetConnectionEndpointPairs();
            var local = pairs.Count > 0 ? pairs[0].LocalHostName?.DisplayName : "n/a";
            var remote = pairs.Count > 0 ? pairs[0].RemoteHostName?.DisplayName : "n/a";
            AppendLog($"Wi-Fi Direct connected. Local={local}, Remote={remote}");

            await StartServerAsync();
        }
        catch (Exception ex)
        {
            AppendLog("Accept request failed: " + ex.Message);
        }
    }

    private void ConnectedDevice_ConnectionStatusChanged(WiFiDirectDevice sender, object args)
    {
        _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
        {
            AppendLog($"Connection status changed: {sender.ConnectionStatus}");
        });
    }

    private void Discover_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Peers.Clear();

            if (_deviceWatcher is not null)
            {
                UnsubscribeDeviceWatcher(_deviceWatcher);
                _deviceWatcher.Stop();
            }

            _deviceWatcher = DeviceInformation.CreateWatcher(
                WiFiDirectDevice.GetDeviceSelector(WiFiDirectDeviceSelectorType.AssociationEndpoint));
            _deviceWatcher.Added += DeviceWatcher_Added;
            _deviceWatcher.Updated += DeviceWatcher_Updated;
            _deviceWatcher.Removed += DeviceWatcher_Removed;
            _deviceWatcher.EnumerationCompleted += DeviceWatcher_EnumerationCompleted;
            _deviceWatcher.Start();

            AppendLog("Peer discovery started.");
        }
        catch (Exception ex)
        {
            AppendLog("Discover failed: " + ex.Message);
        }
    }

    private async void DeviceWatcher_Added(DeviceWatcher sender, DeviceInformation args)
    {
        await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
        {
            Peers.Add(new PeerInfoViewModel
            {
                DisplayName = string.IsNullOrWhiteSpace(args.Name) ? "(unnamed)" : args.Name,
                DeviceId = args.Id
            });

            AppendLog("Discovered peer: " + (string.IsNullOrWhiteSpace(args.Name) ? args.Id : args.Name));
        });
    }

    private async void DeviceWatcher_Updated(DeviceWatcher sender, DeviceInformationUpdate args)
    {
        await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
        {
            AppendLog("Peer updated: " + args.Id);
        });
    }

    private async void DeviceWatcher_Removed(DeviceWatcher sender, DeviceInformationUpdate args)
    {
        await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
        {
            var existing = Peers.FirstOrDefault(p => p.DeviceId == args.Id);
            if (existing is not null)
            {
                Peers.Remove(existing);
            }

            AppendLog("Peer removed: " + args.Id);
        });
    }

    private async void DeviceWatcher_EnumerationCompleted(DeviceWatcher sender, object args)
    {
        await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
        {
            AppendLog("Peer discovery enumeration completed.");
        });
    }

    private async void ConnectSelected_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (PeersListView.SelectedItem is not PeerInfoViewModel peer)
            {
                AppendLog("Select a peer first.");
                return;
            }

            ReplaceConnectedDevice(await WiFiDirectDevice.FromIdAsync(peer.DeviceId));
            if (_connectedDevice is null)
            {
                AppendLog("Connection failed.");
                return;
            }

            AppendLog("Wi-Fi Direct connected to " + peer.DisplayName);

            var pairs = _connectedDevice.GetConnectionEndpointPairs();
            if (pairs.Count == 0)
            {
                AppendLog("No endpoint pair returned.");
                return;
            }

            var endpoint = pairs[0];
            AppendLog($"Endpoint pair. Local={endpoint.LocalHostName?.DisplayName}, Remote={endpoint.RemoteHostName?.DisplayName}");
            await StartClientAsync(endpoint.RemoteHostName);
        }
        catch (Exception ex)
        {
            AppendLog("Connect failed: " + ex.Message);
        }
    }

    private async Task StartServerAsync()
    {
        try
        {
            _serverListener?.Dispose();
            _serverListener = new StreamSocketListener();
            _serverListener.ConnectionReceived += ServerListener_ConnectionReceived;
            await _serverListener.BindServiceNameAsync(DemoPort);
            AppendLog("TCP demo server listening on port " + DemoPort);
        }
        catch (Exception ex)
        {
            AppendLog("StartServer failed: " + ex.Message);
        }
    }

    private async void ServerListener_ConnectionReceived(StreamSocketListener sender, StreamSocketListenerConnectionReceivedEventArgs args)
    {
        try
        {
            ReplaceClientSocket(args.Socket);

            AppendLog("TCP demo client connected.");
            await SendProtocolMessageAsync(ProtocolMessage.Hello("Windows-Host", "host-ready"));
            _ = Task.Run(ReceiveLoopAsync);
        }
        catch (Exception ex)
        {
            AppendLog("ConnectionReceived failed: " + ex.Message);
        }
    }

    private async Task StartClientAsync(HostName hostName)
    {
        try
        {
            var socket = new StreamSocket();
            await socket.ConnectAsync(hostName, DemoPort);
            ReplaceClientSocket(socket);

            AppendLog("TCP demo client connected to remote host.");
            await SendProtocolMessageAsync(ProtocolMessage.Hello("Windows-Client", "client-ready"));
            _ = Task.Run(ReceiveLoopAsync);
        }
        catch (Exception ex)
        {
            AppendLog("StartClient failed: " + ex.Message);
        }
    }

    private async Task ReceiveLoopAsync()
    {
        if (_reader is null)
        {
            return;
        }

        var builder = new StringBuilder();

        try
        {
            while (true)
            {
                var loaded = await _reader.LoadAsync(512);
                if (loaded == 0)
                {
                    await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => AppendLog("TCP demo socket closed."));
                    break;
                }

                builder.Append(_reader.ReadString(loaded));

                while (true)
                {
                    var buffer = builder.ToString();
                    var newlineIndex = buffer.IndexOf('\n');
                    if (newlineIndex < 0)
                    {
                        break;
                    }

                    var line = buffer.Substring(0, newlineIndex).Trim();
                    builder.Clear();
                    builder.Append(buffer.Substring(newlineIndex + 1));

                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        var message = JsonLineProtocol.Deserialize(line);
                        await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                        {
                            AppendLog($"[{message.Type}] {message.Sender}: {message.Text}");
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => AppendLog("Receive loop failed: " + ex.Message));
        }
    }

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        var text = MessageTextBox.Text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        await SendProtocolMessageAsync(ProtocolMessage.Chat("Windows", text));
        MessageTextBox.Text = string.Empty;
    }

    private async Task SendProtocolMessageAsync(ProtocolMessage message)
    {
        if (_writer is null)
        {
            AppendLog("No active TCP demo channel.");
            return;
        }

        try
        {
            var payload = JsonLineProtocol.Serialize(message) + "\n";
            _writer.WriteString(payload);
            await _writer.StoreAsync();
            await _writer.FlushAsync();
            AppendLog($"Sent [{message.Type}] {message.Text}");
        }
        catch (Exception ex)
        {
            AppendLog("Send failed: " + ex.Message);
        }
    }

    private async void StartPortAccess_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadPortAccessConfig(out var config))
        {
            return;
        }

        await StartPortAccessAsync(config);
    }

    private void StopPortAccess_Click(object sender, RoutedEventArgs e)
    {
        StopPortAccessCore(logStop: true);
    }

    private bool TryReadPortAccessConfig(out PortAccessConfig config)
    {
        config = default!;

        if (!int.TryParse(ExposePortTextBox.Text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var ingressPort) ||
            ingressPort is < 1 or > 65535)
        {
            AppendLog("Ingress port must be a value between 1 and 65535.");
            return false;
        }

        if (string.Equals(ingressPort.ToString(CultureInfo.InvariantCulture), DemoPort, StringComparison.Ordinal))
        {
            AppendLog("Ingress port cannot be 50001 (reserved by demo control channel).");
            return false;
        }

        var targetHost = (TargetHostTextBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(targetHost))
        {
            targetHost = "127.0.0.1";
            TargetHostTextBox.Text = targetHost;
        }

        if (!IsSupportedTargetHost(targetHost))
        {
            AppendLog("For safety, target host must be localhost, 127.0.0.1, or ::1.");
            return false;
        }

        if (!int.TryParse(TargetPortTextBox.Text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var targetPort) ||
            targetPort is < 1 or > 65535)
        {
            AppendLog("Target port must be a value between 1 and 65535.");
            return false;
        }

        if (!TryParseAllowedPorts(AllowedPortsTextBox.Text, out var allowedPorts, out var errorMessage))
        {
            AppendLog(errorMessage);
            return false;
        }

        if (!allowedPorts.Contains(targetPort))
        {
            AppendLog($"Target port {targetPort} is not in allowed list: {string.Join(", ", allowedPorts.OrderBy(p => p))}");
            return false;
        }

        config = new PortAccessConfig(ingressPort, targetHost, targetPort, allowedPorts);
        return true;
    }

    private static bool IsSupportedTargetHost(string host)
    {
        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
               host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
               host.Equals("::1", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseAllowedPorts(string? allowedPortsText, out HashSet<int> allowedPorts, out string errorMessage)
    {
        allowedPorts = new HashSet<int>();
        errorMessage = string.Empty;

        var tokens = (allowedPortsText ?? string.Empty)
            .Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length == 0)
        {
            errorMessage = "Allowed target ports list cannot be empty.";
            return false;
        }

        foreach (var token in tokens)
        {
            if (!int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var port) ||
                port is < 1 or > 65535)
            {
                errorMessage = $"Invalid allowed port value: {token}";
                return false;
            }

            allowedPorts.Add(port);
        }

        return true;
    }

    private async Task StartPortAccessAsync(PortAccessConfig config)
    {
        StopPortAccessCore();

        try
        {
            _portAccessConfig = config;
            _portAccessListener = new StreamSocketListener();
            _portAccessListener.ConnectionReceived += PortAccessListener_ConnectionReceived;
            await _portAccessListener.BindServiceNameAsync(config.IngressPort.ToString(CultureInfo.InvariantCulture));

            UpdatePortAccessStatus($"Status: Running {config.IngressPort} -> {config.TargetHost}:{config.TargetPort}");
            AppendLog($"Port access started: {config.IngressPort} -> {config.TargetHost}:{config.TargetPort}");
            AppendLog($"Allowed target ports: {string.Join(", ", config.AllowedTargetPorts.OrderBy(p => p))}");
        }
        catch (Exception ex)
        {
            StopPortAccessCore();
            UpdatePortAccessStatus("Status: Failed to start");
            AppendLog("StartPortAccess failed: " + ex.Message);
        }
    }

    private void StopPortAccessCore(bool logStop = false)
    {
        try
        {
            if (_portAccessListener is not null)
            {
                _portAccessListener.ConnectionReceived -= PortAccessListener_ConnectionReceived;
                _portAccessListener.Dispose();
                _portAccessListener = null;
            }
        }
        catch (Exception ex)
        {
            AppendLog("StopPortAccess failed: " + ex.Message);
        }
        finally
        {
            _portAccessConfig = null;
            UpdatePortAccessStatus("Status: Stopped");
        }

        if (logStop)
        {
            AppendLog("Port access stopped.");
        }
    }

    private async void PortAccessListener_ConnectionReceived(StreamSocketListener sender, StreamSocketListenerConnectionReceivedEventArgs args)
    {
        var config = _portAccessConfig;
        if (config is null)
        {
            args.Socket.Dispose();
            return;
        }

        if (!config.AllowedTargetPorts.Contains(config.TargetPort))
        {
            args.Socket.Dispose();
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                AppendLog($"Port access blocked: target port {config.TargetPort} is not in current allowed list.");
            });
            return;
        }

        var remoteAddress = args.Socket.Information.RemoteAddress?.DisplayName ?? "unknown";
        var remotePort = args.Socket.Information.RemotePort ?? "?";
        var remoteEndpoint = remoteAddress + ":" + remotePort;

        var targetSocket = new StreamSocket();

        try
        {
            await targetSocket.ConnectAsync(
                new HostName(config.TargetHost),
                config.TargetPort.ToString(CultureInfo.InvariantCulture));

            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                AppendLog($"Port access: {remoteEndpoint} -> {config.TargetHost}:{config.TargetPort}");
            });

            _ = Task.Run(() => RelayConnectionAsync(args.Socket, targetSocket, remoteEndpoint));
        }
        catch (Exception ex)
        {
            targetSocket.Dispose();
            args.Socket.Dispose();

            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                AppendLog($"Port access rejected for {remoteEndpoint}: {ex.Message}");
            });
        }
    }

    private async Task RelayConnectionAsync(StreamSocket inbound, StreamSocket outbound, string remoteEndpoint)
    {
        try
        {
            var inboundToTarget = PumpAsync(inbound.InputStream, outbound.OutputStream);
            var targetToInbound = PumpAsync(outbound.InputStream, inbound.OutputStream);
            var firstCompleted = await Task.WhenAny(inboundToTarget, targetToInbound);

            try
            {
                await firstCompleted;
            }
            catch
            {
                // Connection shutdown paths are expected when either side closes.
            }
        }
        catch (Exception ex)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                AppendLog($"Port access relay error for {remoteEndpoint}: {ex.Message}");
            });
        }
        finally
        {
            inbound.Dispose();
            outbound.Dispose();

            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                AppendLog($"Port access closed for {remoteEndpoint}");
            });
        }
    }

    private static async Task PumpAsync(IInputStream input, IOutputStream output)
    {
        using var reader = new DataReader(input)
        {
            InputStreamOptions = InputStreamOptions.Partial
        };
        using var writer = new DataWriter(output);

        while (true)
        {
            var loaded = await reader.LoadAsync(8192);
            if (loaded == 0)
            {
                break;
            }

            var data = new byte[(int)loaded];
            reader.ReadBytes(data);

            writer.WriteBytes(data);
            await writer.StoreAsync();
        }

        await writer.FlushAsync();
    }

    private void ReplaceConnectedDevice(WiFiDirectDevice? newDevice)
    {
        if (_connectedDevice is not null)
        {
            _connectedDevice.ConnectionStatusChanged -= ConnectedDevice_ConnectionStatusChanged;
            _connectedDevice.Dispose();
        }

        _connectedDevice = newDevice;

        if (_connectedDevice is not null)
        {
            _connectedDevice.ConnectionStatusChanged += ConnectedDevice_ConnectionStatusChanged;
        }
    }

    private void ReplaceClientSocket(StreamSocket socket)
    {
        _writer?.Dispose();
        _reader?.Dispose();
        _clientSocket?.Dispose();

        _clientSocket = socket;
        _writer = new DataWriter(_clientSocket.OutputStream);
        _reader = new DataReader(_clientSocket.InputStream)
        {
            InputStreamOptions = InputStreamOptions.Partial
        };
    }

    private void CleanupConnection()
    {
        try
        {
            if (_deviceWatcher is not null)
            {
                UnsubscribeDeviceWatcher(_deviceWatcher);
                if (_deviceWatcher.Status == DeviceWatcherStatus.Started ||
                    _deviceWatcher.Status == DeviceWatcherStatus.EnumerationCompleted)
                {
                    _deviceWatcher.Stop();
                }

                _deviceWatcher = null;
            }
        }
        catch
        {
            // Swallow watcher shutdown exceptions during unload.
        }

        if (_listener is not null)
        {
            _listener.ConnectionRequested -= Listener_ConnectionRequested;
            _listener = null;
        }

        if (_publisher is not null)
        {
            _publisher.StatusChanged -= Publisher_StatusChanged;
            try
            {
                _publisher.Stop();
            }
            catch
            {
                // Ignore shutdown exceptions.
            }

            _publisher = null;
        }

        _serverListener?.Dispose();
        _serverListener = null;

        _writer?.Dispose();
        _writer = null;
        _reader?.Dispose();
        _reader = null;
        _clientSocket?.Dispose();
        _clientSocket = null;

        ReplaceConnectedDevice(null);
    }

    private void UnsubscribeDeviceWatcher(DeviceWatcher watcher)
    {
        watcher.Added -= DeviceWatcher_Added;
        watcher.Updated -= DeviceWatcher_Updated;
        watcher.Removed -= DeviceWatcher_Removed;
        watcher.EnumerationCompleted -= DeviceWatcher_EnumerationCompleted;
    }

    private void UpdatePortAccessStatus(string status)
    {
        PortAccessStatusText.Text = status;
    }

    private void AppendLog(string message)
    {
        Logs.Add($"[{DateTimeOffset.Now:HH:mm:ss}] {message}");
        if (Logs.Count > MaxLogItems)
        {
            Logs.RemoveAt(0);
        }

        if (LogListView.Items.Count > 0)
        {
            LogListView.ScrollIntoView(LogListView.Items[LogListView.Items.Count - 1]);
        }
    }

    private sealed class PortAccessConfig
    {
        public PortAccessConfig(int ingressPort, string targetHost, int targetPort, IReadOnlyCollection<int> allowedTargetPorts)
        {
            IngressPort = ingressPort;
            TargetHost = targetHost;
            TargetPort = targetPort;
            AllowedTargetPorts = allowedTargetPorts;
        }

        public int IngressPort { get; }

        public string TargetHost { get; }

        public int TargetPort { get; }

        public IReadOnlyCollection<int> AllowedTargetPorts { get; }
    }
}

public sealed class PeerInfoViewModel
{
    public string DisplayName { get; set; } = string.Empty;

    public string DeviceId { get; set; } = string.Empty;
}
