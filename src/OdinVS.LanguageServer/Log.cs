namespace OdinVS.LanguageServer;

internal sealed class Log(string path)
{
    private readonly object gate = new();

    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OdinVS");

    public static Log Default { get; } = new(Path.Combine(DataDirectory, "odinvs.log"));

    public void Write(string message)
    {
        try
        {
            lock (gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch (IOException)
        {
        }
    }
}
