using System;
using System.Collections.ObjectModel;
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

    private DeviceWatcher? _deviceWatcher;
    private WiFiDirectAdvertisementPublisher? _publisher;
    private WiFiDirectConnectionListener? _listener;
    private WiFiDirectDevice? _connectedDevice;
    private StreamSocketListener? _serverListener;
    private StreamSocket? _clientSocket;
    private DataWriter? _writer;
    private DataReader? _reader;

    public ObservableCollection<PeerInfoViewModel> Peers { get; } = new ObservableCollection<PeerInfoViewModel>();

    public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

    public MainPage()
    {
        InitializeComponent();
        AppendLog("Ready.");
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
            AppendLog("Host mode started. Advertising Wi‑Fi Direct presence.");
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

            _connectedDevice?.Dispose();
            _connectedDevice = await WiFiDirectDevice.FromIdAsync(request.DeviceInformation.Id);

            if (_connectedDevice is null)
            {
                AppendLog("Failed to accept Wi‑Fi Direct connection.");
                return;
            }

            _connectedDevice.ConnectionStatusChanged += ConnectedDevice_ConnectionStatusChanged;

            var pairs = _connectedDevice.GetConnectionEndpointPairs();
            var local = pairs.Count > 0 ? pairs[0].LocalHostName?.DisplayName : "n/a";
            var remote = pairs.Count > 0 ? pairs[0].RemoteHostName?.DisplayName : "n/a";
            AppendLog($"Wi‑Fi Direct connected. Local={local}, Remote={remote}");

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
            _deviceWatcher?.Stop();
            _deviceWatcher = DeviceInformation.CreateWatcher(WiFiDirectDevice.GetDeviceSelector(WiFiDirectDeviceSelectorType.AssociationEndpoint));
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
                DeviceId = args.Id,
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

            _connectedDevice?.Dispose();
            _connectedDevice = await WiFiDirectDevice.FromIdAsync(peer.DeviceId);

            if (_connectedDevice is null)
            {
                AppendLog("Connection failed.");
                return;
            }

            _connectedDevice.ConnectionStatusChanged += ConnectedDevice_ConnectionStatusChanged;
            AppendLog("Wi‑Fi Direct connected to " + peer.DisplayName);

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
            AppendLog("TCP server listening on port " + DemoPort);
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
            _clientSocket?.Dispose();
            _clientSocket = args.Socket;
            _writer = new DataWriter(_clientSocket.OutputStream);
            _reader = new DataReader(_clientSocket.InputStream)
            {
                InputStreamOptions = InputStreamOptions.Partial
            };

            AppendLog("TCP client connected.");
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
            _clientSocket?.Dispose();
            _clientSocket = new StreamSocket();
            await _clientSocket.ConnectAsync(hostName, DemoPort);
            _writer = new DataWriter(_clientSocket.OutputStream);
            _reader = new DataReader(_clientSocket.InputStream)
            {
                InputStreamOptions = InputStreamOptions.Partial
            };

            AppendLog("TCP client connected to remote host.");
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
                    await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => AppendLog("Socket closed."));
                    break;
                }

                builder.Append(_reader.ReadString(loaded));

                while (true)
                {
                    var buffer = builder.ToString();
                    var newlineIndex = buffer.IndexOf('
');
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
            AppendLog("No active TCP channel.");
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

    private void AppendLog(string message)
    {
        Logs.Add($"[{DateTimeOffset.Now:HH:mm:ss}] {message}");
        if (Logs.Count > 200)
        {
            Logs.RemoveAt(0);
        }
    }
}

public sealed class PeerInfoViewModel
{
    public string DisplayName { get; set; } = string.Empty;

    public string DeviceId { get; set; } = string.Empty;
}
