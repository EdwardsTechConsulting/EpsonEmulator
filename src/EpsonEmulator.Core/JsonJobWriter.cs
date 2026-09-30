using System;
using System.IO;
using System.Text.Json;

namespace EpsonEmulator.Core
{
    public sealed class JsonJobWriter
    {
        private readonly string _outputDirectory;

        public JsonJobWriter(string outputDirectory)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory)) throw new ArgumentException("Output directory is required.", nameof(outputDirectory));
            _outputDirectory = outputDirectory;
        }

        public string Save(ReceiptJob job)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));
            Directory.CreateDirectory(_outputDirectory);

            if (string.IsNullOrWhiteSpace(job.JobId)) job.JobId = Guid.NewGuid().ToString("N");
            var stamp = job.ReceivedAt.ToLocalTime().ToString("yyyyMMdd-HHmmss-fff");
            var fileName = stamp + "-" + job.JobId + ".json";
            var finalPath = Path.Combine(_outputDirectory, fileName);
            var tempPath = finalPath + ".tmp";
            if (File.Exists(finalPath))
            {
                finalPath = Path.Combine(_outputDirectory, stamp + "-" + job.JobId + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".json");
                tempPath = finalPath + ".tmp";
            }
            job.SavedJsonPath = finalPath;

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(tempPath, JsonSerializer.Serialize(job, options));
            File.Move(tempPath, finalPath);
            return finalPath;
        }
    }
}
