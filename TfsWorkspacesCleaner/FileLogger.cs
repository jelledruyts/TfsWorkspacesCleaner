using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace TfsWorkspacesCleaner
{
    public class FileLogger : ILogger
    {
        public string FileName { get; private set; }

        public FileLogger(string fileName)
        {
            this.FileName = fileName;
        }

        public void Log(TraceEventType type, string message)
        {
            File.AppendAllText(this.FileName, string.Format(CultureInfo.CurrentCulture, "[{1}] [{2,-11}] {3}{0}", Environment.NewLine, DateTime.Now, type.ToString(), message));
        }
    }
}