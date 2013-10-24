using System;
using System.Diagnostics;

namespace TfsWorkspacesCleaner
{
    public class ConsoleLogger : ILogger
    {
        public void Log(TraceEventType type, string message)
        {
            var color = ConsoleColor.Gray;
            if (type <= TraceEventType.Error)
            {
                color = ConsoleColor.Red;
            }
            else if (type <= TraceEventType.Warning)
            {
                color = ConsoleColor.Yellow;
            }
            else if (type > TraceEventType.Information)
            {
                color = ConsoleColor.DarkGray;
            }
            WriteLine(color, message);
        }

        public static void WriteLine(ConsoleColor color, string message)
        {
            Write(color, message + Environment.NewLine);
        }

        public static void Write(ConsoleColor color, string message)
        {
            var originalColor = Console.ForegroundColor;
            try
            {
                Console.ForegroundColor = color;
                Console.Write(message);
            }
            finally
            {
                Console.ForegroundColor = originalColor;
            }
        }
    }
}