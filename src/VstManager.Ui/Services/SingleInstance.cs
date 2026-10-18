using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;

namespace VstManager.Ui.Services;

/// <summary>
/// Lets a second launch hand over to the running instance instead of just saying "already
/// running": the running app listens on a named pipe, a relaunch connects and asks it to show
/// itself, then exits. Named pipes are Unix domain sockets on macOS (UNVERIFIED there).
/// </summary>
public static class SingleInstance
{
    private const string ShowMessage = "show";

    /// <summary>Runs for the app's lifetime in the instance that owns the mutex.</summary>
    public static void StartListening(string pipeName, Action onShowRequested)
    {
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(pipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync();
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    if (await reader.ReadLineAsync() == ShowMessage)
                    {
                        onShowRequested();
                    }
                }
                catch (Exception)
                {
                    // A broken connection must not end the listener; brief pause avoids a spin.
                    await Task.Delay(500);
                }
            }
        });
    }

    /// <summary>
    /// Asks the running instance to come to the front. False when nothing answered (e.g. the WPF
    /// app holds the shared mutex), so the caller can fall back to its message.
    /// </summary>
    public static bool TryActivateRunningInstance(string pipeName)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // Windows only lets the foreground process hand focus over; this launch is in the
                // foreground (the user just started it), so it grants that to the running one.
                AllowSetForegroundWindow(AsfwAny);
            }

            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
            client.Connect(1500);
            using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
            writer.WriteLine(ShowMessage);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private const int AsfwAny = -1;

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);
}
