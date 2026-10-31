using System.Speech.Recognition;
using System.Speech.Synthesis;

namespace Mindie.Services;

public sealed class SpeechService : IDisposable
{
    private SpeechRecognitionEngine? _recognizer;
    private SpeechSynthesizer? _synthesizer;
    public bool Available { get; }
    public SpeechService() { try { Available = SpeechRecognitionEngine.InstalledRecognizers().Any(r => r.Culture.TwoLetterISOLanguageName == "es"); } catch { Available = false; } }
    public async Task<string> ListenAsync(CancellationToken cancellation)
    {
        var info = SpeechRecognitionEngine.InstalledRecognizers().FirstOrDefault(r => r.Culture.TwoLetterISOLanguageName == "es") ?? throw new InvalidOperationException("Instala reconocimiento de voz en español en Configuración de Windows → Hora e idioma → Voz.");
        if (_recognizer is not null) throw new InvalidOperationException("Ya hay una escucha en curso.");
        using var recognizer = new SpeechRecognitionEngine(info); _recognizer = recognizer;
        try
        {
            recognizer.LoadGrammar(new DictationGrammar()); recognizer.SetInputToDefaultAudioDevice();
            recognizer.InitialSilenceTimeout = TimeSpan.FromSeconds(8); recognizer.EndSilenceTimeout = TimeSpan.FromMilliseconds(1000);
            var result = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            recognizer.RecognizeCompleted += (_, e) => { if (e.Error is not null) result.TrySetException(e.Error); else if (e.Cancelled) result.TrySetCanceled(); else if (e.Result is not null) result.TrySetResult(e.Result.Text); else result.TrySetException(new InvalidOperationException("No alcancé a escucharte. Revisa el micrófono e intenta de nuevo.")); };
            using var registration = cancellation.Register(() => { try { recognizer.RecognizeAsyncCancel(); } catch { } result.TrySetCanceled(cancellation); });
            recognizer.RecognizeAsync(RecognizeMode.Single);
            return await result.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellation);
        }
        catch (InvalidOperationException) { throw; }
        catch (Exception ex) when (ex is not OperationCanceledException) { throw new InvalidOperationException("No pude escuchar el micrófono. Revisa sus permisos y el paquete de voz de Windows.", ex); }
        finally { _recognizer = null; }
    }
    public void Speak(string text)
    {
        try { _synthesizer ??= new(); var voice = _synthesizer.GetInstalledVoices().FirstOrDefault(v => v.Enabled && v.VoiceInfo.Culture.TwoLetterISOLanguageName == "es"); if (voice is null) return; _synthesizer.SelectVoice(voice.VoiceInfo.Name); _synthesizer.SpeakAsyncCancelAll(); _synthesizer.SpeakAsync(text); } catch { }
    }
    public void Dispose() { _recognizer?.RecognizeAsyncCancel(); _recognizer?.Dispose(); _synthesizer?.Dispose(); }
}
