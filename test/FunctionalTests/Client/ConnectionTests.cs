﻿#region Copyright notice and license

// Copyright 2019 The gRPC Authors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

#endregion

using System.Net;
using System.Net.Sockets;
using Greet;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Shared;
using Grpc.Tests.Shared;
using Microsoft.AspNetCore.Connections.Features;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests.Client
{
    [TestFixture]
    public class ConnectionTests : FunctionalTestBase
    {
        [Test]
        public async Task ALPN_ProtocolDowngradedToHttp1_ThrowErrorFromServer()
        {
            SetExpectedErrorsFilter(r =>
            {
                return r.LoggerName == "Grpc.Net.Client.Internal.GrpcCall" && r.EventId.Name == "ErrorStartingCall";
            });

            // Arrange
            var httpClient = Fixture.CreateClient(TestServerEndpointName.Http1WithTls);

            var channel = GrpcChannel.ForAddress(httpClient.BaseAddress!, new GrpcChannelOptions
            {
                LoggerFactory = LoggerFactory,
                HttpClient = httpClient
            });

            var client = new Greeter.GreeterClient(channel);

            // Act
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => client.SayHelloAsync(new HelloRequest { Name = "John" }).ResponseAsync).DefaultTimeout();

            // Assert
            Assert.AreEqual(StatusCode.Internal, ex.StatusCode);
#if NET5_0_OR_GREATER
            var debugException = ex.Status.DebugException!;
            Assert.AreEqual("The SSL connection could not be established, see inner exception.", debugException.Message);
#else
            Assert.AreEqual("Request protocol 'HTTP/1.1' is not supported.", ex.Status.Detail);
#endif
        }

#if NET5_0_OR_GREATER
        [Test]
        public async Task UnixDomainSockets()
        {
            Task<HelloReply> UnaryUds(HelloRequest request, ServerCallContext context)
            {
#if NET6_0_OR_GREATER
                var endPoint = (UnixDomainSocketEndPoint)context.GetHttpContext().Features.Get<IConnectionSocketFeature>()!.Socket.LocalEndPoint!;
                Assert.NotNull(endPoint);
#endif

                return Task.FromResult(new HelloReply { Message = "Hello " + request.Name });
            }

            // Arrange
            var method = Fixture.DynamicGrpc.AddUnaryMethod<HelloRequest, HelloReply>(UnaryUds);

            var http = Fixture.CreateHandler(TestServerEndpointName.UnixDomainSocket);

            var channel = GrpcChannel.ForAddress(http.address, new GrpcChannelOptions
            {
                LoggerFactory = LoggerFactory,
                HttpHandler = http.handler
            });

            var client = TestClientFactory.Create(channel, method);

            // Act
            var response = await client.UnaryCall(new HelloRequest { Name = "John" }).ResponseAsync.DefaultTimeout();

            // Assert
            Assert.AreEqual("Hello John", response.Message);
        }
#endif

#if NET6_0_OR_GREATER
        [Test]
        [RequireHttp3]
        public async Task Http3()
        {
            // Arrange
            var http = Fixture.CreateHandler(TestServerEndpointName.Http3WithTls);

            var channel = GrpcChannel.ForAddress(http.address, new GrpcChannelOptions
            {
                LoggerFactory = LoggerFactory,
                HttpHandler = http.handler
            });

            var client = new Greeter.GreeterClient(channel);

            // Act
            var response = await client.SayHelloAsync(new HelloRequest { Name = "John" }).ResponseAsync.DefaultTimeout();

            // Assert
            Assert.AreEqual("Hello John", response.Message);
        }
#endif

#if NET5_0_OR_GREATER
        [Test]
        public async Task ShareSocketsHttpHandler()
        {
            Task<HelloReply> Unary(HelloRequest request, ServerCallContext context)
            {
                return Task.FromResult(new HelloReply { Message = request.Name });
            }

            // Arrange
            var method = Fixture.DynamicGrpc.AddUnaryMethod<HelloRequest, HelloReply>(Unary);

            var http = Fixture.CreateHandler(TestServerEndpointName.Http2);

            var channel1 = GrpcChannel.ForAddress(http.address, new GrpcChannelOptions
            {
                LoggerFactory = LoggerFactory,
                HttpHandler = http.handler
            });

            var channel2 = GrpcChannel.ForAddress(http.address, new GrpcChannelOptions
            {
                LoggerFactory = LoggerFactory,
                HttpHandler = http.handler
            });

            var client2 = TestClientFactory.Create(channel2, method);

            // Act
            var reply = await client2.UnaryCall(new HelloRequest { Name = "World" }).ResponseAsync.DefaultTimeout();

            // Assert
            Assert.AreEqual(HttpHandlerType.SocketsHttpHandler, channel1.HttpHandlerType);
            Assert.AreEqual(HttpHandlerType.SocketsHttpHandler, channel2.HttpHandlerType);
            Assert.AreEqual("World", reply.Message);
        }
#endif
    }
}
