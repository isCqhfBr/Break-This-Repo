using ReaderPro.Engine.Data;
using Xunit;

namespace ReaderPro.Tests;

public class SettingsStoreTests
{
    [Fact]
    public void Save_Load_RoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "readerpro_set_test_" + Guid.NewGuid().ToString("N"));
        var store = new SettingsStore(dir);
        var s = store.Load();
        Assert.Equal(20, s.FontSize);
        s.FontSize = 28;
        s.Theme = "Night";
        s.SpeechRate = 3;
        s.SpeechVolume = 75;
        s.VoiceName = "TestVoice";
        store.Save(s);

        var reloaded = new SettingsStore(dir).Load();
        Assert.Equal(28, reloaded.FontSize);
        Assert.Equal("Night", reloaded.Theme);
        Assert.Equal(3, reloaded.SpeechRate);
        Assert.Equal(75, reloaded.SpeechVolume);
        Assert.Equal("TestVoice", reloaded.VoiceName);
        Directory.Delete(dir, true);
    }

    [Fact]
    public void MissingFile_ReturnsDefaults()
    {
        var dir = Path.Combine(Path.GetTempPath(), "readerpro_set_miss_" + Guid.NewGuid().ToString("N"));
        var s = new SettingsStore(dir).Load();
        Assert.InRange(s.FontSize, 12, 48);
        Assert.InRange(s.SpeechVolume, 0, 100);
        Directory.Delete(dir, true);
    }

    [Fact]
    public void CorruptFile_FallsBackToDefaults()
    {
        var dir = Path.Combine(Path.GetTempPath(), "readerpro_set_bad_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "settings.json"), "{ this is not json");
        var s = new SettingsStore(dir).Load();
        Assert.Equal(20, s.FontSize);   // 损坏回默认，不抛异常
        Directory.Delete(dir, true);
    }
}
