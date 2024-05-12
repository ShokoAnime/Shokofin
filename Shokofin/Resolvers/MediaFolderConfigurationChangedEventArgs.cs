using System;
using MediaBrowser.Controller.Entities;
using Shokofin.Configuration;

namespace Shokofin.Resolvers;

public class MediaConfigurationChangedEventArgs : EventArgs
{
    public MediaFolderConfiguration Configuration { get; private set; }

    public Folder Folder { get; private set; }

    public MediaConfigurationChangedEventArgs(MediaFolderConfiguration config, Folder folder)
    {
        Configuration = config;
        Folder = folder;
    }
}