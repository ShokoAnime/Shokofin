using System;
using System.Collections.Generic;

namespace Shokofin.Resolvers.Models;

public sealed class VfsResolution(string? vfsPath, bool shouldContinue, bool skipValidation, HashSet<string> paths) : IDisposable {
    public string? VfsPath { get; } = vfsPath;

    public bool ShouldContinue { get; } = shouldContinue;

    public bool SkipValidation { get; } = skipValidation;

    public HashSet<string> Paths { get; } = paths;

    internal IDisposable? LibraryLock { get; init; }

    public void Dispose() => LibraryLock?.Dispose();
}
