package com.example.wifidirectdemo

import android.Manifest
import android.content.IntentFilter
import android.content.pm.PackageManager
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
import java.net.ServerSocket
import java.net.Socket
import java.util.concurrent.Executors

class MainActivity : AppCompatActivity() {

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

    private val ioExecutor = Executors.newSingleThreadExecutor()

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
        val config = WifiP2pConfig.Builder()
            .setDeviceAddress(device.deviceAddress)
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
        appendLog("Group formed=${info.groupFormed}, isGroupOwner=${info.isGroupOwner}, owner=${info.groupOwnerAddress?.hostAddress}")
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
}
