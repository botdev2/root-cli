
using System.Diagnostics;
using System.Text;

namespace RootCli.Core.Mcp;

internal sealed class McpStdioTransport : IDisposable
{
    private readonly object writeLock = new();
    private Process? process;
    private StreamWriter? stdin;
    private StreamReader? stdout;
    private StreamReader? stderr;
    private Thread? readerThread;
    private Thread? stderrThread;
    private volatile bool running;

    public event Action<string>? MessageReceived;
    public string? LastError { get; private set; }

    public bool IsRunning =>
        running && process != null && !process.HasExited;

    public bool Start(string command, string arguments, string? workingDirectory)
    {
        Stop();
        LastError = null;

        if (string.IsNullOrWhiteSpace(command) || !File.Exists(command))
        {
            LastError = "MCP executable not found: " + (command ?? "(empty)");
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments ?? "",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,

                StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            };

            if (!string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory))
            {
                startInfo.WorkingDirectory = workingDirectory;
            }

            process = Process.Start(startInfo);
            if (process == null)
            {
                LastError = "Failed to start MCP process.";
                return false;
            }

            stdin = process.StandardInput;
            stdout = process.StandardOutput;
            stderr = process.StandardError;
            running = true;
            readerThread = new Thread(ReadLoop) { IsBackground = true, Name = "RootCliMcpStdioReader" };
            readerThread.Start();
            stderrThread = new Thread(DrainStderr) { IsBackground = true, Name = "RootCliMcpStderr" };
            stderrThread.Start();
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Stop();
            return false;
        }
    }

    public void WriteMessage(string json)
    {
        if (!IsRunning || stdin == null || string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        lock (writeLock)
        {
            stdin.AutoFlush = true;
            stdin.Write(json);
            stdin.Write('\n');
            stdin.Flush();
        }
    }

    public void Stop()
    {
        running = false;
        try
        {
            stdin?.Close();
        }
        catch
        {

        }

        try
        {
            if (process != null && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(2000);
            }
        }
        catch
        {

        }

            process = null;
            stdin = null;
            stdout = null;
            stderr = null;
        }

        public void Dispose() => Stop();

        private void DrainStderr()
        {
            try
            {
                while (running && stderr != null)
                {
                    var line = stderr.ReadLine();
                    if (line == null)
                    {
                        break;
                    }
                }
            }
            catch
            {

            }
        }

        private void ReadLoop()
        {
        try
        {
            while (running && process != null && !process.HasExited && stdout != null)
            {
                var message = ReadFramedMessage();
                if (message == null)
                {
                    if (process.HasExited)
                    {
                        break;
                    }

                    Thread.Sleep(10);
                    continue;
                }

                MessageReceived?.Invoke(message);
            }
        }
        catch
        {

        }
    }

    private string? ReadFramedMessage()
    {
        if (stdout == null)
        {
            return null;
        }

        var line = stdout.ReadLine();
        if (line == null)
        {
            return null;
        }

        if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
        {
            var lengthText = line.Substring("Content-Length:".Length).Trim();
            if (!int.TryParse(lengthText, out var contentLength) || contentLength <= 0)
            {
                return null;
            }

            while (true)
            {
                var headerLine = stdout.ReadLine();
                if (headerLine == null)
                {
                    return null;
                }

                if (headerLine.Length == 0)
                {
                    break;
                }
            }

            var buffer = new char[contentLength];
            var read = 0;
            while (read < contentLength)
            {
                var chunk = stdout.Read(buffer, read, contentLength - read);
                if (chunk <= 0)
                {
                    return null;
                }

                read += chunk;
            }

            return new string(buffer, 0, read);
        }

        return line.Trim();
    }
}
