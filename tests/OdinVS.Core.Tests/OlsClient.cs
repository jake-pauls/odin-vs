using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OdinVS.Core.Tests;

// Minimal Content-Length framed JSON-RPC client for talking to a real ols over stdio (tier-2 tests).
internal sealed class OlsClient : IDisposable
{
    private readonly Process process;
    private readonly List<string> stderr = [];
    private readonly List<JsonNode> notifications = [];
    private readonly System.Collections.Concurrent.BlockingCollection<JsonNode> frames = [];
    private int nextId = 1;

    private OlsClient(Process process) => this.process = process;

    public IReadOnlyList<JsonNode> Notifications => notifications;

    public IReadOnlyList<string> Stderr
    {
        get
        {
            lock (stderr)
            {
                return [.. stderr];
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
                lock (client.stderr)
                {
                    client.stderr.Add(e.Data);
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
                    client.frames.Add(client.ReadFrame());
                }
            }
            catch (IOException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                client.frames.CompleteAdding();
            }
        });
        return client;
    }

    public JsonNode Request(string method, JsonNode? parameters, TimeSpan timeout)
    {
        int id = nextId++;
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
        foreach (JsonNode held in notifications)
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
        try
        {
            Request("shutdown", null, TimeSpan.FromSeconds(5));
            Notify("exit", null);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException)
        {
        }

        if (!process.WaitForExit(5000))
        {
            process.Kill();
            process.WaitForExit();
            return -1;
        }

        return process.ExitCode;
    }

    public void Dispose()
    {
        if (!process.HasExited)
        {
            process.Kill();
        }

        process.Dispose();
    }

    private static bool Matches(JsonNode message, bool requireNonEmpty) =>
        message["method"]?.GetValue<string>() == "textDocument/publishDiagnostics"
        && (!requireNonEmpty || message["params"]?["diagnostics"]?.AsArray().Count > 0);

    private void Remember(JsonNode message)
    {
        if (message["method"] is not null)
        {
            notifications.Add(message);
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
        Stream stdin = process.StandardInput.BaseStream;
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
            if (frames.TryTake(out JsonNode? frame, timeout))
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
        Stream stdout = process.StandardOutput.BaseStream;
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

            if (b == '\n')
            {
                return line.ToString().TrimEnd('\r');
            }

            line.Append((char)b);
        }
    }
}
