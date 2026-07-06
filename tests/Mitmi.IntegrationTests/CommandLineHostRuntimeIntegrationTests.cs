using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using Mitmi.Host.Console;
using NModbus;
using NModbus.Data;

namespace Mitmi.IntegrationTests;

public sealed class CommandLineHostRuntimeIntegrationTests
{
    [Fact]
    public async Task RunAsync_writes_file_log_and_capture_records_for_modbus_exchange()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var tempDirectory = new TemporaryDirectory();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var factory = new ModbusFactory();
        using var upstreamListener = new TcpListener(IPAddress.Loopback, 0);
        upstreamListener.Start();
        var upstreamPort = ((IPEndPoint)upstreamListener.LocalEndpoint).Port;
        var listenPort = ReserveTcpPort();
        var dataStore = new DefaultSlaveDataStore();
        dataStore.HoldingRegisters.WritePoints(0, [0x1111, 0x2222]);

        var slaveNetwork = factory.CreateSlaveNetwork(upstreamListener);
        slaveNetwork.AddSlave(factory.CreateSlave(1, dataStore));
        var slaveTask = slaveNetwork.ListenAsync(timeout.Token);

        var configPath = Path.Combine(tempDirectory.Path, "mitmi.config.json");
        await File.WriteAllTextAsync(configPath, RuntimeConfigurationJson(listenPort, upstreamPort));

        using var hostCancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var hostTask = CommandLineHost.RunAsync(
            ["--config", configPath],
            output,
            error,
            hostCancellation.Token,
            applicationDirectory: tempDirectory.Path);

        using var client = await ConnectWithRetryAsync(IPAddress.Loopback, listenPort, timeout.Token);
        var master = factory.CreateMaster(client);
        var registers = await master.ReadHoldingRegistersAsync(1, 0, 2);

        Assert.Equal([0x1111, 0x2222], registers);

        client.Close();
        await hostCancellation.CancelAsync();
        var exitCode = await hostTask.WaitAsync(timeout.Token);
        await timeout.CancelAsync();
        await IgnoreExpectedShutdownAsync(slaveTask);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Empty(error.ToString());

        var logPath = Path.Combine(tempDirectory.Path, "logs", "mitmi.log");
        Assert.True(File.Exists(logPath));
        var fileLog = await File.ReadAllTextAsync(logPath);
        Assert.Contains("protocol.transaction_matched", fileLog);
        Assert.Contains("operation=readHoldingRegisters", fileLog);
        Assert.Contains("address=0", fileLog);
        Assert.Contains("quantity=2", fileLog);
        Assert.Contains("values=1111,2222", fileLog);
        Assert.Contains("protocol.analyzer_summary", fileLog);
        Assert.Contains("address_range=0-1", fileLog);
        Assert.Contains("metrics.session_summary", fileLog);

        var captureFile = Assert.Single(Directory.GetFiles(
            Path.Combine(tempDirectory.Path, "captures"),
            "mitmi-capture-*.ndjson"));
        var captureLines = await File.ReadAllLinesAsync(captureFile);
        Assert.True(captureLines.Length >= 2);

        var captureDocuments = captureLines.Select(line => JsonNode.Parse(line)!.AsObject()).ToArray();
        Assert.Contains(captureDocuments, document => document["direction"]!.GetValue<string>() == "clientToServer");
        Assert.Contains(captureDocuments, document => document["direction"]!.GetValue<string>() == "serverToClient");
        Assert.Contains(captureDocuments, document => document["kind"]!.GetValue<string>() == "trafficChunk");
        var protocolFrame = captureDocuments.FirstOrDefault(document =>
            document["kind"]!.GetValue<string>() == "protocolFrame" &&
            document["direction"]!.GetValue<string>() == "serverToClient");
        Assert.NotNull(protocolFrame);
        Assert.False(string.IsNullOrWhiteSpace(protocolFrame!["correlationId"]!.GetValue<string>()));
        var protocolMetadata = protocolFrame["protocolMetadata"]!.AsObject();
        Assert.Equal("3", protocolMetadata["functionCode"]!.GetValue<string>());
        Assert.Equal("responseMatched", protocolMetadata["transactionEventKind"]!.GetValue<string>());
        Assert.Equal("readHoldingRegisters", protocolMetadata["operation"]!.GetValue<string>());
        Assert.Equal("zeroBasedPdu", protocolMetadata["addressBase"]!.GetValue<string>());
        Assert.Equal("0", protocolMetadata["address"]!.GetValue<string>());
        Assert.Equal("2", protocolMetadata["quantity"]!.GetValue<string>());
        Assert.Equal("0-1", protocolMetadata["addressRange"]!.GetValue<string>());
        Assert.Equal("1111,2222", protocolMetadata["valuesHex"]!.GetValue<string>());
        Assert.All(captureDocuments, document =>
        {
            Assert.Equal(1, document["captureFormatVersion"]!.GetValue<int>());
            Assert.Equal("modbus-tcp", document["protocolId"]!.GetValue<string>());
            Assert.False(string.IsNullOrWhiteSpace(document["kind"]!.GetValue<string>()));
            Assert.True(document["payloadLength"]!.GetValue<int>() > 0);
            Assert.True(document.ContainsKey("rawPayloadBase64"));
        });

        var summaryFile = Assert.Single(Directory.GetFiles(
            Path.Combine(tempDirectory.Path, "captures", "summaries"),
            "mitmi-modbus-analyzer-summary-*.ndjson"));
        var summaryLines = await File.ReadAllLinesAsync(summaryFile);
        var summaryDocuments = summaryLines.Select(line => JsonNode.Parse(line)!.AsObject()).ToArray();
        var holdingRegisterSummary = Assert.Single(summaryDocuments, document =>
            document["operation"]!.GetValue<string>() == "readHoldingRegisters" &&
            document["address"]!.GetValue<int>() == 0 &&
            document["quantity"]!.GetValue<int>() == 2);
        Assert.Equal(1, holdingRegisterSummary["summaryFormatVersion"]!.GetValue<int>());
        Assert.Equal("host-runtime", holdingRegisterSummary["sessionId"]!.GetValue<string>());
        Assert.Equal(1, holdingRegisterSummary["unitId"]!.GetValue<int>());
        Assert.Equal(3, holdingRegisterSummary["functionCode"]!.GetValue<int>());
        Assert.Equal("0-1", holdingRegisterSummary["addressRange"]!.GetValue<string>());
        Assert.Equal("zeroBasedPdu", holdingRegisterSummary["addressBase"]!.GetValue<string>());
        Assert.Equal(1, holdingRegisterSummary["reads"]!.GetValue<int>());
        Assert.Equal(0, holdingRegisterSummary["writes"]!.GetValue<int>());
        Assert.Equal(1, holdingRegisterSummary["requests"]!.GetValue<int>());
        Assert.Equal(1, holdingRegisterSummary["responses"]!.GetValue<int>());
        Assert.Equal(0, holdingRegisterSummary["exceptions"]!.GetValue<int>());

        var discoveryReportFile = Assert.Single(Directory.GetFiles(
            Path.Combine(tempDirectory.Path, "captures", "reports"),
            "mitmi-modbus-device-discovery-*.md"));
        var discoveryReport = await File.ReadAllTextAsync(discoveryReportFile);
        Assert.Contains("# MITMI Modbus Device Discovery Report", discoveryReport);
        Assert.Contains($"- Upstream device: 127.0.0.1:{upstreamPort}", discoveryReport);
        Assert.Contains("| 1 | 3 | readHoldingRegisters | 0-1 | 2 | 1 | 0 | 1 | 1 | 0 |", discoveryReport);
        Assert.Contains("PDU address ranges are zero-based Modbus Protocol Data Unit offsets.", discoveryReport);

        var hostOutput = output.ToString();
        Assert.Contains("Writing capture records to", hostOutput);
        Assert.Contains("Writing Modbus analyzer summary to", hostOutput);
        Assert.Contains("Writing Modbus discovery report to", hostOutput);
    }

    [Fact]
    public async Task RunAsync_posts_observed_value_webhook_without_interrupting_forwarding()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var tempDirectory = new TemporaryDirectory();
        using var output = new StringWriter();
        using var error = new StringWriter();
        await using var webhookServer = new RecordingHttpServer(timeout.Token);
        var factory = new ModbusFactory();
        using var upstreamListener = new TcpListener(IPAddress.Loopback, 0);
        upstreamListener.Start();
        var upstreamPort = ((IPEndPoint)upstreamListener.LocalEndpoint).Port;
        var listenPort = ReserveTcpPort();
        var dataStore = new DefaultSlaveDataStore();
        dataStore.HoldingRegisters.WritePoints(0, [0x1111, 0x2222]);

        var slaveNetwork = factory.CreateSlaveNetwork(upstreamListener);
        slaveNetwork.AddSlave(factory.CreateSlave(1, dataStore));
        var slaveTask = slaveNetwork.ListenAsync(timeout.Token);

        var configPath = Path.Combine(tempDirectory.Path, "mitmi.config.json");
        await File.WriteAllTextAsync(
            configPath,
            RuntimeConfigurationJson(
                listenPort,
                upstreamPort,
                IntegrationsJson(webhookServer.Url)));

        using var hostCancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var hostTask = CommandLineHost.RunAsync(
            ["--config", configPath],
            output,
            error,
            hostCancellation.Token,
            applicationDirectory: tempDirectory.Path);

        using var client = await ConnectWithRetryAsync(IPAddress.Loopback, listenPort, timeout.Token);
        var master = factory.CreateMaster(client);
        var registers = await master.ReadHoldingRegistersAsync(1, 0, 2);
        var webhookRequest = await webhookServer.WaitForRequestAsync(timeout.Token);

        Assert.Equal([0x1111, 0x2222], registers);

        client.Close();
        await hostCancellation.CancelAsync();
        var exitCode = await hostTask.WaitAsync(timeout.Token);
        await timeout.CancelAsync();
        await IgnoreExpectedShutdownAsync(slaveTask);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Empty(error.ToString());
        Assert.StartsWith("POST /mitmi/observed-values HTTP/", webhookRequest.RequestLine, StringComparison.Ordinal);

        var payload = JsonNode.Parse(webhookRequest.Body)!.AsObject();
        Assert.Equal(1, payload["payloadSchemaVersion"]!.GetValue<int>());
        Assert.Equal("host-runtime", payload["sessionId"]!.GetValue<string>());
        Assert.Equal($"127.0.0.1:{upstreamPort}", payload["upstreamEndpoint"]!.GetValue<string>());
        Assert.Equal(1, payload["unitId"]!.GetValue<int>());
        Assert.Equal(3, payload["functionCode"]!.GetValue<int>());
        Assert.Equal("readHoldingRegisters", payload["operation"]!.GetValue<string>());
        Assert.Equal("holdingRegisters", payload["table"]!.GetValue<string>());
        Assert.Equal("0-1", payload["requestedAddressRange"]!.GetValue<string>());
        Assert.Equal(2, payload["observedCells"]!.AsArray().Count);
        Assert.Equal(2, payload["changedCells"]!.AsArray().Count);

        var firstChangedCell = payload["changedCells"]!.AsArray()[0]!.AsObject();
        Assert.Equal(0, firstChangedCell["address"]!.GetValue<int>());
        Assert.Equal(0x1111, firstChangedCell["currentValue"]!.GetValue<int>());
        Assert.Equal("1111", firstChangedCell["currentValueHex"]!.GetValue<string>());

        var fileLog = await File.ReadAllTextAsync(Path.Combine(tempDirectory.Path, "logs", "mitmi.log"));
        Assert.Contains("integration.observed_value_webhook.summary", fileLog);
        Assert.Contains("delivered=1", fileLog);
        Assert.Contains("failed=0", fileLog);
        Assert.Contains("dropped=0", fileLog);
    }

    private static async Task<TcpClient> ConnectWithRetryAsync(
        IPAddress address,
        int port,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        Exception? lastException = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var client = new TcpClient();
            try
            {
                await client.ConnectAsync(address, port, cancellationToken);
                return client;
            }
            catch (Exception exception) when (exception is SocketException or IOException)
            {
                lastException = exception;
                client.Dispose();
                await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
            }
        }

        throw new TimeoutException($"Timed out connecting to {address}:{port}.", lastException);
    }

    private static int ReserveTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task IgnoreExpectedShutdownAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception exception) when (
            exception is OperationCanceledException ||
            exception is ObjectDisposedException ||
            exception is SocketException)
        {
        }
    }

    private static string RuntimeConfigurationJson(
        int listenPort,
        int upstreamPort,
        string integrationsJson = "")
    {
        return $$"""
            {
              "configurationVersion": 1,
              {{integrationsJson}}
              "logging": {
                "console": {
                  "enabled": true,
                  "minimumLevel": "Info"
                },
                "file": {
                  "enabled": true,
                  "minimumLevel": "Info",
                  "path": "./logs/mitmi.log"
                }
              },
              "capture": {
                "enabled": true,
                "outputPath": "./captures",
                "retention": {
                  "mode": "Manual"
                }
              },
              "metrics": {
                "enabled": true,
                "sink": "Log"
              },
              "session": {
                "id": "host-runtime",
                "protocol": "modbus-tcp",
                "listenEndpoint": {
                  "address": "127.0.0.1",
                  "port": {{listenPort}}
                },
                "upstreamEndpoint": {
                  "address": "127.0.0.1",
                  "port": {{upstreamPort}}
                },
                "diagnostics": {
                  "decodeProtocol": true,
                  "captureRawPayloads": true
                }
              }
            }
            """;
    }

    private static string IntegrationsJson(string webhookUrl)
    {
        return $$"""
              "integrations": {
                "observedValueWebhook": {
                  "enabled": true,
                  "url": "{{webhookUrl}}",
                  "trigger": {
                    "mode": "ChangedCellsOnly",
                    "ranges": [
                      {
                        "unitId": 1,
                        "table": "holdingRegisters",
                        "startAddress": 0,
                        "endAddress": 1
                      }
                    ]
                  },
                  "delivery": {
                    "timeoutMilliseconds": 1000,
                    "queueCapacity": 16
                  },
                  "authentication": {
                    "mode": "None"
                  }
                }
              },
        """;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mitmi-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    private sealed class RecordingHttpServer : IAsyncDisposable
    {
        private readonly TcpListener listener;
        private readonly Task<RecordedHttpRequest> requestTask;

        public RecordingHttpServer(CancellationToken cancellationToken)
        {
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Url = $"http://127.0.0.1:{port}/mitmi/observed-values";
            requestTask = AcceptOneRequestAsync(cancellationToken);
        }

        public string Url { get; }

        public async Task<RecordedHttpRequest> WaitForRequestAsync(CancellationToken cancellationToken)
        {
            return await requestTask.WaitAsync(cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            listener.Stop();
            try
            {
                await requestTask;
            }
            catch (Exception exception) when (
                exception is OperationCanceledException ||
                exception is ObjectDisposedException ||
                exception is SocketException ||
                exception is IOException)
            {
            }
        }

        private async Task<RecordedHttpRequest> AcceptOneRequestAsync(CancellationToken cancellationToken)
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken);
            var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);

            var requestLine = await reader.ReadLineAsync(cancellationToken) ?? string.Empty;
            var contentLength = 0;
            var isChunked = false;
            string? headerLine;
            while (!string.IsNullOrEmpty(headerLine = await reader.ReadLineAsync(cancellationToken)))
            {
                var separator = headerLine.IndexOf(':', StringComparison.Ordinal);
                if (separator < 0)
                {
                    continue;
                }

                var name = headerLine[..separator].Trim();
                var value = headerLine[(separator + 1)..].Trim();
                if (string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(value, out var parsedContentLength))
                {
                    contentLength = parsedContentLength;
                }

                if (string.Equals(name, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase) &&
                    value.Contains("chunked", StringComparison.OrdinalIgnoreCase))
                {
                    isChunked = true;
                }
            }

            var body = isChunked
                ? await ReadChunkedBodyAsync(reader, cancellationToken)
                : await ReadFixedLengthBodyAsync(reader, contentLength, cancellationToken);

            var response = Encoding.ASCII.GetBytes(
                "HTTP/1.1 204 No Content\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(response, cancellationToken);
            return new RecordedHttpRequest(requestLine, body);
        }

        private static async Task<string> ReadFixedLengthBodyAsync(
            TextReader reader,
            int contentLength,
            CancellationToken cancellationToken)
        {
            var buffer = new char[contentLength];
            var read = 0;
            while (read < contentLength)
            {
                var currentRead = await reader.ReadAsync(
                    buffer.AsMemory(read, contentLength - read),
                    cancellationToken);
                if (currentRead == 0)
                {
                    break;
                }

                read += currentRead;
            }

            return new string(buffer, 0, read);
        }

        private static async Task<string> ReadChunkedBodyAsync(
            TextReader reader,
            CancellationToken cancellationToken)
        {
            var builder = new StringBuilder();
            while (true)
            {
                var chunkSizeLine = await reader.ReadLineAsync(cancellationToken) ?? "0";
                var extensionSeparator = chunkSizeLine.IndexOf(';', StringComparison.Ordinal);
                var chunkSizeText = extensionSeparator < 0
                    ? chunkSizeLine
                    : chunkSizeLine[..extensionSeparator];
                var chunkSize = Convert.ToInt32(chunkSizeText, 16);
                if (chunkSize == 0)
                {
                    await reader.ReadLineAsync(cancellationToken);
                    break;
                }

                var buffer = new char[chunkSize];
                var read = 0;
                while (read < chunkSize)
                {
                    var currentRead = await reader.ReadAsync(
                        buffer.AsMemory(read, chunkSize - read),
                        cancellationToken);
                    if (currentRead == 0)
                    {
                        break;
                    }

                    read += currentRead;
                }

                builder.Append(buffer, 0, read);
                await reader.ReadLineAsync(cancellationToken);
            }

            return builder.ToString();
        }
    }

    private sealed record RecordedHttpRequest(
        string RequestLine,
        string Body);
}
