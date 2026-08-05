using System.Diagnostics;

namespace RootCli;

internal sealed class ThinkingPane : IDisposable
{
    private readonly object gate = new();
    private readonly Stopwatch watch = new();
    private System.Threading.Timer? timer;
    private int startTop = -1;
    private int logLines;
    private bool active;
    private bool disposed;

    public void Begin()
    {
        lock (gate)
        {
            DisposeTimer_NoLock();
            try
            {
                startTop = Console.CursorTop;
            }
            catch
            {
                startTop = -1;
            }

            logLines = 0;
            active = true;
            watch.Restart();
            WriteTimerLine_NoLock();
            timer = new System.Threading.Timer(static state => ((ThinkingPane)state!).Tick(), this, 1000, 1000);
        }
    }

    public void Log(string message)
    {
        lock (gate)
        {
            if (!active || startTop < 0)
            {
                TermUi.Write("  · ", TermUi.Dim);
                TermUi.WriteLine(message, TermUi.Dim);
                return;
            }

            var row = startTop + logLines;
            try
            {
                var width = Math.Max(20, Console.BufferWidth - 1);
                Console.SetCursorPosition(0, row);
                Console.Write(new string(' ', width));
                Console.SetCursorPosition(0, row);
                TermUi.Write("  · ", TermUi.Dim);
                TermUi.Write(message, TermUi.Dim);
                if (message.Length + 4 < width)
                {
                    Console.Write(new string(' ', width - message.Length - 4));
                }

                logLines++;
                WriteTimerLine_NoLock();
            }
            catch
            {
                TermUi.Write("  · ", TermUi.Dim);
                TermUi.WriteLine(message, TermUi.Dim);
                logLines++;
            }
        }
    }

    public void ReplaceWithAnswer()
    {
        lock (gate)
        {
            DisposeTimer_NoLock();
            if (!active)
            {
                return;
            }

            if (startTop >= 0)
            {
                try
                {
                    var width = Math.Max(20, Console.BufferWidth - 1);
                    var rows = logLines + 1;
                    for (var i = 0; i < rows + 1; i++)
                    {
                        var top = startTop + i;
                        if (top >= Console.BufferHeight)
                        {
                            break;
                        }

                        Console.SetCursorPosition(0, top);
                        Console.Write(new string(' ', width));
                    }

                    Console.SetCursorPosition(0, startTop);
                }
                catch
                {

                }
            }

            active = false;
            logLines = 0;
            watch.Stop();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        lock (gate)
        {
            DisposeTimer_NoLock();
            active = false;
        }
    }

    private void Tick()
    {
        lock (gate)
        {
            if (!active || startTop < 0)
            {
                return;
            }

            try
            {
                WriteTimerLine_NoLock();
            }
            catch
            {

            }
        }
    }

    private void WriteTimerLine_NoLock()
    {
        var row = startTop + logLines;
        if (row < 0 || row >= Console.BufferHeight)
        {
            return;
        }

        var width = Math.Max(20, Console.BufferWidth - 1);
        int left = 0, top = 0;
        try
        {
            left = Console.CursorLeft;
            top = Console.CursorTop;
        }
        catch
        {

        }

        var elapsed = watch.Elapsed;
        var clock = $"{elapsed.Hours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
        var line = "  elapsed  " + clock;

        Console.SetCursorPosition(0, row);
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = TermUi.Dim;
        if (line.Length >= width)
        {
            Console.Write(line[..(width - 1)]);
        }
        else
        {
            Console.Write(line + new string(' ', width - line.Length));
        }

        Console.ForegroundColor = prev;

        try
        {

            if (top > row)
            {
                Console.SetCursorPosition(left, top);
            }
            else
            {
                Console.SetCursorPosition(0, row + 1);
            }
        }
        catch
        {

        }
    }

    private void DisposeTimer_NoLock()
    {
        if (timer == null)
        {
            return;
        }

        try
        {
            timer.Dispose();
        }
        catch
        {

        }

        timer = null;
    }
}
