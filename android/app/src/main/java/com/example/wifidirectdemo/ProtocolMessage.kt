package com.example.wifidirectdemo

import java.time.Instant
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.put

data class ProtocolMessage(
    val type: String,
    val sender: String,
    val text: String,
    val timestampUtc: String = Instant.now().toString()
) {
    fun toJsonLine(): String {
        return buildJsonObject {
            put("type", type)
            put("sender", sender)
            put("text", text)
            put("timestampUtc", timestampUtc)
        }.toString()
    }

    companion object {
        private val json = Json {
            ignoreUnknownKeys = true
        }

        fun hello(sender: String, text: String = "ready"): ProtocolMessage =
            ProtocolMessage("hello", sender, text)

        fun chat(sender: String, text: String): ProtocolMessage =
            ProtocolMessage("chat", sender, text)

        fun ping(sender: String, text: String = "ping"): ProtocolMessage =
            ProtocolMessage("ping", sender, text)

        fun fromJson(json: String): ProtocolMessage {
            val obj = this.json.parseToJsonElement(json).jsonObject
            return ProtocolMessage(
                type = obj["type"]?.jsonPrimitive?.content.orEmpty(),
                sender = obj["sender"]?.jsonPrimitive?.content.orEmpty(),
                text = obj["text"]?.jsonPrimitive?.content.orEmpty(),
                timestampUtc = obj["timestampUtc"]?.jsonPrimitive?.content.orEmpty()
            )
        }
    }
}
