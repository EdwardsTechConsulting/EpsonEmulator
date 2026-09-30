# Epson Emulator

A local Windows desktop utility that accepts raw Epson-compatible ESC/POS print jobs over TCP/IP and renders received receipts for inspection.

## Run

```powershell
dotnet run --project .\src\EpsonEmulator.App\EpsonEmulator.App.csproj
```

The default listener endpoint is `0.0.0.0:9100`, the conventional raw printer socket port. If another process already owns that port, choose a different port in the app before starting the listener.

The bind address is configurable and persisted with the port and output directory. Use `all`, `*`, or `0.0.0.0` for all IPv4 interfaces, `::` for all IPv6 interfaces, or a specific local IPv4/IPv6 address such as `127.0.0.1` or `::1`. The app validates the value before starting and shows the effective listening endpoint.

## Build And Test

```powershell
.\Build-EpsonEmulator.ps1
```

## Command Coverage

Implemented practical core:

- `ESC @` initialize/reset style state
- `LF`, `CR`, `FF` line and job boundaries
- `ESC a n` alignment
- `ESC E n` bold on/off
- `ESC - n` underline on/off
- `ESC M n` font A/B selection
- `ESC t n` common code page selection metadata
- `GS ! n` width/height text scaling
- `GS V`, `ESC i`, `ESC m` paper cut/job completion
- `GS k` common barcode payload forms, rendered as readable placeholders
- `GS ( k` QR-code store/print commands, rendered as readable placeholders when enough data is present
- Unknown ESC/POS command diagnostics with byte offsets

Limitations:

- This is direct IP/TCP raw printer emulation, not an installed Windows printer driver.
- Code page decoding is intentionally conservative and maps printable ASCII plus a small CP437-compatible subset.
- Barcodes and QR codes are shown as placeholders with data rather than machine-scannable graphics.
- Raster images, cash drawer pulses, NV graphics, status backchannels, and vendor-specific commands are logged as unsupported/unknown diagnostics.

## JSON Output Schema

Each received job is written atomically as one collision-safe `.json` file in the configured output directory. The top-level schema is intentionally stable and uses `SchemaVersion: "1.0"`.

- `JobId`, `ReceivedAt`, `SourceAddress`, `SourcePort`, `ByteCount`, `Status`, and `CompletionReason` describe the job.
- `RawBytesBase64` preserves the exact input bytes safely.
- `SavedJsonPath` records where the JSON file was written.
- `Elements` is the ordered receipt content. Line elements contain `Alignment` and ordered `Spans`; each span includes text and a style snapshot.
- `Events` contains recognized printer events such as initialize, alignment, barcode, QR, and paper cut commands.
- `Diagnostics` contains unknown or unsupported commands with byte offsets and hex bytes.
