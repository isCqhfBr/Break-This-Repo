using System.Speech.Synthesis;

namespace ReaderPro.Services;

/// <summary>朗读单元：所属章节 + 段落索引 + 文本。</summary>
public readonly record struct SpeakUnit(int Chapter, int Paragraph, string Text);

/// <summary>
/// 听书服务：System.Speech（Windows 内置 SAPI 语音，离线可用，PRD 3.10 TTS）。
/// 支持跨章节连续朗读、暂停/恢复/停止、语速调节；按单元回调进度（章节+段落，驱动阅读位置跟随）。
/// </summary>
public sealed class TtsService : IDisposable
{
    private readonly SpeechSynthesizer _synth = new();
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private Task? _task;
    private int _rate = 0;          // -10..10
    private int _volume = 100;

    public bool IsSpeaking { get; private set; }
    public bool IsPaused { get; private set; }
    public event Action<int, int>? UnitStarted;   // (chapter, paragraph)

    public TtsService()
    {
        try { _synth.Rate = _rate; } catch { }
        try { _synth.Volume = _volume; } catch { }
    }

    public string[] InstalledVoices =>
        _synth.GetInstalledVoices()
              .Where(v => v.Enabled)
              .Select(v => v.VoiceInfo.Name)
              .ToArray();

    public void SetRate(int rate)
    {
        _rate = Math.Clamp(rate, -10, 10);
        try { _synth.Rate = _rate; } catch { }
    }

    public void SetVolume(int volume)
    {
        _volume = Math.Clamp(volume, 0, 100);
        try { _synth.Volume = _volume; } catch { }
    }

    public void SetVoice(string name)
    {
        try { _synth.SelectVoice(name); } catch { }
    }

    /// <summary>开始朗读单元队列（后台线程逐单元 + 回调进度；跨章节连续）。</summary>
    public void Speak(IReadOnlyList<SpeakUnit> units, int startIndex = 0)
    {
        Stop();
        if (units.Count == 0 || startIndex >= units.Count) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        IsSpeaking = true;
        _task = Task.Run(() =>
        {
            try
            {
                for (int i = startIndex; i < units.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    UnitStarted?.Invoke(units[i].Chapter, units[i].Paragraph);
                    _synth.Speak(units[i].Text);
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                IsSpeaking = false;
            }
        });
    }

    public void Pause()
    {
        _synth.Pause();
        IsPaused = true;
    }

    public void Resume()
    {
        _synth.Resume();
        IsPaused = false;
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _synth.SpeakAsyncCancelAll(); } catch { }
        try { _synth.Resume(); } catch { }
        IsSpeaking = false;
        IsPaused = false;
        _task = null;
    }

    public void Dispose()
    {
        Stop();
        _synth.Dispose();
    }
}
