﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Collections;
using System.IO;
using System.Text;
using Microsoft.AspNetCore.SignalR.Internal.Formatters;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.AspNetCore.SignalR.Internal.Protocol
{
    public static class HandshakeProtocol
    {
        private static readonly UTF8Encoding _utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        private const string ProtocolPropertyName = "protocol";
        private const string ErrorPropertyName = "error";
        private const string TypePropertyName = "type";

        public static void WriteRequestMessage(HandshakeRequestMessage requestMessage, Stream output)
        {
            using (var writer = CreateJsonTextWriter(output))
            {
                writer.WriteStartObject();
                writer.WritePropertyName(ProtocolPropertyName);
                writer.WriteValue(requestMessage.Protocol);
                writer.WriteEndObject();
            }

            TextMessageFormatter.WriteRecordSeparator(output);
        }

        public static void WriteResponseMessage(HandshakeResponseMessage responseMessage, Stream output)
        {
            using (var writer = CreateJsonTextWriter(output))
            {
                writer.WriteStartObject();
                if (!string.IsNullOrEmpty(responseMessage.Error))
                {
                    writer.WritePropertyName(ErrorPropertyName);
                    writer.WriteValue(responseMessage.Error);
                }
                writer.WriteEndObject();
            }

            TextMessageFormatter.WriteRecordSeparator(output);
        }

        private static JsonTextWriter CreateJsonTextWriter(Stream output)
        {
            return new JsonTextWriter(new StreamWriter(output, _utf8NoBom, 1024, leaveOpen: true));
        }

        public static HandshakeResponseMessage ParseResponseMessage(ReadOnlySpan<byte> buffer)
        {
            using (var memoryStream = new MemoryStream(buffer.ToArray()))
            {
                using (var reader = new JsonTextReader(new StreamReader(memoryStream)))
                {
                    var token = JToken.ReadFrom(reader);
                    var handshakeJObject = JsonUtils.GetObject(token);

                    // a handshake response does not have a type
                    // check the incoming message was not any other type of message
                    var type = JsonUtils.GetOptionalProperty<string>(handshakeJObject, TypePropertyName);
                    if (!string.IsNullOrEmpty(type))
                    {
                        throw new InvalidOperationException("Handshake response should not have a 'type' value.");
                    }

                    var error = JsonUtils.GetOptionalProperty<string>(handshakeJObject, ErrorPropertyName);
                    return new HandshakeResponseMessage(error);
                }
            }
        }

        public static bool TryParseRequestMessage(ReadOnlySequence<byte> buffer, out HandshakeRequestMessage requestMessage, out SequencePosition consumed, out SequencePosition examined)
        {
            if (!TryReadMessageIntoSingleMemory(buffer, out consumed, out examined, out var memory))
            {
                requestMessage = null;
                return false;
            }

            if (!TextMessageParser.TryParseMessage(ref memory, out var payload))
            {
                throw new InvalidDataException("Unable to parse payload as a handshake request message.");
            }

            using (var memoryStream = new MemoryStream(payload.ToArray()))
            {
                using (var reader = new JsonTextReader(new StreamReader(memoryStream)))
                {
                    var token = JToken.ReadFrom(reader);
                    var handshakeJObject = JsonUtils.GetObject(token);
                    var protocol = JsonUtils.GetRequiredProperty<string>(handshakeJObject, ProtocolPropertyName);
                    requestMessage = new HandshakeRequestMessage(protocol);
                }
            }
            return true;
        }

        internal static bool TryReadMessageIntoSingleMemory(ReadOnlySequence<byte> buffer, out SequencePosition consumed, out SequencePosition examined, out ReadOnlyMemory<byte> memory)
        {
            var separator = buffer.PositionOf(TextMessageFormatter.RecordSeparator);
            if (separator == null)
            {
                // Haven't seen the entire message so bail
                consumed = buffer.Start;
                examined = buffer.End;
                memory = null;
                return false;
            }

            consumed = buffer.GetPosition(1, separator.Value);
            examined = consumed;
            memory = buffer.IsSingleSegment ? buffer.First : buffer.ToArray();
            return true;
        }
    }
}
