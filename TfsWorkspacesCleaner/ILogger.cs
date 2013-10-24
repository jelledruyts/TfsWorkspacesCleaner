using System.Diagnostics;

namespace TfsWorkspacesCleaner
{
    public interface ILogger
    {
        void Log(TraceEventType type, string message);
    }
}