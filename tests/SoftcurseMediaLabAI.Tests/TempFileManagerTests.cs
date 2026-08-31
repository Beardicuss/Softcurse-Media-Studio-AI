using Xunit;

namespace SoftcurseMediaLabAI.Tests;

public sealed class TempFileManagerTests
{
    [Fact]
    public void CreatedPathsStayInsideAppOwnedDirectory()
    {
        string path = TempFileManager.CreateTempPath("unsafe:name", "png");
        try
        {
            Assert.StartsWith(Path.GetFullPath(TempFileManager.TempDirectory), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(".png", Path.GetExtension(path));
        }
        finally
        {
            TempFileManager.CleanupAll();
        }
    }

    [Fact]
    public void CleanupDeletesOnlyRegisteredFile()
    {
        string registered = Path.Combine(Path.GetTempPath(), $"softcurse_registered_{Guid.NewGuid():N}.tmp");
        string unregistered = Path.Combine(Path.GetTempPath(), $"softcurse_unregistered_{Guid.NewGuid():N}.tmp");
        File.WriteAllText(registered, "temporary");
        File.WriteAllText(unregistered, "keep");

        try
        {
            TempFileManager.RegisterTempFile(registered);
            TempFileManager.CleanupAll();

            Assert.False(File.Exists(registered));
            Assert.True(File.Exists(unregistered));
        }
        finally
        {
            if (File.Exists(registered)) File.Delete(registered);
            if (File.Exists(unregistered)) File.Delete(unregistered);
        }
    }
}
