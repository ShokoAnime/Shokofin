using System;
using System.Collections.Generic;

namespace Shokofin.Configuration.Models;

public class LibraryConfigurationChangedEventArgs(LibraryConfiguration libraryConfiguration, IReadOnlyList<MediaFolderConfiguration> mediaFolderConfigurations) : EventArgs {
    public LibraryConfiguration LibraryConfiguration { get; private init; } = libraryConfiguration;

    public IReadOnlyList<MediaFolderConfiguration> MediaFolderConfigurations { get; private init; } = mediaFolderConfigurations;
}
