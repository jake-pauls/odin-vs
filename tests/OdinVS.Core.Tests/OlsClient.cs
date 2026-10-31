using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OdinVS.Core.Tests;

// Minimal Content-Length framed JSON-RPC client for talking to a real ols over stdio (tier-2 tests).
internal sealed class OlsClient : IDisposable
{
    private readonly Process m_Process;
    private readonly List<string> m_Stderr = [];
    private readonly List<JsonNode> m_Notifications = [];
    private readonly System.Collections.Concurrent.BlockingCollection<JsonNode> m_Frames = [];
    private int m_NextId = 1;

    private OlsClient(Process process) => m_Process = process;

    public IReadOnlyList<JsonNode> Notifications => m_Notifications;

    // Why the last Shutdown() did not complete cleanly, with a hint; null if it did. Include it in assertion messages.
    public string? ShutdownProblem { get; private set; }

    public IReadOnlyList<string> Stderr
    {
        get
        {
            lock (m_Stderr)
            {
                return [.. m_Stderr];
            }
        }
    }

    public static OlsClient Start(string olsPath, string workingDirectory)
    {
        Process process = new()
        {
            StartInfo = new ProcessStartInfo(olsPath)
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };

        OlsClient client = new(process);
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (client.m_Stderr)
                {
                    client.m_Stderr.Add(e.Data);
                }
            }
        };
        process.Start();
        process.BeginErrorReadLine();
        Task.Run(() =>
        {
            try
            {
                while (true)
                {
                    client.m_Frames.Add(client.ReadFrame());
                }
            }
            catch (IOException)
            {
                // Expected end of the stream: ols exited or closed stdout. Read() reports it to the test as an IOException.
            }
            catch (ObjectDisposedException)
            {
                // Expected when the test disposes the client while this thread is still blocked on a read.
            }
            finally
            {
                client.m_Frames.CompleteAdding();
            }
        });
        return client;
    }

    public JsonNode Request(string method, JsonNode? parameters, TimeSpan timeout)
    {
        int id = m_NextId++;
        Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters });
        DateTime deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            JsonNode message = Read(deadline - DateTime.UtcNow);
            if (message["id"]?.GetValue<int>() == id && message["method"] is null)
            {
                return message;
            }

            Remember(message);
        }
    }

    public void Notify(string method, JsonNode? parameters) =>
        Send(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = parameters });

    // Waits for a publishDiagnostics notification whose diagnostics array is non-empty (or any, if requireNonEmpty is false).
    public JsonNode? WaitForDiagnostics(TimeSpan timeout, bool requireNonEmpty)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        foreach (JsonNode held in m_Notifications)
        {
            if (Matches(held, requireNonEmpty))
            {
                return held;
            }
        }

        while (DateTime.UtcNow < deadline)
        {
            JsonNode message;
            try
            {
                message = Read(deadline - DateTime.UtcNow);
            }
            catch (TimeoutException)
            {
                return null;
            }

            Remember(message);
            if (Matches(message, requireNonEmpty))
            {
                return message;
            }
        }

        return null;
    }

    public int Shutdown()
    {
        ShutdownProblem = null;
        try
        {
            Request("shutdown", null, TimeSpan.FromSeconds(5));
            Notify("exit", null);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException)
        {
            ShutdownProblem = $"ols did not complete shutdown/exit ({ex.GetType().Name}: {ex.Message}). It probably crashed or hung; check Stderr and the window/logMessage notifications.";
        }

        if (!m_Process.WaitForExit(5000))
        {
            m_Process.Kill();
            m_Process.WaitForExit();
            ShutdownProblem ??= "ols did not exit within 5 seconds of the exit notification and was killed.";
            return -1;
        }

        return m_Process.ExitCode;
    }

    public void Dispose()
    {
        if (!m_Process.HasExited)
        {
            m_Process.Kill();
        }

        m_Process.Dispose();
    }

    private static bool Matches(JsonNode message, bool requireNonEmpty) =>
        message["method"]?.GetValue<string>() is "textDocument/publishDiagnostics"
        && (!requireNonEmpty || message["params"]?["diagnostics"]?.AsArray().Count > 0);

    private void Remember(JsonNode message)
    {
        if (message["method"] is not null)
        {
            m_Notifications.Add(message);
            // Answer server-to-client requests (e.g. window/workDoneProgress/create) so ols does not block.
            if (message["id"] is JsonNode id)
            {
                Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id.DeepClone(), ["result"] = null });
            }
        }
    }

    private void Send(JsonNode message)
    {
        byte[] body = Encoding.UTF8.GetBytes(message.ToJsonString());
        byte[] header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        Stream stdin = m_Process.StandardInput.BaseStream;
        stdin.Write(header);
        stdin.Write(body);
        stdin.Flush();
    }

    private JsonNode Read(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new TimeoutException("No message from ols in time.");
        }

        try
        {
            if (m_Frames.TryTake(out JsonNode? frame, timeout))
            {
                return frame;
            }
        }
        catch (InvalidOperationException)
        {
            throw new IOException("ols closed stdout.");
        }

        throw new TimeoutException("No message from ols in time.");
    }

    private JsonNode ReadFrame()
    {
        Stream stdout = m_Process.StandardOutput.BaseStream;
        int length = 0;
        while (true)
        {
            string line = ReadLine(stdout);
            if (line.Length == 0)
            {
                break;
            }

            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                length = int.Parse(line["Content-Length:".Length..].Trim());
            }
        }

        byte[] body = new byte[length];
        int read = 0;
        while (read < length)
        {
            int n = stdout.Read(body, read, length - read);
            if (n == 0)
            {
                throw new IOException("ols closed stdout.");
            }

            read += n;
        }

        return JsonNode.Parse(body)!;
    }

    private static string ReadLine(Stream stream)
    {
        StringBuilder line = new();
        while (true)
        {
            int b = stream.ReadByte();
            if (b < 0)
            {
                throw new IOException("ols closed stdout.");
            }

            if (b is '\n')
            {
                return line.ToString().TrimEnd('\r');
            }

            line.Append((char)b);
        }
    }
}
