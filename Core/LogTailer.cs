using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SCLogReader.Core;

/// <summary>
/// Liest die Game.log live mit, OHNE Star Citizen zu stören.
/// Entscheidend: FileShare.ReadWrite | FileShare.Delete, sonst "Access denied",
/// weil das Spiel die Datei offen hält. Erkennt Log-Rotation (Neustart von SC).
/// </summary>
public class LogTailer
{
    readonly string _path;
    long _pos;
    CancellationTokenSource? _cts;

    public event Action<string>? Line;
    public event Action<string>? Status;

    public LogTailer(string path) => _path = path;

    public void Start(bool fromStart = true)
    {
        Stop();
        _cts = new CancellationTokenSource();
        _pos = fromStart ? 0 : -1;   // -1 = beim ersten Lauf ans Ende springen
        var token = _cts.Token;
        Task.Run(() => LoopAsync(token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
    }

    async Task LoopAsync(CancellationToken ct)
    {
        bool first = true;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!File.Exists(_path))
                {
                    Status?.Invoke("warte auf Datei…");
                }
                else
                {
                    using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);

                    if (first && _pos < 0) { _pos = fs.Length; first = false; }
                    first = false;

                    if (fs.Length < _pos) { _pos = 0; Status?.Invoke("Log rotiert – neu eingelesen"); }

                    fs.Seek(_pos, SeekOrigin.Begin);
                    using var sr = new StreamReader(fs);
                    string? l;
                    while ((l = await sr.ReadLineAsync()) != null)
                        Line?.Invoke(l);

                    _pos = fs.Position;
                    Status?.Invoke($"live · {DateTime.Now:HH:mm:ss}");
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { Status?.Invoke("Fehler: " + ex.Message); }

            try { await Task.Delay(500, ct); }
            catch (OperationCanceledException) { break; }
        }
    }
}
