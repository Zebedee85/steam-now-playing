using System.Diagnostics;
using SteamNowPlaying.Core;
using SteamNowPlaying.Spotify;

namespace SteamNowPlaying.UI;

sealed class SettingsForm : Form
{
    const string ClientIdHelpUrl = BuildInfo.RepoUrl + "#using-your-own-spotify-app";

    readonly AppSettings settings;
    readonly TextBox txtTemplate = new();
    readonly TextBox txtIdle = new();
    readonly CheckBox chkOnline = new();
    readonly CheckBox chkStartup = new();
    readonly TextBox txtClientId = new();

    public SettingsForm(AppSettings settings)
    {
        this.settings = settings;

        Text = "Settings";
        Font = SystemFonts.MessageBoxFont ?? Font;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.None;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        Build();

        txtTemplate.Text = settings.StatusTemplate;
        txtIdle.Text = settings.IdleText;
        chkOnline.Checked = settings.GoOnline;
        chkStartup.Checked = StartupRegistration.IsEnabled;
        txtClientId.Text = settings.SpotifyClientId;
    }

    int S(int px) => (int)Math.Round(px * DeviceDpi / 96.0);

    void Build()
    {
        SuspendLayout();
        var width = S(400);

        var root = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Padding = new Padding(S(16), S(12), S(16), S(12)),
        };

        var row = 0;
        void Add(Control c) => root.Controls.Add(c, 0, row++);

        Label Caption(string text) => new()
        {
            Text = text,
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(0, S(8), 0, S(2)),
        };

        Label Hint(string text) => new()
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(width, 0),
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, S(2), 0, S(4)),
        };

        void Box(TextBox box)
        {
            box.Width = width;
            box.Margin = new Padding(0, 0, 0, S(2));
        }

        Add(Caption("Status text"));
        Box(txtTemplate);
        Add(txtTemplate);
        Add(Hint("Use {track}, {artist} and {album}. Default: " + StatusFormatter.DefaultTemplate));

        Add(Caption("When nothing's playing, show"));
        Box(txtIdle);
        Add(txtIdle);
        Add(Hint("Leave blank to clear the status, so Steam shows you normally."));

        Add(Caption("Steam"));
        chkOnline.Text = "Set me to Online when this app signs in";
        chkOnline.AutoSize = true;
        chkOnline.Margin = new Padding(0, S(2), 0, 0);
        Add(chkOnline);
        Add(Hint("Untick if you use Invisible or Offline: otherwise this app would switch you back to Online."));

        chkStartup.Text = "Start with Windows (in the tray)";
        chkStartup.AutoSize = true;
        chkStartup.Margin = new Padding(0, S(4), 0, S(4));
        Add(chkStartup);

        Add(Caption("Spotify Client ID"));
        Box(txtClientId);
        Add(txtClientId);
        var clientHint = BuildInfo.DefaultSpotifyClientId is null
            ? "This copy has no built-in Spotify app, so you need your own Client ID."
            : "Leave blank to use the one built into this app.";
        Add(Hint(clientHint + $" Your own app's redirect URI must be {SpotifyClient.RedirectUri}"));

        var help = new LinkLabel { Text = "How to make your own Spotify app", AutoSize = true, Margin = new Padding(0, 0, 0, S(4)) };
        help.LinkClicked += (_, _) => Open(ClientIdHelpUrl);
        Add(help);

        var folder = new LinkLabel { Text = "Open the settings and log folder", AutoSize = true, Margin = new Padding(0, S(8), 0, 0) };
        folder.LinkClicked += (_, _) =>
        {
            Directory.CreateDirectory(AppSettings.Folder);
            Open(AppSettings.Folder);
        };
        Add(folder);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(0, S(14), 0, 0),
        };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(S(88), 0) };
        var save = new Button { Text = "Save", AutoSize = true, MinimumSize = new Size(S(88), 0), Margin = new Padding(0, 0, S(8), 0) };
        save.Click += (_, _) => SaveAndClose();
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(save);
        Add(buttons);

        AcceptButton = save;
        CancelButton = cancel;

        Controls.Add(root);
        ResumeLayout(false);
        PerformLayout();
    }

    void SaveAndClose()
    {
        var template = txtTemplate.Text.Trim();
        settings.StatusTemplate = template.Length == 0 ? StatusFormatter.DefaultTemplate : template;
        settings.IdleText = txtIdle.Text.Trim();
        settings.GoOnline = chkOnline.Checked;
        settings.SpotifyClientId = txtClientId.Text.Trim();
        settings.Save();

        if (chkStartup.Checked != StartupRegistration.IsEnabled)
        {
            StartupRegistration.Set(chkStartup.Checked);
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    static void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Write("Couldn't open " + target + ": " + ex.Message);
        }
    }
}
