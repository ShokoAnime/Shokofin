
using System;
using System.IO;
using MediaBrowser.Controller.Entities;
using Shokofin.Configuration;

namespace Shokofin.Resolvers.Models;

public class ShokoWatcher
{
    public Folder MediaFolder;

    public MediaFolderConfiguration Configuration;

    public FileSystemWatcher Watcher;

    public IDisposable SubmitterLease;

    public ShokoWatcher(Folder mediaFolder, MediaFolderConfiguration configuration, FileSystemWatcher watcher, IDisposable lease)
    {
        MediaFolder = mediaFolder;
        Configuration = configuration;
        Watcher = watcher;
        SubmitterLease = lease;
    }
}
