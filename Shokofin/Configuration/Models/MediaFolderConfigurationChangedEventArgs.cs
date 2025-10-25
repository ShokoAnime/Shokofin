using System;

namespace Shokofin.Configuration.Models;

public class MediaConfigurationChangedEventArgs(LibraryConfiguration libraryConfiguration, MediaFolderConfiguration config) : EventArgs {
    public LibraryConfiguration LibraryConfiguration { get; private init; } = libraryConfiguration;

    public MediaFolderConfiguration MediaFolderConfiguration { get; private init; } = config;
}
