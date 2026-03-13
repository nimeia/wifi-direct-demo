package com.example.wifidirectdemo

import org.json.JSONObject
import java.time.Instant

data class ProtocolMessage(
    val type: String,
    val sender: String,
    val text: String,
    val timestampUtc: String = Instant.now().toString()
) {
    fun toJsonLine(): String {
        val json = JSONObject()
            .put("type", type)
            .put("sender", sender)
            .put("text", text)
            .put("timestampUtc", timestampUtc)
        return json.toString()
    }

    companion object {
        fun hello(sender: String, text: String = "ready"): ProtocolMessage =
            ProtocolMessage("hello", sender, text)

        fun chat(sender: String, text: String): ProtocolMessage =
            ProtocolMessage("chat", sender, text)

        fun ping(sender: String, text: String = "ping"): ProtocolMessage =
            ProtocolMessage("ping", sender, text)

        fun fromJson(json: String): ProtocolMessage {
            val obj = JSONObject(json)
            return ProtocolMessage(
                type = obj.optString("type"),
                sender = obj.optString("sender"),
                text = obj.optString("text"),
                timestampUtc = obj.optString("timestampUtc")
            )
        }
    }
}
