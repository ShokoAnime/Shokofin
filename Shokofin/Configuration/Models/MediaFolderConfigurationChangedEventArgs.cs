using System;
using MediaBrowser.Controller.Entities;

namespace Shokofin.Configuration.Models;

public class MediaConfigurationChangedEventArgs : EventArgs
{
    public MediaFolderConfiguration Configuration { get; private init; }

    public Folder MediaFolder { get; private init; }

    public MediaConfigurationChangedEventArgs(MediaFolderConfiguration config, Folder folder)
    {
        Configuration = config;
        MediaFolder = folder;
    }
}