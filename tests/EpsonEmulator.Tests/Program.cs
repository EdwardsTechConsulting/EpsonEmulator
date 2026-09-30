using EpsonEmulator.Core;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EpsonEmulator.Tests
{
    internal static class Program
    {
        private static int _failures;

        private static async Task<int> Main()
        {
            ParserCapturesTextStylesAndEvents();
            JsonWriterPersistsStableSchemaAndRawBytes();
            BindAddressParserValidatesConfiguredAddress();
            await TcpServerReceivesRepresentativeJob();

            if (_failures == 0)
            {
                Console.WriteLine("All tests passed.");
                return 0;
            }

            Console.Error.WriteLine(_failures + " test(s) failed.");
            return 1;
        }

        private static void ParserCapturesTextStylesAndEvents()
        {
            var bytes = new byte[]
            {
                0x1B, 0x40,
                0x1B, 0x61, 0x01
            }
            .Concat(Encoding.ASCII.GetBytes("STORE"))
            .Concat(new byte[] { 0x0A, 0x1B, 0x45, 0x01 })
            .Concat(Encoding.ASCII.GetBytes("TOTAL $12.34"))
            .Concat(new byte[] { 0x0A, 0x1D, 0x21, 0x11 })
            .Concat(Encoding.ASCII.GetBytes("THANK YOU"))
            .Concat(new byte[] { 0x0A, 0x1D, 0x6B, 0x04 })
            .Concat(Encoding.ASCII.GetBytes("ABC123"))
            .Concat(new byte[] { 0x00, 0x1D, 0x56, 0x00 })
            .ToArray();

            var job = new EscPosParser().Parse(bytes, "127.0.0.1", 9100, "Test");

            Assert(job.Elements.Count >= 4, "parser creates lines and barcode element");
            Assert(job.Elements[0].Alignment == "Center", "alignment is captured");
            Assert(job.Elements[1].Spans[0].Style.Bold, "bold style span is captured");
            Assert(job.Elements[2].Spans[0].Style.WidthMultiplier == 2, "text scale width is captured");
            Assert(job.Events.Any(e => e.Type == "Barcode" && e.Data == "ABC123"), "barcode event is captured");
            Assert(job.Events.Any(e => e.Type == "Cut"), "cut event is captured");
            Assert(job.RawBytesBase64 == Convert.ToBase64String(bytes), "raw bytes are preserved");
        }

        private static void JsonWriterPersistsStableSchemaAndRawBytes()
        {
            var dir = Path.Combine(Path.GetTempPath(), "EscPosJsonTests", Guid.NewGuid().ToString("N"));
            var bytes = Encoding.ASCII.GetBytes("A\n");
            var job = new EscPosParser().Parse(bytes, "10.0.0.5", 12345, "ConnectionClosed");
            var path = new JsonJobWriter(dir).Save(job);

            Assert(File.Exists(path), "json file exists");
            using (var doc = JsonDocument.Parse(File.ReadAllText(path)))
            {
                var root = doc.RootElement;
                Assert(root.GetProperty("SchemaVersion").GetString() == "1.0", "schema version is serialized");
                Assert(root.GetProperty("SourceAddress").GetString() == "10.0.0.5", "source metadata is serialized");
                Assert(root.GetProperty("RawBytesBase64").GetString() == Convert.ToBase64String(bytes), "raw base64 is serialized");
                Assert(root.GetProperty("Elements").GetArrayLength() == 1, "receipt elements are serialized");
                Assert(root.GetProperty("SavedJsonPath").GetString() == path, "saved json path is serialized");
            }
        }

        private static async Task TcpServerReceivesRepresentativeJob()
        {
            using (var server = new TcpPrinterServer())
            {
                var completion = new TaskCompletionSource<ReceiptJob>();
                server.JobReceived += (sender, job) => completion.TrySetResult(job);
                server.ListenerError += (sender, error) => completion.TrySetException(error);
                server.Start(IPAddress.Loopback, 0);
                var port = server.LocalEndpoint.Port;
                Assert(server.LocalEndpoint.Address.Equals(IPAddress.Loopback), "tcp server binds selected address");

                using (var client = new TcpClient())
                {
                    await client.ConnectAsync(IPAddress.Loopback, port);
                    var bytes = Encoding.ASCII.GetBytes("TCP TEST\n");
                    await client.GetStream().WriteAsync(bytes, 0, bytes.Length);
                }

                var finished = await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromSeconds(5)));
                Assert(finished == completion.Task, "tcp server receives a job");
                if (finished == completion.Task)
                {
                    var job = await completion.Task;
                    Assert(job.Elements[0].Spans[0].Text == "TCP TEST", "tcp job content is parsed");
                }
            }
        }

        private static void BindAddressParserValidatesConfiguredAddress()
        {
            Assert(BindAddressParser.Parse("all").Equals(IPAddress.Any), "all maps to IPv4 any");
            Assert(BindAddressParser.Parse("*").Equals(IPAddress.Any), "star maps to IPv4 any");
            Assert(BindAddressParser.Parse("::").Equals(IPAddress.IPv6Any), "IPv6 any is accepted");
            Assert(BindAddressParser.Parse("127.0.0.1").Equals(IPAddress.Loopback), "specific IPv4 is accepted");
            Assert(BindAddressParser.Parse("::1").Equals(IPAddress.IPv6Loopback), "specific IPv6 is accepted");

            var failed = false;
            try
            {
                BindAddressParser.Parse("not an address");
            }
            catch (FormatException)
            {
                failed = true;
            }
            Assert(failed, "invalid bind address is rejected");
        }

        private static void Assert(bool condition, string name)
        {
            if (condition)
            {
                Console.WriteLine("PASS " + name);
            }
            else
            {
                _failures++;
                Console.Error.WriteLine("FAIL " + name);
            }
        }
    }
}
