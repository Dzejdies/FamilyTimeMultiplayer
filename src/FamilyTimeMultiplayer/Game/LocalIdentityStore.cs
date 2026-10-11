using System;
using System.IO;
using BepInEx;
using FamilyTimeMultiplayer.Core.Security;

namespace FamilyTimeMultiplayer.Game;

public static class LocalIdentityStore
{
    public static PlayerIdentity LoadOrCreate()
    {
        string path = Path.Combine(Paths.ConfigPath, "ftmp-player.key");
        if (File.Exists(path)) { using var stream = File.OpenRead(path); return PlayerIdentity.Read(stream); }
        Directory.CreateDirectory(Paths.ConfigPath);
        PlayerIdentity created = PlayerIdentity.Create();
        string temporary = path + ".new." + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { created.Write(stream); stream.Flush(true); }
            try { File.Move(temporary, path); return created; }
            catch (IOException) when (File.Exists(path))
            {
                created.Dispose();
                using var stream = File.OpenRead(path);
                return PlayerIdentity.Read(stream);
            }
        }
        catch { created.Dispose(); throw; }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
