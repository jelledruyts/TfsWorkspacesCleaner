using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace TfsWorkspacesCleaner
{
    public static class Program
    {
        private static AssemblyName entryAssemblyName = Assembly.GetEntryAssembly().GetName();

        public static int Main(string[] args)
        {
            try
            {
                var options = GetOptions(args);
                try
                {
                    options.Logger.Log(TraceEventType.Information, string.Format(CultureInfo.CurrentCulture, "{0} v{1}", entryAssemblyName.Name, entryAssemblyName.Version));
                    if (options.Help)
                    {
                        ShowUsage(options.Logger);
                        return 0;
                    }
                    string invalidMessage;
                    if (!options.IsValid(out invalidMessage))
                    {
                        options.Logger.Log(TraceEventType.Error, "Error: " + invalidMessage);
                        ShowUsage(options.Logger);
                        return -2;
                    }
                    options.LogOptionList();
                    using (var cleaner = new Cleaner(options))
                    {
                        cleaner.Execute();
                        return 0;
                    }
                }
                catch (FileNotFoundException exc)
                {
                    if (exc.Message.Contains("Version=10.0.0.0"))
                    {
                        ConsoleLogger.WriteLine(ConsoleColor.Red, "This application was compiled against the Team Explorer 2010 assemblies, which");
                        ConsoleLogger.WriteLine(ConsoleColor.Red, "you don't seem to have installed. You can redirect the assembly versions by");
                        ConsoleLogger.WriteLine(ConsoleColor.Red, string.Format(CultureInfo.CurrentCulture, "uncommenting the appropriate lines in the \"{0}.exe.config\"", entryAssemblyName.Name));
                        ConsoleLogger.WriteLine(ConsoleColor.Red, "file, depending on the version of Team Explorer you do have installed.");
                        return -2;
                    }
                    else
                    {
                        throw;
                    }
                }
                catch (Exception exc)
                {
                    options.Logger.Log(TraceEventType.Error, exc.ToString());
                    return -1;
                }
            }
            catch (Exception exc)
            {
                // If something goes wrong while logging, make sure to at least write to the console.
                ConsoleLogger.WriteLine(ConsoleColor.Red, exc.ToString());
                return -1;
            }
        }

        private static void ShowUsage(ILogger logger)
        {
            logger.Log(TraceEventType.Information, string.Empty);
            logger.Log(TraceEventType.Information, "Deletes TFS workspaces that have not been accessed in a number of days, along with their files locally on disk.");
            logger.Log(TraceEventType.Information, string.Empty);
            logger.Log(TraceEventType.Information, string.Format(CultureInfo.CurrentCulture, "{0} /collection:url [/q] [/owner:owner] [/computer:computer]", entryAssemblyName.Name));
            logger.Log(TraceEventType.Information, "                     [/inactivedays:days] [/log:logfile] [/simulate:true|false]");
            logger.Log(TraceEventType.Information, "                     [/workspacetype:Workstation|BuildServer]");
            logger.Log(TraceEventType.Information, "                     [/deletelocal:true|false] [/comment:comment]");
            logger.Log(TraceEventType.Information, string.Empty);
            logger.Log(TraceEventType.Information, "  /collection    : The url for a Team Project Collection");
            logger.Log(TraceEventType.Information, "  /q             : Quiet (do not prompt for confirmation)");
            logger.Log(TraceEventType.Information, "  /owner         : The user name of the workspace owner");
            logger.Log(TraceEventType.Information, "  /computer      : The name of the computer");
            logger.Log(TraceEventType.Information, "  /inactivedays  : The number of days the workspace has not been accessed");
            logger.Log(TraceEventType.Information, "  /log           : The log file name");
            logger.Log(TraceEventType.Information, "  /simulate      : 'true' to simulate (does not delete anything)");
            logger.Log(TraceEventType.Information, "  /workspacetype : 'Workstation' (default) only deletes the workspace files");
            logger.Log(TraceEventType.Information, "                   'BuildServer' deletes sources and binaries on a build server");
            logger.Log(TraceEventType.Information, "  /deletelocal   : 'false' to not delete local files, only the server workspace");
            logger.Log(TraceEventType.Information, "  /comment       : If set, requires that workspaces contain this value in their");
            logger.Log(TraceEventType.Information, "                   comment, e.g. 'Workspace created by Team Build' to avoid");
            logger.Log(TraceEventType.Information, "                   deleting workspaces that were not created by a build server");
            logger.Log(TraceEventType.Information, string.Empty);
            logger.Log(TraceEventType.Information, "During processing, press any key to pause or be able to cancel.");
            logger.Log(TraceEventType.Information, string.Empty);
        }

        private static Options GetOptions(string[] args)
        {
            var arguments = GetArguments(args);
            var options = new Options();
            options.TeamProjectCollectionUrl = GetArgumentValue(arguments, "collection", null);
            options.Owner = GetArgumentValue(arguments, "owner", Environment.UserName);
            options.Computer = GetArgumentValue(arguments, "computer", Environment.MachineName);
            options.WorkspaceType = (WorkspaceType)Enum.Parse(typeof(WorkspaceType), GetArgumentValue(arguments, "workspacetype", WorkspaceType.Workstation.ToString()), true);
            options.DeleteLocalDirectories = bool.Parse(GetArgumentValue(arguments, "deletelocal", bool.TrueString));
            options.Simulate = bool.Parse(GetArgumentValue(arguments, "simulate", bool.FalseString));
            options.Quiet = arguments.ContainsKey("q");
            options.MaxLastAccessDate = DateTime.Now.AddDays(-1 * int.Parse(GetArgumentValue(arguments, "inactivedays", 30.ToString())));
            options.WorkspaceComment = GetArgumentValue(arguments, "comment", null);
            options.Help = arguments.Count == 0 || arguments.ContainsKey("?");
            var logger = new AggregateLogger();
            logger.Loggers.Add(new ConsoleLogger());
            if (arguments.ContainsKey("log"))
            {
                logger.Loggers.Add(new FileLogger(arguments["log"]));
            }
            options.Logger = logger;
            return options;
        }

        private static IDictionary<string, string> GetArguments(string[] args)
        {
            var arguments = new Dictionary<string, string>();
            foreach (var arg in args)
            {
                var argument = arg;
                if (argument.StartsWith("/"))
                {
                    argument = arg.Substring(1);
                }
                var key = argument;
                string value = null;
                var separatorIndex = key.IndexOf(':');
                if (separatorIndex >= 0)
                {
                    key = argument.Substring(0, separatorIndex);
                    value = argument.Substring(separatorIndex + 1);
                }
                arguments.Add(key.ToLowerInvariant(), value);
            }
            return arguments;
        }

        private static string GetArgumentValue(IDictionary<string, string> arguments, string key, string defaultValue)
        {
            return arguments.ContainsKey(key) ? arguments[key] : defaultValue;
        }
    }
}