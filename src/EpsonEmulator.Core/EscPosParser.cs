using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace EpsonEmulator.Core
{
    public sealed class EscPosParser
    {
        private readonly List<ReceiptElement> _elements = new List<ReceiptElement>();
        private readonly List<PrinterEvent> _events = new List<PrinterEvent>();
        private readonly List<CommandDiagnostic> _diagnostics = new List<CommandDiagnostic>();
        private readonly List<TextSpan> _currentSpans = new List<TextSpan>();
        private TextStyle _style = new TextStyle();
        private string _alignment = "Left";

        public ReceiptJob Parse(byte[] bytes, string sourceAddress, int sourcePort, string completionReason)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));

            ResetState();
            var i = 0;
            while (i < bytes.Length)
            {
                var b = bytes[i];
                if (b == 0x1B)
                {
                    i = ParseEsc(bytes, i);
                }
                else if (b == 0x1D)
                {
                    i = ParseGs(bytes, i);
                }
                else if (b == 0x0A)
                {
                    FlushLine();
                    i++;
                }
                else if (b == 0x0D)
                {
                    i++;
                }
                else if (b == 0x0C)
                {
                    FlushLine();
                    AddEvent("JobBoundary", i, "FormFeed", "");
                    i++;
                }
                else if (b >= 0x20 || b == 0x09)
                {
                    var start = i;
                    while (i < bytes.Length && (bytes[i] >= 0x20 || bytes[i] == 0x09) && bytes[i] != 0x1B && bytes[i] != 0x1D)
                    {
                        i++;
                    }
                    AddText(DecodeText(bytes, start, i - start));
                }
                else
                {
                    AddDiagnostic(i, "Control", "Ignored control byte.", bytes, i, 1);
                    i++;
                }
            }

            FlushLine();
            return new ReceiptJob
            {
                ReceivedAt = DateTimeOffset.Now,
                SourceAddress = sourceAddress ?? "",
                SourcePort = sourcePort,
                ByteCount = bytes.Length,
                Status = _diagnostics.Any(d => d.Severity == "Error") ? "CompletedWithErrors" : "Completed",
                CompletionReason = completionReason ?? "ConnectionClosed",
                RawBytesBase64 = Convert.ToBase64String(bytes),
                Elements = _elements,
                Events = _events,
                Diagnostics = _diagnostics
            };
        }

        private void ResetState()
        {
            _elements.Clear();
            _events.Clear();
            _diagnostics.Clear();
            _currentSpans.Clear();
            _style = new TextStyle();
            _alignment = "Left";
        }

        private int ParseEsc(byte[] bytes, int offset)
        {
            if (offset + 1 >= bytes.Length)
            {
                AddDiagnostic(offset, "ESC", "Truncated ESC command.", bytes, offset, 1);
                return bytes.Length;
            }

            var command = bytes[offset + 1];
            switch (command)
            {
                case 0x40:
                    FlushLine();
                    _style = new TextStyle();
                    _alignment = "Left";
                    AddEvent("Command", offset, "Initialize", "");
                    return offset + 2;
                case 0x61:
                    if (!Has(bytes, offset, 3)) return Truncated(bytes, offset, "ESC a");
                    _alignment = AlignmentName(bytes[offset + 2]);
                    AddEvent("Command", offset, "Alignment", _alignment);
                    return offset + 3;
                case 0x45:
                    if (!Has(bytes, offset, 3)) return Truncated(bytes, offset, "ESC E");
                    _style.Bold = (bytes[offset + 2] & 0x01) == 1;
                    AddEvent("Command", offset, "Bold", _style.Bold ? "On" : "Off");
                    return offset + 3;
                case 0x2D:
                    if (!Has(bytes, offset, 3)) return Truncated(bytes, offset, "ESC -");
                    _style.Underline = bytes[offset + 2] != 0;
                    AddEvent("Command", offset, "Underline", _style.Underline ? "On" : "Off");
                    return offset + 3;
                case 0x4D:
                    if (!Has(bytes, offset, 3)) return Truncated(bytes, offset, "ESC M");
                    _style.Font = bytes[offset + 2] == 1 ? "B" : "A";
                    AddEvent("Command", offset, "Font", _style.Font);
                    return offset + 3;
                case 0x74:
                    if (!Has(bytes, offset, 3)) return Truncated(bytes, offset, "ESC t");
                    _style.CodePage = CodePageName(bytes[offset + 2]);
                    AddEvent("Command", offset, "CodePage", _style.CodePage);
                    return offset + 3;
                case 0x69:
                case 0x6D:
                    FlushLine();
                    AddEvent("Cut", offset, command == 0x69 ? "FullCut" : "PartialCut", "");
                    return offset + 2;
                default:
                    AddDiagnostic(offset, "ESC", "Unknown or unsupported ESC command.", bytes, offset, Math.Min(2, bytes.Length - offset));
                    return offset + 2;
            }
        }

        private int ParseGs(byte[] bytes, int offset)
        {
            if (offset + 1 >= bytes.Length)
            {
                AddDiagnostic(offset, "GS", "Truncated GS command.", bytes, offset, 1);
                return bytes.Length;
            }

            var command = bytes[offset + 1];
            switch (command)
            {
                case 0x21:
                    if (!Has(bytes, offset, 3)) return Truncated(bytes, offset, "GS !");
                    _style.WidthMultiplier = ((bytes[offset + 2] >> 4) & 0x07) + 1;
                    _style.HeightMultiplier = (bytes[offset + 2] & 0x07) + 1;
                    AddEvent("Command", offset, "TextScale", _style.WidthMultiplier + "x" + _style.HeightMultiplier);
                    return offset + 3;
                case 0x56:
                    FlushLine();
                    var cutBytes = Has(bytes, offset, 3) ? 3 : 2;
                    AddEvent("Cut", offset, "PaperCut", "");
                    return offset + cutBytes;
                case 0x6B:
                    return ParseBarcode(bytes, offset);
                case 0x28:
                    return ParseGsParen(bytes, offset);
                default:
                    AddDiagnostic(offset, "GS", "Unknown or unsupported GS command.", bytes, offset, Math.Min(2, bytes.Length - offset));
                    return offset + 2;
            }
        }

        private int ParseBarcode(byte[] bytes, int offset)
        {
            if (!Has(bytes, offset, 3)) return Truncated(bytes, offset, "GS k");
            var system = bytes[offset + 2];
            if (system <= 6)
            {
                var end = offset + 3;
                while (end < bytes.Length && bytes[end] != 0) end++;
                if (end >= bytes.Length) return Truncated(bytes, offset, "GS k barcode");
                var data = DecodeText(bytes, offset + 3, end - offset - 3);
                FlushLine();
                AddPlaceholder("Barcode", BarcodeName(system), data);
                AddEvent("Barcode", offset, BarcodeName(system), data);
                return end + 1;
            }
            else
            {
                if (!Has(bytes, offset, 4)) return Truncated(bytes, offset, "GS k barcode");
                var length = bytes[offset + 3];
                if (!Has(bytes, offset, 4 + length)) return Truncated(bytes, offset, "GS k barcode payload");
                var data = DecodeText(bytes, offset + 4, length);
                FlushLine();
                AddPlaceholder("Barcode", BarcodeName(system), data);
                AddEvent("Barcode", offset, BarcodeName(system), data);
                return offset + 4 + length;
            }
        }

        private int ParseGsParen(byte[] bytes, int offset)
        {
            if (!Has(bytes, offset, 5)) return Truncated(bytes, offset, "GS (");
            var fn = bytes[offset + 2];
            var pL = bytes[offset + 3];
            var pH = bytes[offset + 4];
            var length = pL + (pH * 256);
            if (!Has(bytes, offset, 5 + length)) return Truncated(bytes, offset, "GS ( payload");

            if (fn == 0x6B)
            {
                var payload = bytes.Skip(offset + 5).Take(length).ToArray();
                var data = DecodeText(payload, 0, payload.Length);
                AddEvent("QrCode", offset, "GS(k", data);
                if (payload.Length >= 3 && payload[0] == 49 && payload[1] == 81)
                {
                    FlushLine();
                    AddPlaceholder("QrCode", "QR Print", "");
                }
                else if (payload.Length > 3 && payload[0] == 49 && payload[1] == 80)
                {
                    FlushLine();
                    AddPlaceholder("QrCode", "QR Data", DecodeText(payload, 3, payload.Length - 3));
                }
            }
            else
            {
                AddDiagnostic(offset, "GS (", "Unsupported GS parenthesized command.", bytes, offset, Math.Min(5 + length, bytes.Length - offset));
            }

            return offset + 5 + length;
        }

        private void AddText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var last = _currentSpans.LastOrDefault();
            if (last != null && SameStyle(last.Style, _style))
            {
                last.Text += text;
            }
            else
            {
                _currentSpans.Add(new TextSpan { Text = text, Style = _style.Clone() });
            }
        }

        private void FlushLine()
        {
            if (_currentSpans.Count == 0) return;
            _elements.Add(new ReceiptElement
            {
                Type = "Line",
                Alignment = _alignment,
                Spans = _currentSpans.Select(s => new TextSpan { Text = s.Text, Style = s.Style.Clone() }).ToList()
            });
            _currentSpans.Clear();
        }

        private void AddPlaceholder(string type, string name, string data)
        {
            _elements.Add(new ReceiptElement
            {
                Type = type,
                Alignment = _alignment,
                Spans = new List<TextSpan>
                {
                    new TextSpan { Text = "[" + name + (string.IsNullOrEmpty(data) ? "" : ": " + data) + "]", Style = _style.Clone() }
                }
            });
        }

        private void AddEvent(string type, int offset, string name, string data)
        {
            _events.Add(new PrinterEvent { Type = type, Offset = offset, Name = name, Data = data ?? "" });
        }

        private void AddDiagnostic(int offset, string command, string message, byte[] bytes, int start, int count)
        {
            _diagnostics.Add(new CommandDiagnostic
            {
                Offset = offset,
                Command = command,
                Message = message,
                BytesHex = BitConverter.ToString(bytes, start, Math.Max(0, count)).Replace("-", " ")
            });
        }

        private int Truncated(byte[] bytes, int offset, string command)
        {
            AddDiagnostic(offset, command, "Truncated command payload.", bytes, offset, bytes.Length - offset);
            return bytes.Length;
        }

        private static bool Has(byte[] bytes, int offset, int count)
        {
            return offset + count <= bytes.Length;
        }

        private static bool SameStyle(TextStyle left, TextStyle right)
        {
            return left.Bold == right.Bold &&
                   left.Underline == right.Underline &&
                   left.Font == right.Font &&
                   left.WidthMultiplier == right.WidthMultiplier &&
                   left.HeightMultiplier == right.HeightMultiplier &&
                   left.CodePage == right.CodePage;
        }

        private static string DecodeText(byte[] bytes, int offset, int count)
        {
            var builder = new StringBuilder(count);
            for (var i = offset; i < offset + count; i++)
            {
                var b = bytes[i];
                if (b == 0x09) builder.Append("    ");
                else if (b >= 0x20 && b <= 0x7E) builder.Append((char)b);
                else builder.Append(Cp437Fallback(b));
            }
            return builder.ToString();
        }

        private static char Cp437Fallback(byte b)
        {
            const string table = "ÇüéâäàåçêëèïîìÄÅÉæÆôöòûùÿÖÜ¢£¥₧ƒáíóúñÑªº¿⌐¬½¼¡«»";
            if (b >= 0x80 && b < 0x80 + table.Length) return table[b - 0x80];
            return '?';
        }

        private static string AlignmentName(byte value)
        {
            if (value == 1) return "Center";
            if (value == 2) return "Right";
            return "Left";
        }

        private static string CodePageName(byte value)
        {
            switch (value)
            {
                case 0: return "PC437";
                case 2: return "PC850";
                case 16: return "WPC1252";
                case 17: return "PC866";
                case 19: return "PC858";
                default: return "CodePage-" + value;
            }
        }

        private static string BarcodeName(byte value)
        {
            switch (value)
            {
                case 0:
                case 65: return "UPC-A";
                case 1:
                case 66: return "UPC-E";
                case 2:
                case 67: return "EAN13";
                case 3:
                case 68: return "EAN8";
                case 4:
                case 69: return "CODE39";
                case 5:
                case 70: return "ITF";
                case 6:
                case 71: return "CODABAR";
                case 72: return "CODE93";
                case 73: return "CODE128";
                default: return "Barcode-" + value;
            }
        }
    }
}
