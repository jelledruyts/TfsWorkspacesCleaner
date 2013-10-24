using System.Collections.Generic;
using System.Diagnostics;

namespace TfsWorkspacesCleaner
{
    public class AggregateLogger : ILogger
    {
        public IList<ILogger> Loggers { get; private set; }

        public AggregateLogger()
        {
            this.Loggers = new List<ILogger>();
        }

        public void Log(TraceEventType type, string message)
        {
            foreach (var logger in this.Loggers)
            {
                logger.Log(type, message);
            }
        }
    }
}