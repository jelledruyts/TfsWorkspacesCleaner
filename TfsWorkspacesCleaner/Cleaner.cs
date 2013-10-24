using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.VersionControl.Client;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

namespace TfsWorkspacesCleaner
{
    public class Cleaner : IDisposable
    {
        #region Fields

        private static readonly string[] BuildServerWorkspaceRootDirectories = new string[] { "Sources", "Binaries" };
        private static readonly DirectoryInfoComparer DirectoryInfoComparerInstance = new DirectoryInfoComparer();
        private TfsTeamProjectCollection tfs;
        private Options options;

        #endregion

        #region Constructors

        public Cleaner(Options options)
        {
            this.options = options;
        }

        #endregion

        #region Execute

        public void Execute()
        {
            var workspacesToProcess = GetWorkspacesToProcess();

            if (!workspacesToProcess.Any())
            {
                Log(TraceEventType.Information, "Nothing to do. Exiting.");
                return;
            }

            if (!this.options.Quiet && !this.options.Simulate)
            {
                var confirmed = false;
                ConsoleLogger.Write(ConsoleColor.Yellow, string.Format(CultureInfo.CurrentCulture, "Enter 'y' to confirm deleting {0} workspace(s): ", workspacesToProcess.Count));
                var confirm = Console.ReadLine();
                confirmed = string.Equals(confirm, "y", StringComparison.OrdinalIgnoreCase);
                if (!confirmed)
                {
                    Log(TraceEventType.Information, "Canceled");
                    return;
                }
            }

            foreach (var workspaceToProcess in workspacesToProcess)
            {
                try
                {
                    if (Console.KeyAvailable)
                    {
                        var keyPressed = Console.ReadKey(true);
                        ConsoleLogger.Write(ConsoleColor.Yellow, "Paused. Enter 'q' to quit processing or any other key to resume: ");
                        var confirm = Console.ReadLine();
                        if (string.Equals(confirm, "q", StringComparison.OrdinalIgnoreCase))
                        {
                            Log(TraceEventType.Information, "Processing canceled.");
                            break;
                        }
                    }
                    // Refresh the workspace so we can check if it is still valid for cleanup.
                    workspaceToProcess.Workspace.Refresh();
                    Log(TraceEventType.Information, string.Format(CultureInfo.CurrentCulture, "Processing workspace \"{0}\" ({1}/{2})", workspaceToProcess.Workspace.Name, workspacesToProcess.IndexOf(workspaceToProcess) + 1, workspacesToProcess.Count));
                    Log(TraceEventType.Verbose, string.Format(CultureInfo.CurrentCulture, "  Last accessed: {0}", workspaceToProcess.Workspace.LastAccessDate));
                    Log(TraceEventType.Verbose, string.Format(CultureInfo.CurrentCulture, "  Comment: \"{0}\"", workspaceToProcess.Workspace.Comment));
                    if (workspaceToProcess.Workspace.LastAccessDate >= this.options.MaxLastAccessDate)
                    {
                        // The workspace was accessed since we determined it could be cleaned up; skip it.
                        Log(TraceEventType.Warning, "  The workspace was accessed since determining it could be deleted; skipping.");
                    }
                    else
                    {
                        if (this.options.DeleteLocalDirectories && workspaceToProcess.Workspace.IsLocal)
                        {
                            try
                            {
                                foreach (var localFolderToDelete in GetLocalFoldersToDelete(workspaceToProcess.Workspace))
                                {
                                    if (localFolderToDelete.Exists)
                                    {
                                        Log(TraceEventType.Verbose, string.Format(CultureInfo.CurrentCulture, "  Deleting local folder: \"{0}\"", localFolderToDelete.FullName));
                                        workspaceToProcess.LocalDeletedSizeInBytes += DeleteFileSystemItem(localFolderToDelete, this.options.Simulate);
                                        workspaceToProcess.LocalFoldersDeleted.Add(localFolderToDelete);
                                        workspaceToProcess.LocalStatus = Status.Deleted;
                                        DeleteEmptyDirectoryHierarchy(localFolderToDelete.Parent, this.options.Simulate, workspaceToProcess.LocalFoldersDeleted);
                                    }
                                }
                                Log(TraceEventType.Verbose, string.Format(CultureInfo.CurrentCulture, "  Deleted {0} in {1} local folder(s) for this workspace", ToSizeString(workspaceToProcess.LocalDeletedSizeInBytes), workspaceToProcess.LocalFoldersDeleted.Count));
                            }
                            catch (Exception exc)
                            {
                                Log(TraceEventType.Error, string.Format(CultureInfo.CurrentCulture, "An error occurred while deleting local folders for workspace \"{0}\": {1}", workspaceToProcess.Workspace.Name, exc.ToString()));
                                workspaceToProcess.LocalStatus = Status.Failed;
                                workspaceToProcess.Exception = exc;
                            }
                        }
                        if (workspaceToProcess.LocalStatus != Status.Failed && !workspaceToProcess.Workspace.IsDeleted)
                        {
                            try
                            {
                                Log(TraceEventType.Verbose, "  Deleting server workspace");
                                if (!this.options.Simulate)
                                {
                                    workspaceToProcess.Workspace.Delete();
                                }
                                workspaceToProcess.ServerStatus = Status.Deleted;
                            }
                            catch (Exception exc)
                            {
                                Log(TraceEventType.Error, string.Format(CultureInfo.CurrentCulture, "An error occurred while deleting workspace \"{0}\": {1}", workspaceToProcess.Workspace.Name, exc.ToString()));
                                workspaceToProcess.ServerStatus = Status.Failed;
                                workspaceToProcess.Exception = exc;
                            }
                        }
                    }
                }
                catch (Exception exc)
                {
                    Log(TraceEventType.Error, string.Format(CultureInfo.CurrentCulture, "An error occurred while processing workspace \"{0}\": {1}", workspaceToProcess.Workspace.Name, exc.ToString()));
                    workspaceToProcess.Exception = exc;
                }
            }

            Log(TraceEventType.Information, string.Empty);
            Log(TraceEventType.Information, "SUMMARY:");
            Log(TraceEventType.Information, string.Empty);
            LogTable("Workspace", "Last Accessed", "Status", "Size");
            Log(TraceEventType.Information, new string('-', 79));
            foreach (var workspaceToProcess in workspacesToProcess)
            {
                var status = (workspaceToProcess.LocalStatus == Status.Failed || workspaceToProcess.ServerStatus == Status.Failed ? Status.Failed.ToString() : (workspaceToProcess.LocalStatus == Status.Skipped && workspaceToProcess.ServerStatus == Status.Skipped ? Status.Skipped.ToString() : Status.Deleted.ToString()));
                LogTable(workspaceToProcess.Workspace.Name, workspaceToProcess.Workspace.LastAccessDate.ToString(), status, ToSizeString(workspaceToProcess.LocalDeletedSizeInBytes));
            }
            Log(TraceEventType.Information, string.Empty);

            Log(TraceEventType.Information, "Done " + (this.options.Simulate ? "simulating." : "processing."));
            Log(TraceEventType.Information, string.Format(CultureInfo.CurrentCulture, "{0} of {1} server workspace(s) deleted.", workspacesToProcess.Count(w => w.ServerStatus == Status.Deleted), workspacesToProcess.Count));
            Log(TraceEventType.Information, string.Format(CultureInfo.CurrentCulture, "{0} deleted in {1} local folder(s).", ToSizeString(workspacesToProcess.Sum(w => w.LocalDeletedSizeInBytes)), workspacesToProcess.Sum(w => w.LocalFoldersDeleted.Count)));
            var workspacesFailed = workspacesToProcess.Where(w => w.Exception != null);
            if (workspacesFailed.Any())
            {
                Log(TraceEventType.Warning, string.Format(CultureInfo.CurrentCulture, "{0} of {1} workspace(s) failed: {2}", workspacesFailed.Count(), workspacesToProcess.Count, string.Join(", ", workspacesFailed.Select(w => w.Workspace.Name).ToArray())));
            }
        }

        #endregion

        #region Helper Methods

        private void EnsureConnected()
        {
            if (this.tfs == null)
            {
                Log(TraceEventType.Information, string.Format(CultureInfo.CurrentCulture, "Connecting to \"{0}\".", this.options.TeamProjectCollectionUrl));
                this.tfs = TfsTeamProjectCollectionFactory.GetTeamProjectCollection(new Uri(this.options.TeamProjectCollectionUrl));
                this.tfs.EnsureAuthenticated();
            }
        }

        private List<WorkspaceResult> GetWorkspacesToProcess()
        {
            EnsureConnected();
            var vcs = this.tfs.GetService<VersionControlServer>();
            var allWorkspaces = vcs.QueryWorkspaces(null, this.options.Owner, this.options.Computer);
            Log(TraceEventType.Information, string.Format(CultureInfo.CurrentCulture, "{0} workspace(s) found for user \"{1}\" on computer \"{2}\".", allWorkspaces.Length, this.options.Owner, this.options.Computer));
            var workspacesToProcess = allWorkspaces.Where(w => w.LastAccessDate < this.options.MaxLastAccessDate);
            Log(TraceEventType.Information, string.Format(CultureInfo.CurrentCulture, "{0} workspace(s) found that were not accessed since {1}.", workspacesToProcess.Count(), this.options.MaxLastAccessDate));
            if (!string.IsNullOrEmpty(this.options.WorkspaceComment))
            {
                workspacesToProcess = workspacesToProcess.Where(w => !string.IsNullOrEmpty(w.Comment) && w.Comment.IndexOf(this.options.WorkspaceComment, StringComparison.InvariantCultureIgnoreCase) >= 0);
                Log(TraceEventType.Information, string.Format(CultureInfo.CurrentCulture, "{0} workspace(s) found that contain the required comment.", workspacesToProcess.Count()));
            }
            return workspacesToProcess.OrderBy(w => w.LastAccessDate).Select(w => new WorkspaceResult(w)).ToList();
        }

        private IList<DirectoryInfo> GetLocalFoldersToDelete(Workspace workspace)
        {
            var mappedLocalFolders = workspace.Folders.Where(f => f.Type == WorkingFolderType.Map).Select(f => f.LocalItem);
            if (this.options.WorkspaceType == WorkspaceType.BuildServer)
            {
                // On a Team Build server, we don't want to only delete the mapped workspace directories,
                // but also the binaries and possibly other directories used locally by a build agent.
                var firstMappedFolder = mappedLocalFolders.FirstOrDefault();
                if (firstMappedFolder != null && Directory.Exists(firstMappedFolder))
                {
                    var buildRootFolder = new DirectoryInfo(firstMappedFolder);
                    while (buildRootFolder != null)
                    {
                        // We walk down the first mapped folder's path to find a directory that contains
                        // the typical "Sources" and "Binaries" directories used by Team Build.
                        var isBuildRootDirectory = buildRootFolder.GetDirectories().Select(d => d.Name).Intersect(BuildServerWorkspaceRootDirectories, StringComparer.OrdinalIgnoreCase).Count() == BuildServerWorkspaceRootDirectories.Length;
                        if (isBuildRootDirectory)
                        {
                            // This will be the root directory containing the complete build artifacts.
                            // Delete this one instead of the individual mapped workspace directories.
                            return new DirectoryInfo[] { buildRootFolder };
                        }
                        buildRootFolder = buildRootFolder.Parent;
                    }

                    // If the proper build-specific root folder was not found, fall back to the mapped folders.
                    return mappedLocalFolders.Select(f => new DirectoryInfo(f)).ToList();
                }
            }
            else
            {
                // On regular workstations, only delete the mapped workspace directories.
                return mappedLocalFolders.Select(f => new DirectoryInfo(f)).ToList();
            }
            return new DirectoryInfo[0];
        }

        private void DeleteEmptyDirectoryHierarchy(DirectoryInfo directory, bool simulate, IList<DirectoryInfo> logicallyDeletedDirectories)
        {
            if (directory != null && directory.Exists && !directory.EnumerateDirectories().Except(logicallyDeletedDirectories, DirectoryInfoComparerInstance).Any() && !directory.EnumerateFiles().Any())
            {
                // There are no directories or files in this directory; delete it.
                Log(TraceEventType.Verbose, string.Format(CultureInfo.CurrentCulture, "  Deleting empty local folder: \"{0}\"", directory.FullName));
                if (!simulate)
                {
                    Directory.Delete(directory.FullName);
                }
                logicallyDeletedDirectories.Add(directory);

                // Traverse the parent hierarchy.
                DeleteEmptyDirectoryHierarchy(directory.Parent, simulate, logicallyDeletedDirectories);
            }
        }

        private long DeleteFileSystemItem(FileSystemInfo item, bool simulate)
        {
            long deletedSizeInBytes = 0;

            var directory = item as DirectoryInfo;
            if (directory != null)
            {
                foreach (var childItem in directory.EnumerateFileSystemInfos())
                {
                    deletedSizeInBytes += DeleteFileSystemItem(childItem, simulate);
                }
            }

            var file = item as FileInfo;
            if (file != null)
            {
                deletedSizeInBytes += file.Length;
            }

            if (!simulate)
            {
                item.Attributes = FileAttributes.Normal;
                item.Delete();
            }

            return deletedSizeInBytes;
        }

        private string ToSizeString(long bytes)
        {
            if (bytes < 1024)
            {
                return string.Format(CultureInfo.CurrentCulture, "{0} B", bytes);
            }
            double kiloBytes = (bytes / 1024d);
            if (kiloBytes < 1024)
            {
                return string.Format(CultureInfo.CurrentCulture, "{0} KB", kiloBytes.ToString("f2", CultureInfo.CurrentCulture));
            }
            double megaBytes = (kiloBytes / 1024d);
            if (megaBytes < 1024)
            {
                return string.Format(CultureInfo.CurrentCulture, "{0} MB", megaBytes.ToString("f2", CultureInfo.CurrentCulture));
            }
            double gigaBytes = (megaBytes / 1024d);
            if (gigaBytes < 1024)
            {
                return string.Format(CultureInfo.CurrentCulture, "{0} GB", gigaBytes.ToString("f3", CultureInfo.CurrentCulture));
            }
            double teraBytes = (gigaBytes / 1024d);
            return string.Format(CultureInfo.CurrentCulture, "{0} TB", gigaBytes.ToString("f3", CultureInfo.CurrentCulture));
        }

        #endregion

        #region Logging

        private void LogTable(string workspaceName, string lastAccessDate, string status, string size)
        {
            Log(TraceEventType.Information, string.Format(CultureInfo.CurrentCulture, "{0,-31}  {1,-22}  {2,-7}  {3,-7}", workspaceName, lastAccessDate, status, size));
        }

        private void Log(TraceEventType type, string message)
        {
            this.options.Logger.Log(type, message);
        }

        #endregion

        #region Dispose

        public void Dispose()
        {
            if (this.tfs != null)
            {
                this.tfs.Dispose();
            }
        }

        #endregion

        #region DirectoryInfoComparer Class

        private class DirectoryInfoComparer : IEqualityComparer<DirectoryInfo>
        {
            public bool Equals(DirectoryInfo x, DirectoryInfo y)
            {
                return string.Equals(x.FullName, y.FullName, StringComparison.OrdinalIgnoreCase);
            }

            public int GetHashCode(DirectoryInfo obj)
            {
                return obj.FullName.GetHashCode();
            }
        }

        #endregion
    }
}