using ReaderPro.Engine.Data;
using Xunit;

namespace ReaderPro.Tests;

public class BookRepositoryTests
{
    private static string NewDataDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rprepo_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void AddFindAndPersist_RoundTrip()
    {
        var dir = NewDataDir();
        try
        {
            var repo = new BookRepository(dir);
            var b = new BookRecord { Title = "测试书", Author = "作者", Format = "txt", Path = @"C:\x\a.txt" };
            repo.AddOrUpdate(b);
            repo.SaveProgress(b.Id, 3, 0.5);
            repo.Save();

            var repo2 = new BookRepository(dir);
            var loaded = repo2.Find(b.Id);
            Assert.NotNull(loaded);
            Assert.Equal("测试书", loaded!.Title);
            var p = repo2.GetProgress(b.Id);
            Assert.NotNull(p);
            Assert.Equal(3, p!.Chapter);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ScanFolder_FindsSupportedFilesOnly()
    {
        var dir = NewDataDir();
        try
        {
            var sub = Path.Combine(dir, "子目录");
            Directory.CreateDirectory(sub);
            File.WriteAllText(Path.Combine(dir, "a.txt"), "x");
            File.WriteAllText(Path.Combine(sub, "b.txt"), "x");
            File.WriteAllText(Path.Combine(dir, "c.pdf"), "x");
            File.WriteAllText(Path.Combine(dir, ".hidden.txt"), "x");

            var repo = new BookRepository(dir);
            var found = repo.ScanFolder(dir);
            Assert.Equal(2, found.Count);
            Assert.All(found, p => Assert.EndsWith(".txt", p));
            Assert.DoesNotContain(found, p => Path.GetFileName(p).StartsWith('.'));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
