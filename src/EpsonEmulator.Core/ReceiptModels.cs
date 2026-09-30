using System;
using System.Collections.Generic;

namespace EpsonEmulator.Core
{
    public sealed class ReceiptJob
    {
        public string SchemaVersion { get; set; } = "1.0";
        public string JobId { get; set; } = Guid.NewGuid().ToString("N");
        public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.Now;
        public string SourceAddress { get; set; } = "";
        public int SourcePort { get; set; }
        public int ByteCount { get; set; }
        public string Status { get; set; } = "Completed";
        public string CompletionReason { get; set; } = "ConnectionClosed";
        public string RawBytesBase64 { get; set; } = "";
        public string SavedJsonPath { get; set; } = "";
        public List<ReceiptElement> Elements { get; set; } = new List<ReceiptElement>();
        public List<PrinterEvent> Events { get; set; } = new List<PrinterEvent>();
        public List<CommandDiagnostic> Diagnostics { get; set; } = new List<CommandDiagnostic>();
    }

    public sealed class ReceiptElement
    {
        public string Type { get; set; } = "Line";
        public string Alignment { get; set; } = "Left";
        public List<TextSpan> Spans { get; set; } = new List<TextSpan>();
    }

    public sealed class TextSpan
    {
        public string Text { get; set; } = "";
        public TextStyle Style { get; set; } = new TextStyle();
    }

    public sealed class TextStyle
    {
        public bool Bold { get; set; }
        public bool Underline { get; set; }
        public string Font { get; set; } = "A";
        public int WidthMultiplier { get; set; } = 1;
        public int HeightMultiplier { get; set; } = 1;
        public string CodePage { get; set; } = "PC437";

        public TextStyle Clone()
        {
            return new TextStyle
            {
                Bold = Bold,
                Underline = Underline,
                Font = Font,
                WidthMultiplier = WidthMultiplier,
                HeightMultiplier = HeightMultiplier,
                CodePage = CodePage
            };
        }
    }

    public sealed class PrinterEvent
    {
        public string Type { get; set; } = "";
        public int Offset { get; set; }
        public string Name { get; set; } = "";
        public string Data { get; set; } = "";
        public Dictionary<string, string> Properties { get; set; } = new Dictionary<string, string>();
    }

    public sealed class CommandDiagnostic
    {
        public int Offset { get; set; }
        public string Severity { get; set; } = "Info";
        public string Command { get; set; } = "";
        public string Message { get; set; } = "";
        public string BytesHex { get; set; } = "";
    }
}
