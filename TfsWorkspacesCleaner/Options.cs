using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace TfsWorkspacesCleaner
{
    public class Options
    {
        public string TeamProjectCollectionUrl { get; set; }
        public string Owner { get; set; }
        public string Computer { get; set; }
        public string WorkspaceComment { get; set; }
        public WorkspaceType WorkspaceType { get; set; }
        public bool DeleteLocalDirectories { get; set; }
        public bool Simulate { get; set; }
        public bool Quiet { get; set; }
        public DateTime MaxLastAccessDate { get; set; }
        [Browsable(false)]
        public bool Help { get; set; }
        [Browsable(false)]
        public ILogger Logger { get; set; }

        public void LogOptionList()
        {
            this.Logger.Log(TraceEventType.Verbose, "Options:");
            foreach (var property in this.GetType().GetProperties().Where(p => p.GetCustomAttributes(typeof(BrowsableAttribute), false).Length == 0))
            {
                this.Logger.Log(TraceEventType.Verbose, string.Format(CultureInfo.CurrentCulture, "  {0}: {1}", property.Name, property.GetValue(this, null)));
            }
        }

        public bool IsValid(out string invalidMessage)
        {
            Uri dummy;
            if (string.IsNullOrEmpty(this.TeamProjectCollectionUrl) || !Uri.TryCreate(this.TeamProjectCollectionUrl, UriKind.Absolute, out dummy))
            {
                invalidMessage = "The URL of the Team Project Collection must be specified.";
                return false;
            }
            invalidMessage = null;
            return true;
        }
    }
}