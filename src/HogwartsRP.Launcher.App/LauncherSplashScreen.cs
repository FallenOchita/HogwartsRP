using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace HogwartsRP.Launcher.App;

internal sealed class LauncherSplashScreen : Form
{
    private readonly System.Windows.Forms.Timer _closeTimer = new() { Interval = 1800 };
    private Image? _splashImage;

    public LauncherSplashScreen()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = false;
        ClientSize = new Size(1100, 619);
        BackColor = Color.FromArgb(12, 16, 29);
        TopMost = true;

        var imagePath = Path.Combine(AppContext.BaseDirectory, "Assets", "HogwartsRP-Splash.jpg");
        if (File.Exists(imagePath))
        {
            _splashImage = Image.FromFile(imagePath);
            Controls.Add(new PictureBox
            {
                Dock = DockStyle.Fill,
                Image = _splashImage,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = BackColor
            });
        }
        else
        {
            Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = "HOGWARTSRP\\nA NEW CHAPTER AWAITS",
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(224, 190, 112),
                Font = new Font("Georgia", 28F, FontStyle.Bold)
            });
        }

        _closeTimer.Tick += (_, _) => Close();
        Shown += (_, _) => _closeTimer.Start();
        FormClosed += (_, _) => _closeTimer.Stop();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _closeTimer.Dispose();
            _splashImage?.Dispose();
        }
        base.Dispose(disposing);
    }
}
