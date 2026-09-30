using EpsonEmulator.Core;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Windows.Forms;

namespace EpsonEmulator.App
{
    public sealed class MainForm : Form
    {
        private readonly TcpPrinterServer _server = new TcpPrinterServer();
        private readonly ListBox _history = new ListBox();
        private readonly RichTextBox _preview = new RichTextBox();
        private readonly TextBox _bindAddress = new TextBox();
        private readonly NumericUpDown _port = new NumericUpDown();
        private readonly TextBox _outputDirectory = new TextBox();
        private readonly Label _status = new Label();
        private readonly TextBox _details = new TextBox();
        private readonly Button _start = new Button();
        private readonly Button _stop = new Button();
        private readonly Button _browse = new Button();
        private readonly Button _openJson = new Button();
        private readonly AppSettings _settings;
        private ReceiptJob _selectedJob;

        public MainForm()
        {
            _settings = AppSettings.Load();
            Text = "Epson Emulator";
            Width = 1180;
            Height = 760;
            MinimumSize = new Size(940, 620);

            _bindAddress.Text = _settings.BindAddress;
            _port.Minimum = 1;
            _port.Maximum = 65535;
            _port.Value = Math.Max((int)_port.Minimum, Math.Min((int)_port.Maximum, _settings.Port));
            _outputDirectory.Text = _settings.OutputDirectory;

            BuildLayout();
            WireEvents();
            UpdateControls();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SaveSettings();
            _server.Dispose();
            base.OnFormClosing(e);
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(10) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            Controls.Add(root);

            var top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 8, RowCount = 2 };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 55));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            top.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            top.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
            root.Controls.Add(top, 0, 0);

            top.Controls.Add(new Label { Text = "Bind", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 0);
            top.Controls.Add(_bindAddress, 1, 0);
            top.Controls.Add(new Label { Text = "Port", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 2, 0);
            top.Controls.Add(_port, 3, 0);
            _start.Text = "Start";
            _stop.Text = "Stop";
            top.Controls.Add(_start, 5, 0);
            top.Controls.Add(_stop, 6, 0);
            _openJson.Text = "Open JSON";
            top.Controls.Add(_openJson, 7, 0);

            top.Controls.Add(new Label { Text = "Output", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 1);
            _outputDirectory.Dock = DockStyle.Fill;
            top.SetColumnSpan(_outputDirectory, 4);
            top.Controls.Add(_outputDirectory, 1, 1);
            _browse.Text = "Browse";
            top.Controls.Add(_browse, 5, 1);

            _details.Multiline = true;
            _details.ReadOnly = true;
            _details.ScrollBars = ScrollBars.Vertical;
            top.SetColumnSpan(_details, 2);
            top.Controls.Add(_details, 6, 1);

            var body = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 330 };
            root.Controls.Add(body, 0, 1);

            _history.Dock = DockStyle.Fill;
            _history.Font = new Font(FontFamily.GenericMonospace, 9);
            body.Panel1.Controls.Add(_history);

            _preview.Dock = DockStyle.Fill;
            _preview.ReadOnly = true;
            _preview.Font = new Font("Consolas", 11);
            _preview.BackColor = Color.White;
            body.Panel2.Controls.Add(_preview);

            _status.Dock = DockStyle.Fill;
            _status.TextAlign = ContentAlignment.MiddleLeft;
            root.Controls.Add(_status, 0, 2);
        }

        private void WireEvents()
        {
            _start.Click += (sender, args) => StartListener();
            _stop.Click += (sender, args) => StopListener();
            _browse.Click += (sender, args) => BrowseOutput();
            _openJson.Click += (sender, args) => OpenSelectedJson();
            _history.SelectedIndexChanged += (sender, args) => ShowSelectedJob();

            _server.StatusChanged += (sender, message) => BeginInvoke((Action)(() => _status.Text = message));
            _server.ListenerError += (sender, error) => BeginInvoke((Action)(() =>
            {
                _status.Text = "Error: " + error.Message;
                MessageBox.Show(this, error.Message, "Listener Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }));
            _server.JobReceived += (sender, job) => BeginInvoke((Action)(() => SaveAndAddJob(job)));
        }

        private void StartListener()
        {
            try
            {
                var address = BindAddressParser.Parse(_bindAddress.Text);
                _server.Start(address, (int)_port.Value);
                SaveSettings();
                _status.Text = "Listening on " + BindAddressParser.Display(_server.LocalEndpoint.Address) + ":" + _server.LocalEndpoint.Port;
                UpdateControls();
            }
            catch (FormatException ex)
            {
                MessageBox.Show(this, ex.Message, "Invalid Bind Address", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Unable To Start Listener", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void StopListener()
        {
            _server.Stop();
            UpdateControls();
        }

        private void SaveAndAddJob(ReceiptJob job)
        {
            try
            {
                var path = new JsonJobWriter(_outputDirectory.Text.Trim()).Save(job);
                _history.Items.Insert(0, new JobListItem(job));
                _history.SelectedIndex = 0;
                _status.Text = "Saved " + path;
            }
            catch (Exception ex)
            {
                job.Status = "SaveFailed";
                job.Diagnostics.Add(new CommandDiagnostic { Severity = "Error", Command = "JSON", Message = ex.Message });
                _history.Items.Insert(0, new JobListItem(job));
                _history.SelectedIndex = 0;
                _status.Text = "Received job but JSON save failed: " + ex.Message;
            }
        }

        private void ShowSelectedJob()
        {
            var item = _history.SelectedItem as JobListItem;
            _selectedJob = item?.Job;
            RenderJob(_selectedJob);
            UpdateControls();
        }

        private void RenderJob(ReceiptJob job)
        {
            _preview.Clear();
            if (job == null)
            {
                _details.Clear();
                return;
            }

            _details.Text =
                "Job: " + job.JobId + Environment.NewLine +
                "When: " + job.ReceivedAt.LocalDateTime + Environment.NewLine +
                "Source: " + job.SourceAddress + ":" + job.SourcePort + Environment.NewLine +
                "Bytes: " + job.ByteCount + Environment.NewLine +
                "Status: " + job.Status + Environment.NewLine +
                "JSON: " + job.SavedJsonPath;

            foreach (var element in job.Elements)
            {
                var text = string.Join("", element.Spans.Select(s => s.Text));
                if (element.Alignment == "Center") text = Center(text, 42);
                if (element.Alignment == "Right") text = text.PadLeft(Math.Max(42, text.Length));

                foreach (var span in element.Spans)
                {
                    var start = _preview.TextLength;
                    var spanText = span.Text;
                    if (element.Spans.Count == 1)
                    {
                        if (element.Alignment == "Center") spanText = Center(spanText, 42);
                        if (element.Alignment == "Right") spanText = spanText.PadLeft(Math.Max(42, spanText.Length));
                    }
                    _preview.AppendText(spanText);
                    _preview.Select(start, spanText.Length);
                    _preview.SelectionFont = new Font(_preview.Font, FontStyleFor(span.Style));
                    if (span.Style.WidthMultiplier > 1 || span.Style.HeightMultiplier > 1)
                    {
                        _preview.SelectionFont = new Font(_preview.Font.FontFamily, 14, FontStyleFor(span.Style));
                    }
                }
                _preview.AppendText(Environment.NewLine);
            }

            if (job.Events.Count > 0)
            {
                _preview.AppendText(Environment.NewLine + "Events" + Environment.NewLine);
                foreach (var printerEvent in job.Events)
                {
                    _preview.AppendText("- " + printerEvent.Type + " " + printerEvent.Name + " " + printerEvent.Data + Environment.NewLine);
                }
            }

            if (job.Diagnostics.Count > 0)
            {
                _preview.AppendText(Environment.NewLine + "Diagnostics" + Environment.NewLine);
                foreach (var diagnostic in job.Diagnostics)
                {
                    _preview.AppendText("- @" + diagnostic.Offset + " " + diagnostic.Command + ": " + diagnostic.Message + " [" + diagnostic.BytesHex + "]" + Environment.NewLine);
                }
            }
        }

        private void BrowseOutput()
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Choose where received ESC/POS jobs are saved as JSON.";
                dialog.SelectedPath = _outputDirectory.Text;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _outputDirectory.Text = dialog.SelectedPath;
                    SaveSettings();
                }
            }
        }

        private void OpenSelectedJson()
        {
            if (_selectedJob == null || string.IsNullOrWhiteSpace(_selectedJob.SavedJsonPath) || !File.Exists(_selectedJob.SavedJsonPath)) return;
            Process.Start(new ProcessStartInfo { FileName = _selectedJob.SavedJsonPath, UseShellExecute = true });
        }

        private void UpdateControls()
        {
            _start.Enabled = !_server.IsRunning;
            _stop.Enabled = _server.IsRunning;
            _bindAddress.Enabled = !_server.IsRunning;
            _port.Enabled = !_server.IsRunning;
            _openJson.Enabled = _selectedJob != null && File.Exists(_selectedJob.SavedJsonPath ?? "");
            if (string.IsNullOrWhiteSpace(_status.Text)) _status.Text = "Stopped";
        }

        private void SaveSettings()
        {
            _settings.BindAddress = _bindAddress.Text.Trim();
            _settings.Port = (int)_port.Value;
            _settings.OutputDirectory = _outputDirectory.Text.Trim();
            _settings.Save();
        }

        private static string Center(string text, int width)
        {
            if (text.Length >= width) return text;
            var left = (width - text.Length) / 2;
            return new string(' ', left) + text;
        }

        private static FontStyle FontStyleFor(TextStyle style)
        {
            var fontStyle = FontStyle.Regular;
            if (style.Bold) fontStyle |= FontStyle.Bold;
            if (style.Underline) fontStyle |= FontStyle.Underline;
            return fontStyle;
        }

        private sealed class JobListItem
        {
            public JobListItem(ReceiptJob job)
            {
                Job = job;
            }

            public ReceiptJob Job { get; }

            public override string ToString()
            {
                return Job.ReceivedAt.ToLocalTime().ToString("HH:mm:ss") + "  " + Job.ByteCount.ToString().PadLeft(5) + " bytes  " + Job.Status;
            }
        }
    }
}
