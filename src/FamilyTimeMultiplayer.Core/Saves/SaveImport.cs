using System;
using System.IO;

namespace FamilyTimeMultiplayer.Core.Saves;

public static class SaveImport
{
    private const string TemporaryFileSuffix = ".ftmp-importing";
    private const string InvalidWindowsFileNameCharacters = "<>:\"/\\|?*";

    public static string CopyToUniqueName(string sourcePath, string destinationDirectory, string requestedName, string extension)
    {
        ValidateArguments(sourcePath, destinationDirectory, requestedName, extension);
        string normalizedName = requestedName.Trim();
        string destinationRoot = Path.GetFullPath(destinationDirectory);
        Directory.CreateDirectory(destinationRoot);
        string destinationPath = Path.Combine(destinationRoot, normalizedName + extension);
        if (File.Exists(destinationPath)) throw new SaveNameConflictException(destinationPath);
        string temporaryPath = Path.Combine(destinationRoot, Guid.NewGuid().ToString("N") + TemporaryFileSuffix);
        return CopyThroughTemporaryFile(sourcePath, destinationPath, temporaryPath);
    }

    private static void ValidateArguments(string sourcePath, string destinationDirectory, string requestedName, string extension)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) throw new ArgumentException("A source save path is required.", nameof(sourcePath));
        if (string.IsNullOrWhiteSpace(destinationDirectory)) throw new ArgumentException("A destination directory is required.", nameof(destinationDirectory));
        ValidateSaveName(requestedName);
        ValidateExtension(extension);
    }

    private static void ValidateSaveName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A save name is required.", nameof(name));
        string normalizedName = name.Trim();
        if (normalizedName.Length == 0 || normalizedName == "." || normalizedName == ".." ||
            normalizedName.IndexOfAny(InvalidWindowsFileNameCharacters.ToCharArray()) >= 0 ||
            ContainsControlCharacter(normalizedName) || normalizedName.EndsWith(".", StringComparison.Ordinal))
            throw new ArgumentException("The save name contains unsupported characters.", nameof(name));
    }

    private static void ValidateExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension) || extension[0] != '.' ||
            extension.IndexOfAny(InvalidWindowsFileNameCharacters.ToCharArray()) >= 0 ||
            extension.Length == 1 || extension.Substring(1).IndexOf('.') >= 0)
            throw new ArgumentException("A single file extension is required.", nameof(extension));
    }

    private static bool ContainsControlCharacter(string value)
    {
        foreach (char character in value)
            if (char.IsControl(character)) return true;
        return false;
    }

    private static string CopyThroughTemporaryFile(string sourcePath, string destinationPath, string temporaryPath)
    {
        try
        {
            CopyAndValidate(sourcePath, temporaryPath);
            try
            {
                File.Move(temporaryPath, destinationPath);
            }
            catch (IOException exception) when (File.Exists(destinationPath))
            {
                throw new SaveNameConflictException(destinationPath, exception);
            }
            return destinationPath;
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    private static void CopyAndValidate(string sourcePath, string temporaryPath)
    {
        using FileStream source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        long sourceLength = source.Length;
        using (FileStream destination = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            source.CopyTo(destination);
            destination.Flush();
            if (source.Length != sourceLength || destination.Length != sourceLength)
                throw new IOException("The save changed or was incomplete while it was copied.");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

public sealed class SaveNameConflictException : IOException
{
    public SaveNameConflictException(string destinationPath, Exception? innerException = null)
        : base($"A save named '{Path.GetFileName(destinationPath)}' already exists.", innerException)
    {
    }
}
