using System.Diagnostics;
using SteamNowPlaying.Core;
using SteamNowPlaying.Steam;

namespace SteamNowPlaying.UI;

sealed class MainForm : Form
{
    enum SteamAction { SignIn, Cancel, SignOut }
    enum SpotifyAction { OpenSettings, Connect, Cancel, Disconnect }

    readonly AppSettings settings;
    readonly StatusSync sync;

    readonly Label lblHeading = new();
    readonly Label lblStatus = new();
    readonly Label lblSub = new();

    readonly GroupBox grpSteam = new();
    readonly Label lblSteam = new();
    readonly Button btnSteam = new();
    readonly PictureBox picQr = new();
    readonly Label lblSteamHint = new();

    readonly GroupBox grpSpotify = new();
    readonly Label lblSpotify = new();
    readonly Button btnSpotify = new();
    readonly Label lblSpotifyProblem = new();

    readonly CheckBox chkPause = new();
    readonly Button btnSettings = new();
    readonly LinkLabel lnkAbout = new();

    readonly NotifyIcon tray = new();
    readonly ToolStripMenuItem trayPause = new("Pause sharing");

    bool allowVisible;
    bool exiting;
    bool started;
    int refreshQueued;
    string? shownQrUrl;
    SteamAction steamAction;
    SpotifyAction spotifyAction;
    bool updatingPause;

    public MainForm(AppSettings settings, bool startHidden)
    {
        this.settings = settings;
        sync = new StatusSync(settings);

        // Start in the tray only if there's nothing to set up.
        allowVisible = !startHidden || !sync.Steam.HasSavedLogin || sync.Spotify?.IsConnected != true;

        Text = BuildInfo.AppName;
        Font = SystemFonts.MessageBoxFont ?? Font;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.None;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var appIcon = LoadAppIcon();
        if (appIcon is not null) Icon = appIcon;

        BuildLayout();
        BuildTray(appIcon);

        sync.Changed += QueueRefresh;
        Resize += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized) HideToTray();
        };
    }

    int S(int px) => (int)Math.Round(px * DeviceDpi / 96.0);

    // ---------------------------------------------------------------- layout

    void BuildLayout()
    {
        SuspendLayout();

        var textWidth = S(390);

        var root = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Padding = new Padding(S(16), S(12), S(16), S(12)),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        lblHeading.AutoSize = true;
        lblHeading.Text = "ON YOUR STEAM PROFILE";
        lblHeading.ForeColor = SystemColors.GrayText;
        lblHeading.Font = new Font(Font.FontFamily, 7.5f, FontStyle.Bold);
        lblHeading.Margin = new Padding(0, 0, 0, S(2));

        lblStatus.AutoSize = true;
        lblStatus.MaximumSize = new Size(textWidth, 0);
        lblStatus.Font = new Font(Font.FontFamily, 13f, FontStyle.Bold);
        lblStatus.UseMnemonic = false;
        lblStatus.Margin = new Padding(0, 0, 0, S(2));

        lblSub.AutoSize = true;
        lblSub.MaximumSize = new Size(textWidth, 0);
        lblSub.ForeColor = SystemColors.GrayText;
        lblSub.UseMnemonic = false;
        lblSub.Margin = new Padding(0, 0, 0, S(10));

        // Steam card
        ConfigureGroup(grpSteam, "Steam");
        var steamGrid = CardGrid();
        ConfigureCardLabel(lblSteam);
        ConfigureCardButton(btnSteam);
        btnSteam.Click += (_, _) => OnSteamButton();

        picQr.SizeMode = PictureBoxSizeMode.Zoom;
        picQr.Size = new Size(S(200), S(200));
        picQr.Anchor = AnchorStyles.None;
        picQr.Margin = new Padding(0, S(8), 0, S(4));
        picQr.BackColor = Color.White;
        picQr.Visible = false;

        lblSteamHint.AutoSize = true;
        lblSteamHint.MaximumSize = new Size(textWidth - S(24), 0);
        lblSteamHint.ForeColor = SystemColors.GrayText;
        lblSteamHint.UseMnemonic = false;
        lblSteamHint.Margin = new Padding(0, S(4), 0, 0);
        lblSteamHint.Visible = false;

        steamGrid.Controls.Add(lblSteam, 0, 0);
        steamGrid.Controls.Add(btnSteam, 1, 0);
        steamGrid.Controls.Add(picQr, 0, 1);
        steamGrid.SetColumnSpan(picQr, 2);
        steamGrid.Controls.Add(lblSteamHint, 0, 2);
        steamGrid.SetColumnSpan(lblSteamHint, 2);
        grpSteam.Controls.Add(steamGrid);

        // Spotify card
        ConfigureGroup(grpSpotify, "Spotify");
        var spotifyGrid = CardGrid();
        ConfigureCardLabel(lblSpotify);
        ConfigureCardButton(btnSpotify);
        btnSpotify.Click += (_, _) => OnSpotifyButton();

        lblSpotifyProblem.AutoSize = true;
        lblSpotifyProblem.MaximumSize = new Size(textWidth - S(24), 0);
        lblSpotifyProblem.ForeColor = Color.Firebrick;
        lblSpotifyProblem.UseMnemonic = false;
        lblSpotifyProblem.Margin = new Padding(0, S(6), 0, 0);
        lblSpotifyProblem.Visible = false;

        spotifyGrid.Controls.Add(lblSpotify, 0, 0);
        spotifyGrid.Controls.Add(btnSpotify, 1, 0);
        spotifyGrid.Controls.Add(lblSpotifyProblem, 0, 1);
        spotifyGrid.SetColumnSpan(lblSpotifyProblem, 2);
        grpSpotify.Controls.Add(spotifyGrid);

        // Bottom row
        var bottom = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(0, S(8), 0, 0),
        };

        chkPause.AutoSize = true;
        chkPause.Text = "Pause sharing";
        chkPause.Margin = new Padding(0, S(6), S(16), 0);
        chkPause.Checked = settings.Paused;
        chkPause.CheckedChanged += (_, _) => { if (!updatingPause) SetPaused(chkPause.Checked); };

        btnSettings.AutoSize = true;
        btnSettings.Text = "Settings…";
        btnSettings.MinimumSize = new Size(S(88), 0);
        btnSettings.Margin = new Padding(0, 0, S(12), 0);
        btnSettings.Click += (_, _) => OpenSettings();

        lnkAbout.AutoSize = true;
        lnkAbout.Text = "About";
        lnkAbout.Margin = new Padding(0, S(6), 0, 0);
        lnkAbout.LinkClicked += (_, _) => ShowAbout();

        bottom.Controls.Add(chkPause);
        bottom.Controls.Add(btnSettings);
        bottom.Controls.Add(lnkAbout);

        root.Controls.Add(lblHeading, 0, 0);
        root.Controls.Add(lblStatus, 0, 1);
        root.Controls.Add(lblSub, 0, 2);
        root.Controls.Add(grpSteam, 0, 3);
        root.Controls.Add(grpSpotify, 0, 4);
        root.Controls.Add(bottom, 0, 5);
        Controls.Add(root);

        ResumeLayout(false);
        PerformLayout();

        void ConfigureGroup(GroupBox group, string title)
        {
            group.Text = title;
            group.AutoSize = true;
            group.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            group.MinimumSize = new Size(textWidth, 0);
            group.Padding = new Padding(S(10), S(6), S(10), S(10));
            group.Margin = new Padding(0, 0, 0, S(8));
        }

        TableLayoutPanel CardGrid()
        {
            var grid = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                Dock = DockStyle.Top,
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            return grid;
        }

        void ConfigureCardLabel(Label label)
        {
            label.AutoSize = true;
            label.MaximumSize = new Size(textWidth - S(140), 0);
            label.Anchor = AnchorStyles.Left;
            label.UseMnemonic = false;
            label.Margin = new Padding(0, S(4), S(8), S(4));
        }

        void ConfigureCardButton(Button button)
        {
            button.AutoSize = true;
            button.MinimumSize = new Size(S(100), 0);
            button.Anchor = AnchorStyles.Right;
            button.Margin = new Padding(0);
        }
    }

    void BuildTray(Icon? appIcon)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => ShowFromTray());
        trayPause.CheckOnClick = false;
        trayPause.Click += (_, _) => SetPaused(!settings.Paused);
        menu.Items.Add(trayPause);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());

        tray.Icon = appIcon is null ? SystemIcons.Application : new Icon(appIcon, SystemInformation.SmallIconSize);
        tray.Text = BuildInfo.AppName;
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => ShowFromTray();
        tray.Visible = true;
    }

    static Icon? LoadAppIcon()
    {
        try
        {
            var exe = Environment.ProcessPath;
            return exe is null ? null : Icon.ExtractAssociatedIcon(exe);
        }
        catch
        {
            return null;
        }
    }

    // ---------------------------------------------------------------- lifecycle

    protected override void SetVisibleCore(bool value)
    {
        if (!allowVisible)
        {
            value = false;
            if (!IsHandleCreated) CreateHandle();
        }
        base.SetVisibleCore(value);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (started) return;
        started = true;
        RefreshUi();
        sync.Start();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Program.ShowMeMessage && Program.ShowMeMessage != 0)
        {
            ShowFromTray();
            return;
        }
        base.WndProc(ref m);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!exiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        sync.Changed -= QueueRefresh;
        sync.Dispose(); // clears the Steam status and signs this session off
        tray.Visible = false;
        tray.Dispose();
        base.OnFormClosed(e);
    }

    void ShowFromTray()
    {
        allowVisible = true;
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        NativeMethods.SetForegroundWindow(Handle);
    }

    void HideToTray()
    {
        Hide();
        if (!settings.TrayHintShown)
        {
            settings.TrayHintShown = true;
            settings.Save();
            tray.ShowBalloonTip(4000, BuildInfo.AppName, "Still running here in the tray. Right-click the icon to quit.", ToolTipIcon.None);
        }
    }

    void ExitApp()
    {
        exiting = true;
        Close();
    }

    // ---------------------------------------------------------------- actions

    void OnSteamButton()
    {
        var steam = sync.Steam;
        switch (steamAction)
        {
            case SteamAction.SignIn:
                steam.Start();
                break;
            case SteamAction.Cancel:
                steam.CancelSignIn();
                break;
            case SteamAction.SignOut:
                var answer = MessageBox.Show(this,
                    "Sign out of Steam in this app? Your status goes back to normal, and you'll need to scan a new code to use it again.",
                    BuildInfo.AppName, MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                if (answer == DialogResult.OK) steam.SignOut();
                break;
        }
        RefreshUi();
    }

    void OnSpotifyButton()
    {
        switch (spotifyAction)
        {
            case SpotifyAction.OpenSettings:
                OpenSettings();
                break;
            case SpotifyAction.Connect:
                _ = ConnectSpotifyAsync();
                break;
            case SpotifyAction.Cancel:
                sync.CancelSpotifyConnect();
                break;
            case SpotifyAction.Disconnect:
                sync.DisconnectSpotify();
                break;
        }
        RefreshUi();
    }

    async Task ConnectSpotifyAsync()
    {
        try
        {
            await sync.ConnectSpotifyAsync(OpenUrl);
        }
        catch (Exception ex)
        {
            Log.Write("Spotify connect error: " + ex);
        }
    }

    void SetPaused(bool paused)
    {
        sync.SetPaused(paused);
        RefreshUi();
    }

    void OpenSettings()
    {
        using var dialog = new SettingsForm(settings);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            sync.SettingsChanged();
        }
        RefreshUi();
    }

    void ShowAbout()
    {
        var answer = MessageBox.Show(this,
            $"{BuildInfo.AppName} {BuildInfo.Version}\n\n" +
            "Shows what you're playing on Spotify as your Steam status.\n\n" +
            "Inspired by HuxleyMc/Steam-Spotify. Uses SteamKit2 and QRCoder.\n\n" +
            "Open the project page?",
            "About " + BuildInfo.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Information);
        if (answer == DialogResult.Yes) OpenUrl(BuildInfo.RepoUrl);
    }

    static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Write("Couldn't open browser: " + ex.Message);
        }
    }

    // ---------------------------------------------------------------- rendering

    void QueueRefresh()
    {
        if (Interlocked.Exchange(ref refreshQueued, 1) == 1) return;
        try
        {
            if (IsHandleCreated && !IsDisposed) BeginInvoke(new Action(RefreshUi));
            else refreshQueued = 0;
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            refreshQueued = 0;
        }
    }

    void RefreshUi()
    {
        Interlocked.Exchange(ref refreshQueued, 0);
        if (IsDisposed) return;

        SuspendLayout();
        try
        {
            RenderHeader();
            RenderSteam();
            RenderSpotify();

            updatingPause = true;
            chkPause.Checked = settings.Paused;
            updatingPause = false;
            trayPause.Checked = settings.Paused;
            tray.Text = Truncate($"{BuildInfo.AppName}: {lblStatus.Text}", 63);
        }
        finally
        {
            ResumeLayout(true);
        }
    }

    void RenderHeader()
    {
        var steam = sync.Steam;
        string status, sub;

        if (sync.Paused)
        {
            status = "Paused";
            sub = "Sharing is paused, so your Steam status is left alone.";
        }
        else if (steam.State != SteamState.Online)
        {
            status = "Not live";
            sub = steam.HasSavedLogin ? "Connecting to Steam…" : "Sign in to Steam below to start.";
        }
        else if (sync.Spotify?.IsConnected != true)
        {
            status = "Not live";
            sub = "Connect Spotify below to start.";
        }
        else if (steam.PlayingBlocked)
        {
            status = "In a game";
            sub = "You're playing something on Steam. Your song comes back when you stop.";
        }
        else if (sync.StatusText is { } text)
        {
            status = text;
            sub = sync.CurrentTrack?.IsPlaying == true ? "Showing now." : "Showing while nothing's playing.";
        }
        else
        {
            status = "Nothing playing";
            sub = "Your normal Steam status is showing.";
        }

        lblStatus.Text = status;
        lblSub.Text = sub;
    }

    void RenderSteam()
    {
        var steam = sync.Steam;
        var showQr = false;
        string? hint = null;

        switch (steam.State)
        {
            case SteamState.SignedOut:
                lblSteam.Text = steam.Message ?? "Not signed in.";
                SetSteamButton("Sign in", SteamAction.SignIn);
                break;

            case SteamState.Connecting:
                lblSteam.Text = steam.Message ?? "Connecting to Steam…";
                SetSteamButton("Cancel", SteamAction.Cancel);
                break;

            case SteamState.WaitingForQr:
                lblSteam.Text = "Scan this with the Steam app on your phone.";
                hint = "In the Steam app, tap the shield (Steam Guard), then \"Scan a QR code\". " +
                       "The code refreshes itself, and this app never sees your password.";
                showQr = steam.QrChallengeUrl is not null;
                SetSteamButton("Cancel", SteamAction.Cancel);
                break;

            case SteamState.SigningIn:
                lblSteam.Text = "Signing in…";
                SetSteamButton("Cancel", SteamAction.Cancel);
                break;

            case SteamState.Online:
                var name = steam.PersonaName ?? steam.AccountName ?? "your account";
                lblSteam.Text = $"Signed in as {name}.";
                SetSteamButton("Sign out", SteamAction.SignOut);
                break;

            case SteamState.Reconnecting:
                lblSteam.Text = steam.Message ?? "Reconnecting…";
                SetSteamButton("Sign out", SteamAction.SignOut);
                break;

            case SteamState.Error:
                lblSteam.Text = steam.Message ?? "Something went wrong.";
                SetSteamButton("Try again", SteamAction.SignIn);
                break;
        }

        if (showQr && steam.QrChallengeUrl != shownQrUrl)
        {
            shownQrUrl = steam.QrChallengeUrl;
            var old = picQr.Image;
            picQr.Image = QrImage.Create(shownQrUrl!, Math.Max(4, S(6)));
            old?.Dispose();
        }
        else if (!showQr && picQr.Image is not null)
        {
            var old = picQr.Image;
            picQr.Image = null;
            old.Dispose();
            shownQrUrl = null;
        }

        picQr.Visible = showQr;
        lblSteamHint.Text = hint ?? "";
        lblSteamHint.Visible = hint is not null;
    }

    void SetSteamButton(string text, SteamAction action)
    {
        btnSteam.Text = text;
        steamAction = action;
    }

    void RenderSpotify()
    {
        var spotify = sync.Spotify;

        if (spotify is null)
        {
            lblSpotify.Text = "No Spotify app is set up in this copy.";
            SetSpotifyButton("Settings…", SpotifyAction.OpenSettings);
        }
        else if (sync.SpotifyConnecting)
        {
            lblSpotify.Text = "Approve access in your browser…";
            SetSpotifyButton("Cancel", SpotifyAction.Cancel);
        }
        else if (!spotify.IsConnected)
        {
            lblSpotify.Text = "Not connected.";
            SetSpotifyButton("Connect", SpotifyAction.Connect);
        }
        else
        {
            lblSpotify.Text = spotify.DisplayName is { Length: > 0 } n ? $"Connected as {n}." : "Connected.";
            SetSpotifyButton("Disconnect", SpotifyAction.Disconnect);
        }

        var problem = sync.SpotifyProblem;
        lblSpotifyProblem.Text = problem ?? "";
        lblSpotifyProblem.Visible = problem is not null;
    }

    void SetSpotifyButton(string text, SpotifyAction action)
    {
        btnSpotify.Text = text;
        spotifyAction = action;
    }

    static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            picQr.Image?.Dispose();
            tray.Dispose();
        }
        base.Dispose(disposing);
    }
}
