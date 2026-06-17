using System;
using Velopack;

namespace NovaBrowser.App;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build()
            .SetArgs(args)
            .SetAutoApplyOnStartup(true)
            .Run();

        App.SetStartupArgs(args);
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
