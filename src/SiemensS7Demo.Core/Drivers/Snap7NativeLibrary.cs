using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace SiemensS7Demo.Drivers;

internal static class Snap7NativeLibrary
{
    private const string LibraryName = "snap7.dll";
    private static bool _resolverRegistered;

    public static void RegisterResolver()
    {
        if (_resolverRegistered)
        {
            return;
        }

        NativeLibrary.SetDllImportResolver(typeof(Snap7NativeLibrary).Assembly, Resolve);
        _resolverRegistered = true;
    }

    public static string DescribeCandidatePaths()
        => string.Join(Environment.NewLine, GetCandidatePaths());

    private static IntPtr Resolve(
        string libraryName,
        Assembly assembly,
        DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, LibraryName, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(libraryName, "snap7", StringComparison.OrdinalIgnoreCase))
        {
            return IntPtr.Zero;
        }

        foreach (var candidate in GetCandidatePaths())
        {
            if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out var handle))
            {
                return handle;
            }
        }

        return IntPtr.Zero;
    }

    private static IEnumerable<string> GetCandidatePaths()
    {
        yield return Path.Combine(AppContext.BaseDirectory, LibraryName);
        yield return Path.Combine(Environment.CurrentDirectory, LibraryName);

        foreach (var root in CandidateRoots())
        {
            yield return Path.GetFullPath(Path.Combine(root, "src", "SiemensS7Demo", "Native", "Snap7", "win64", LibraryName));
            yield return Path.GetFullPath(Path.Combine(root, "Native", "Snap7", "win64", LibraryName));
        }

        var envPath = Environment.GetEnvironmentVariable("SNAP7_DLL");
        if (!string.IsNullOrWhiteSpace(envPath))
        {
            yield return Path.GetFullPath(Environment.ExpandEnvironmentVariables(envPath));
        }
    }

    private static IEnumerable<string> CandidateRoots()
    {
        var current = Environment.CurrentDirectory;
        yield return Path.Combine(current, "..");
        yield return Path.Combine(current, "..", "..");
        yield return Path.Combine(current, "..", "..", "..");

        var baseDirectory = AppContext.BaseDirectory;
        yield return Path.Combine(baseDirectory, "..", "..", "..", "..");
        yield return Path.Combine(baseDirectory, "..", "..", "..", "..", "..");
    }
}
