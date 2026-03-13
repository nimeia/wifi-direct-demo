package com.example.wifidirectdemo

import android.Manifest
import android.content.IntentFilter
import android.content.pm.PackageManager
import android.net.MacAddress
import android.net.wifi.p2p.WifiP2pConfig
import android.net.wifi.p2p.WifiP2pDevice
import android.net.wifi.p2p.WifiP2pInfo
import android.net.wifi.p2p.WifiP2pManager
import android.os.Build
import android.os.Bundle
import android.widget.ArrayAdapter
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AppCompatActivity
import androidx.core.content.ContextCompat
import com.example.wifidirectdemo.databinding.ActivityMainBinding
import java.io.BufferedReader
import java.io.BufferedWriter
import java.io.InputStreamReader
import java.io.OutputStreamWriter
import java.net.HttpURLConnection
import java.net.InetSocketAddress
import java.net.ServerSocket
import java.net.Socket
import java.net.URL
import java.util.concurrent.Executors
import kotlin.system.measureTimeMillis

class MainActivity : AppCompatActivity() {
    companion object {
        private const val PORT_TEST_TIMEOUT_MS = 5000
    }

    private lateinit var binding: ActivityMainBinding
    private lateinit var manager: WifiP2pManager
    private lateinit var channel: WifiP2pManager.Channel
    private lateinit var receiver: WiFiDirectBroadcastReceiver
    private val intentFilter = IntentFilter()

    private val peers = mutableListOf<WifiP2pDevice>()
    private lateinit var peersAdapter: ArrayAdapter<String>

    private var serverSocket: ServerSocket? = null
    private var socket: Socket? = null
    private var writer: BufferedWriter? = null
    private var reader: BufferedReader? = null

    private val ioExecutor = Executors.newCachedThreadPool()

    private val permissionLauncher = registerForActivityResult(
        ActivityResultContracts.RequestMultiplePermissions()
    ) {
        appendLog("Permission result received.")
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityMainBinding.inflate(layoutInflater)
        setContentView(binding.root)

        manager = getSystemService(WIFI_P2P_SERVICE) as WifiP2pManager
        channel = manager.initialize(this, mainLooper, null)
        receiver = WiFiDirectBroadcastReceiver(manager, channel, this)

        intentFilter.addAction(WifiP2pManager.WIFI_P2P_STATE_CHANGED_ACTION)
        intentFilter.addAction(WifiP2pManager.WIFI_P2P_PEERS_CHANGED_ACTION)
        intentFilter.addAction(WifiP2pManager.WIFI_P2P_CONNECTION_CHANGED_ACTION)
        intentFilter.addAction(WifiP2pManager.WIFI_P2P_THIS_DEVICE_CHANGED_ACTION)

        peersAdapter = ArrayAdapter(this, android.R.layout.simple_list_item_1, mutableListOf())
        binding.peersListView.adapter = peersAdapter

        binding.discoverButton.setOnClickListener {
            ensurePermissions()
            discoverPeers()
        }

        binding.peersListView.setOnItemClickListener { _, _, position, _ ->
            connectToPeer(peers[position])
        }

        binding.sendButton.setOnClickListener {
            val text = binding.messageEditText.text?.toString()?.trim().orEmpty()
            if (text.isNotBlank()) {
                sendMessage(ProtocolMessage.chat("Android", text))
                binding.messageEditText.setText("")
            }
        }

        binding.httpTestButton.setOnClickListener {
            runHttpPortTest()
        }

        binding.tcpTestButton.setOnClickListener {
            runTcpPortTest()
        }

        appendLog("Ready.")
    }

    override fun onResume() {
        super.onResume()
        registerReceiver(receiver, intentFilter)
    }

    override fun onPause() {
        super.onPause()
        unregisterReceiver(receiver)
    }

    override fun onDestroy() {
        super.onDestroy()
        serverSocket?.closeQuietly()
        socket?.closeQuietly()
        ioExecutor.shutdownNow()
    }

    private fun ensurePermissions() {
        val permissions = mutableListOf<String>()
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            permissions += Manifest.permission.NEARBY_WIFI_DEVICES
        } else {
            permissions += Manifest.permission.ACCESS_FINE_LOCATION
        }

        val missing = permissions.filter {
            ContextCompat.checkSelfPermission(this, it) != PackageManager.PERMISSION_GRANTED
        }

        if (missing.isNotEmpty()) {
            permissionLauncher.launch(missing.toTypedArray())
        }
    }

    private fun discoverPeers() {
        manager.discoverPeers(channel, object : WifiP2pManager.ActionListener {
            override fun onSuccess() {
                appendLog("discoverPeers() started.")
            }

            override fun onFailure(reason: Int) {
                appendLog("discoverPeers() failed: $reason")
            }
        })
    }

    fun updatePeers(newPeers: List<WifiP2pDevice>) {
        peers.clear()
        peers.addAll(newPeers)
        peersAdapter.clear()
        peersAdapter.addAll(newPeers.map { it.deviceName.ifBlank { it.deviceAddress } })
        peersAdapter.notifyDataSetChanged()
        appendLog("Peers: ${newPeers.size}")
    }

    private fun connectToPeer(device: WifiP2pDevice) {
        val macAddress = try {
            MacAddress.fromString(device.deviceAddress)
        } catch (_: IllegalArgumentException) {
            appendLog("Invalid device address: ${device.deviceAddress}")
            return
        }

        val config = WifiP2pConfig.Builder()
            .setDeviceAddress(macAddress)
            .build()

        manager.connect(channel, config, object : WifiP2pManager.ActionListener {
            override fun onSuccess() {
                appendLog("connect() requested: ${device.deviceName}")
            }

            override fun onFailure(reason: Int) {
                appendLog("connect() failed: $reason")
            }
        })
    }

    fun onConnectionInfoAvailable(info: WifiP2pInfo) {
        val ownerHost = info.groupOwnerAddress?.hostAddress.orEmpty()
        appendLog("Group formed=${info.groupFormed}, isGroupOwner=${info.isGroupOwner}, owner=$ownerHost")

        val currentTestHost = binding.portTestHostEditText.text?.toString()?.trim().orEmpty()
        if (ownerHost.isNotBlank() && currentTestHost.isBlank()) {
            binding.portTestHostEditText.setText(ownerHost)
        }

        if (!info.groupFormed) {
            return
        }

        if (info.isGroupOwner) {
            startServer()
        } else {
            val host = info.groupOwnerAddress?.hostAddress ?: return
            startClient(host)
        }
    }

    private fun startServer() {
        ioExecutor.execute {
            try {
                serverSocket?.close()
                serverSocket = ServerSocket(50001)
                runOnUiThread { appendLog("TCP server listening on 50001") }
                socket = serverSocket!!.accept()
                attachSocket(socket!!)
                sendMessage(ProtocolMessage.hello("Android-Host", "host-ready"))
                readLoop()
            } catch (ex: Exception) {
                runOnUiThread { appendLog("startServer failed: ${ex.message}") }
            }
        }
    }

    private fun runHttpPortTest() {
        val endpoint = readPortTestEndpoint() ?: return
        ioExecutor.execute {
            var connection: HttpURLConnection? = null
            try {
                val url = URL("http://${endpoint.host}:${endpoint.port}${endpoint.path}")
                connection = (url.openConnection() as HttpURLConnection).apply {
                    requestMethod = "GET"
                    connectTimeout = PORT_TEST_TIMEOUT_MS
                    readTimeout = PORT_TEST_TIMEOUT_MS
                    instanceFollowRedirects = false
                }

                val conn = connection ?: return@execute
                var statusCode = -1
                val elapsed = measureTimeMillis {
                    statusCode = conn.responseCode
                }
                val source = if (statusCode in 200..399) conn.inputStream else conn.errorStream
                val bodySnippet = source?.bufferedReader(Charsets.UTF_8)?.use { reader ->
                    reader.readText()
                        .replace("\r", " ")
                        .replace("\n", " ")
                        .take(160)
                }.orEmpty()

                runOnUiThread {
                    appendLog(
                        "HTTP test ${endpoint.host}:${endpoint.port}${endpoint.path} -> " +
                            "status=$statusCode, connect=${elapsed}ms, snippet=${if (bodySnippet.isBlank()) "<empty>" else bodySnippet}"
                    )
                }
            } catch (ex: Exception) {
                runOnUiThread {
                    appendLog("HTTP test failed (${endpoint.host}:${endpoint.port}${endpoint.path}): ${ex.message}")
                }
            } finally {
                connection?.disconnect()
            }
        }
    }

    private fun runTcpPortTest() {
        val endpoint = readPortTestEndpoint() ?: return
        ioExecutor.execute {
            var socket: Socket? = null
            try {
                socket = Socket()
                val elapsed = measureTimeMillis {
                    socket.connect(InetSocketAddress(endpoint.host, endpoint.port), PORT_TEST_TIMEOUT_MS)
                }

                runOnUiThread {
                    appendLog("TCP test ${endpoint.host}:${endpoint.port} connected in ${elapsed}ms")
                }
            } catch (ex: Exception) {
                runOnUiThread {
                    appendLog("TCP test failed (${endpoint.host}:${endpoint.port}): ${ex.message}")
                }
            } finally {
                socket?.close()
            }
        }
    }

    private fun readPortTestEndpoint(): PortAccessEndpoint? {
        val host = binding.portTestHostEditText.text?.toString()?.trim().orEmpty()
        if (host.isBlank()) {
            appendLog("Port test host is required.")
            return null
        }

        val portText = binding.portTestPortEditText.text?.toString()?.trim().orEmpty()
        val port = portText.toIntOrNull()
        if (port == null || port !in 1..65535) {
            appendLog("Port test port must be 1..65535.")
            return null
        }

        var path = binding.portTestPathEditText.text?.toString()?.trim().orEmpty()
        if (path.isBlank()) {
            path = "/"
            binding.portTestPathEditText.setText(path)
        } else if (!path.startsWith("/")) {
            path = "/$path"
            binding.portTestPathEditText.setText(path)
        }

        return PortAccessEndpoint(host, port, path)
    }

    private fun startClient(host: String) {
        ioExecutor.execute {
            try {
                socket?.close()
                socket = Socket(host, 50001)
                attachSocket(socket!!)
                runOnUiThread { appendLog("TCP connected to $host:50001") }
                sendMessage(ProtocolMessage.hello("Android-Client", "client-ready"))
                readLoop()
            } catch (ex: Exception) {
                runOnUiThread { appendLog("startClient failed: ${ex.message}") }
            }
        }
    }

    private fun attachSocket(connected: Socket) {
        writer = BufferedWriter(OutputStreamWriter(connected.getOutputStream(), Charsets.UTF_8))
        reader = BufferedReader(InputStreamReader(connected.getInputStream(), Charsets.UTF_8))
    }

    private fun readLoop() {
        try {
            while (true) {
                val line = reader?.readLine() ?: break
                val message = ProtocolMessage.fromJson(line)
                runOnUiThread {
                    appendLog("[${message.type}] ${message.sender}: ${message.text}")
                }
            }
        } catch (ex: Exception) {
            runOnUiThread { appendLog("readLoop failed: ${ex.message}") }
        }
    }

    private fun sendMessage(message: ProtocolMessage) {
        ioExecutor.execute {
            try {
                writer?.apply {
                    write(message.toJsonLine())
                    newLine()
                    flush()
                } ?: runOnUiThread {
                    appendLog("No active socket.")
                }

                runOnUiThread {
                    appendLog("Sent [${message.type}] ${message.text}")
                }
            } catch (ex: Exception) {
                runOnUiThread { appendLog("sendMessage failed: ${ex.message}") }
            }
        }
    }

    fun appendLog(message: String) {
        val previous = binding.logTextView.text?.toString().orEmpty()
        binding.logTextView.text = previous + if (previous.isEmpty()) message else "\n$message"
    }

    private data class PortAccessEndpoint(
        val host: String,
        val port: Int,
        val path: String
    )
}

private fun ServerSocket.closeQuietly() {
    try {
        close()
    } catch (_: Exception) {
    }
}

private fun Socket.closeQuietly() {
    try {
        close()
    } catch (_: Exception) {
    }
}
