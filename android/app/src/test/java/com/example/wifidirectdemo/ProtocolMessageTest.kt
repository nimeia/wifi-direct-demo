package com.example.wifidirectdemo

import org.junit.Assert.assertEquals
import org.junit.Test

class ProtocolMessageTest {

    @Test
    fun toJsonLine_and_fromJson_roundTrip() {
        val msg = ProtocolMessage.chat("android", "hello")
        val json = msg.toJsonLine()
        val parsed = ProtocolMessage.fromJson(json)

        assertEquals("chat", parsed.type)
        assertEquals("android", parsed.sender)
        assertEquals("hello", parsed.text)
    }
}
