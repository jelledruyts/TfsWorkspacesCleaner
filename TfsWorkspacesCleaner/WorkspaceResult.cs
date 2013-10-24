using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.TeamFoundation.VersionControl.Client;

namespace TfsWorkspacesCleaner
{
    public class WorkspaceResult
    {
        public Workspace Workspace { get; private set; }
        public Status ServerStatus { get; set; }
        public Status LocalStatus { get; set; }
        public IList<DirectoryInfo> LocalFoldersDeleted { get; private set; }
        public long LocalDeletedSizeInBytes { get; set; }
        public Exception Exception { get; set; }

        public WorkspaceResult(Workspace workspace)
        {
            this.Workspace = workspace;
            this.LocalFoldersDeleted = new List<DirectoryInfo>();
        }
    }
}