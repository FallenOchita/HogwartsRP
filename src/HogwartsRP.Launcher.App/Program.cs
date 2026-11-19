using System.Windows.Forms;

namespace HogwartsRP.Launcher.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        using (var splash = new LauncherSplashScreen())
            splash.ShowDialog();
        Application.Run(new LauncherWindow());
    }
}
