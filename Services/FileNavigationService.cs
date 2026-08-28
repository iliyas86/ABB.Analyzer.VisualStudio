using EnvDTE;
using Microsoft.VisualStudio.Shell;
using System;
using System.IO;

namespace ABB.Analyze.VisualStudio.Services
{
    internal static class FileNavigationService
    {
        public static FileNavigationResult Open(
            string repository,
            string relativePath,
            int line = 0)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return FileNavigationResult.Failed(
                    "No source-file path is available for this item.");
            }

            if (string.IsNullOrWhiteSpace(repository))
            {
                return FileNavigationResult.Failed(
                    "The current repository path is unavailable.");
            }

            try
            {
                string resolvedPath =
                    ResolvePath(
                        repository,
                        relativePath);

                if (string.IsNullOrWhiteSpace(resolvedPath))
                {
                    return FileNavigationResult.Failed(
                        "The source file could not be resolved." +
                        Environment.NewLine +
                        Environment.NewLine +
                        "Path:" +
                        Environment.NewLine +
                        relativePath);
                }

                if (!File.Exists(resolvedPath))
                {
                    return FileNavigationResult.Failed(
                        "The source file does not exist." +
                        Environment.NewLine +
                        Environment.NewLine +
                        "Resolved path:" +
                        Environment.NewLine +
                        resolvedPath);
                }

                var dte =
                    Package.GetGlobalService(
                        typeof(DTE))
                    as DTE;

                if (dte == null)
                {
                    return FileNavigationResult.Failed(
                        "The Visual Studio automation service is unavailable.");
                }

                Window window =
                    dte.ItemOperations.OpenFile(
                        resolvedPath);

                window?.Activate();

                NavigateToLine(
                    dte,
                    line);

                return FileNavigationResult.Succeeded(
                    resolvedPath);
            }
            catch (Exception exception)
            {
                return FileNavigationResult.Failed(
                    "The source file could not be opened." +
                    Environment.NewLine +
                    Environment.NewLine +
                    exception.Message);
            }
        }

        private static string ResolvePath(
            string repository,
            string path)
        {
            string normalizedPath =
                path.Trim()
                    .Trim('"')
                    .Replace(
                        '/',
                        Path.DirectorySeparatorChar);

            if (Path.IsPathRooted(normalizedPath))
            {
                return Path.GetFullPath(
                    normalizedPath);
            }

            string repositoryRelativePath =
                Path.GetFullPath(
                    Path.Combine(
                        repository,
                        normalizedPath));

            if (File.Exists(repositoryRelativePath))
            {
                return repositoryRelativePath;
            }

            string fileName =
                Path.GetFileName(
                    normalizedPath);

            if (string.IsNullOrWhiteSpace(fileName))
            {
                return repositoryRelativePath;
            }

            return FindFileByName(
                       repository,
                       fileName)
                   ?? repositoryRelativePath;
        }

        private static string FindFileByName(
            string repository,
            string fileName)
        {
            try
            {
                foreach (string file in Directory.EnumerateFiles(
                             repository,
                             fileName,
                             SearchOption.AllDirectories))
                {
                    if (!IsIgnoredDirectory(file))
                    {
                        return file;
                    }
                }
            }
            catch (
                UnauthorizedAccessException)
            {
                // Some repository subdirectories may not be accessible.
            }
            catch (
                IOException)
            {
                // File discovery is best-effort.
            }

            return null;
        }

        private static bool IsIgnoredDirectory(
            string path)
        {
            string normalizedPath =
                path.Replace(
                        '/',
                        Path.DirectorySeparatorChar)
                    .ToLowerInvariant();

            string separator =
                Path.DirectorySeparatorChar
                    .ToString();

            return
                normalizedPath.Contains(
                    separator + ".git" + separator) ||
                normalizedPath.Contains(
                    separator + ".vs" + separator) ||
                normalizedPath.Contains(
                    separator + "bin" + separator) ||
                normalizedPath.Contains(
                    separator + "obj" + separator) ||
                normalizedPath.Contains(
                    separator + "node_modules" + separator) ||
                normalizedPath.Contains(
                    separator + "strykeroutput" + separator);
        }

        private static void NavigateToLine(
            DTE dte,
            int line)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (line <= 0)
            {
                return;
            }

            if (!(dte.ActiveDocument?.Selection
                  is TextSelection selection))
            {
                return;
            }

            int safeLine =
                Math.Max(
                    1,
                    line);

            selection.GotoLine(
                safeLine,
                true);
        }
    }

    internal sealed class FileNavigationResult
    {
        private FileNavigationResult(
            bool success,
            string path,
            string errorMessage)
        {
            Success = success;
            Path = path ?? string.Empty;
            ErrorMessage =
                errorMessage ?? string.Empty;
        }

        public bool Success { get; }

        public string Path { get; }

        public string ErrorMessage { get; }

        public static FileNavigationResult Succeeded(
            string path)
        {
            return new FileNavigationResult(
                true,
                path,
                string.Empty);
        }

        public static FileNavigationResult Failed(
            string errorMessage)
        {
            return new FileNavigationResult(
                false,
                string.Empty,
                errorMessage);
        }
    }
}