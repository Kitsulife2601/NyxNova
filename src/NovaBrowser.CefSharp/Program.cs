using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using Velopack;

namespace NovaBrowser.App;

public static class Program
{
    public const string SingleInstancePipeName = "NyxNovaBrowser.ActivationPipe";

    [STAThread]
    public static void Main(string[] args)
    {
        using var singleInstance = new Mutex(initiallyOwned: true, name: "NyxNovaBrowser.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            ForwardArgsToRunningInstance(args);
            return;
        }

        VelopackApp.Build()
            .SetArgs(args)
            .SetAutoApplyOnStartup(true)
            .Run();

        App.SetStartupArgs(args);
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    private static void ForwardArgsToRunningInstance(string[] args)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", SingleInstancePipeName, PipeDirection.Out);
            client.Connect(1500);
            using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
            foreach (var arg in args)
            {
                writer.WriteLine(arg);
            }
        }
        catch
        {
            // Die laufende Instanz war nicht erreichbar; der Zweitstart wird einfach verworfen.
        }
    }
}
