using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Revv.Windows;

public class MainForm : Form
{
    // -------------------------------------------------------------------------
    // Colors
    // -------------------------------------------------------------------------

    private static readonly Color BgColor          = Color.FromArgb(10, 10, 10);
    private static readonly Color SurfaceColor      = Color.FromArgb(20, 20, 20);
    private static readonly Color AccentRed         = Color.FromArgb(232, 0, 29);
    private static readonly Color TextPrimary       = Color.FromArgb(240, 240, 240);
    private static readonly Color TextMuted         = Color.FromArgb(85, 85, 85);
    private static readonly Color ConnectedGreen    = Color.FromArgb(0, 232, 122);

    // -------------------------------------------------------------------------
    // State
    // -------------------------------------------------------------------------

    private readonly RevvSession _session = new();
    private bool _exitRequested = false;

    // -------------------------------------------------------------------------
    // Controls
    // -------------------------------------------------------------------------

    private AttitudeSKControl _attitudeView = null!;
    private PulseSKControl _pulseView = null!;
    private Label _titleLabel = null!;
    private Label _steeringValueLabel = null!;
    private Label _statusLabel = null!;
    private Label _ipLabel = null!;
    private Label _telemetryLabel = null!;
    private Label _errorLabel = null!;
    private Button _minimizeButton = null!;
    private NotifyIcon _trayIcon = null!;
    private System.Windows.Forms.Timer _telemetryTimer = null!;

    // -------------------------------------------------------------------------
    // Constructor
    // -------------------------------------------------------------------------

    public MainForm()
    {
        BuildForm();
        BuildControls();
        BuildTrayIcon();
        WireSession();
    }

    // -------------------------------------------------------------------------
    // Form setup
    // -------------------------------------------------------------------------

    private void BuildForm()
    {
        Text              = "REVV";
        ClientSize        = new Size(360, 480);
        FormBorderStyle   = FormBorderStyle.FixedSingle;
        MaximizeBox       = false;
        BackColor         = BgColor;
        Font              = new Font("Segoe UI", 9f);
        StartPosition     = FormStartPosition.CenterScreen;

        var iconPath = Path.Combine(AppContext.BaseDirectory, "revv.ico");
        if (File.Exists(iconPath))
            Icon = new Icon(iconPath);
    }

    private void BuildControls()
    {
        // Title
        _titleLabel = new Label
        {
            Text      = "R E V V",
            ForeColor = AccentRed,
            BackColor = Color.Transparent,
            Font      = new Font("Segoe UI", 14f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            Bounds    = new Rectangle(0, 12, 360, 32),
        };

        // Pulse (disconnected / waiting state)
        _pulseView = new PulseSKControl
        {
            Bounds    = new Rectangle(80, 56, 200, 200),
            BackColor = BgColor,
        };

        // Attitude indicator (SkiaSharp horizon, shown only when connected)
        _attitudeView = new AttitudeSKControl
        {
            Bounds    = new Rectangle(80, 56, 200, 200),
            BackColor = BgColor,
            Visible   = false,
        };

        // Steering value
        _steeringValueLabel = new Label
        {
            Text      = "0.00",
            ForeColor = AccentRed,
            BackColor = Color.Transparent,
            Font      = new Font("Segoe UI", 22f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            Bounds    = new Rectangle(0, 262, 360, 36),
            Visible   = false,
        };

        // Status
        _statusLabel = new Label
        {
            Text      = "WAITING FOR PHONE",
            ForeColor = TextMuted,
            BackColor = Color.Transparent,
            Font      = new Font("Segoe UI", 9f),
            TextAlign = ContentAlignment.MiddleCenter,
            Bounds    = new Rectangle(0, 308, 360, 20),
        };

        // Local IP
        _ipLabel = new Label
        {
            Text      = GetLocalIpAddresses(),
            ForeColor = Color.FromArgb(42, 42, 42),
            BackColor = Color.Transparent,
            Font      = new Font("Segoe UI", 8f),
            TextAlign = ContentAlignment.MiddleCenter,
            Bounds    = new Rectangle(0, 330, 360, 18),
        };

        // Telemetry status (Forza speed data)
        _telemetryLabel = new Label
        {
            Text      = "FORZA TELEMETRY  ·  WAITING",
            ForeColor = TextMuted,
            BackColor = Color.Transparent,
            Font      = new Font("Segoe UI", 8f),
            TextAlign = ContentAlignment.MiddleCenter,
            Bounds    = new Rectangle(0, 350, 360, 18),
        };

        // Error
        _errorLabel = new Label
        {
            Text      = string.Empty,
            ForeColor = AccentRed,
            BackColor = Color.Transparent,
            Font      = new Font("Segoe UI", 8f),
            TextAlign = ContentAlignment.MiddleCenter,
            Bounds    = new Rectangle(8, 370, 344, 36),
        };

        // Minimize button
        _minimizeButton = new Button
        {
            Text         = "MINIMIZE TO TRAY",
            ForeColor    = Color.FromArgb(58, 58, 58),
            BackColor    = SurfaceColor,
            FlatStyle    = FlatStyle.Flat,
            Font         = new Font("Segoe UI", 8f),
            Bounds       = new Rectangle(24, 424, 312, 36),
            Cursor       = Cursors.Hand,
        };
        _minimizeButton.FlatAppearance.BorderColor = Color.FromArgb(31, 31, 31);
        _minimizeButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(28, 28, 28);
        _minimizeButton.Click += (_, _) => MinimizeToTray();

        // Telemetry refresh timer (1 Hz is plenty for a status label)
        _telemetryTimer = new System.Windows.Forms.Timer { Interval = 1000 };

        Controls.AddRange(new Control[] { _titleLabel, _pulseView, _attitudeView, _steeringValueLabel,
                                          _statusLabel, _ipLabel, _telemetryLabel, _errorLabel, _minimizeButton });
    }

    private void BuildTrayIcon()
    {
        var menu = new ContextMenuStrip { BackColor = SurfaceColor, ForeColor = TextPrimary };
        menu.Items.Add("Show REVV").Click   += (_, _) => RestoreFromTray();
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit").Click        += (_, _) => ExitApp();

        var iconPath = Path.Combine(AppContext.BaseDirectory, "revv.ico");
        _trayIcon = new NotifyIcon
        {
            Text             = "REVV — PC Receiver",
            Icon             = File.Exists(iconPath) ? new Icon(iconPath) : SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible          = false,
        };
        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();
    }

    // -------------------------------------------------------------------------
    // Session wiring
    // -------------------------------------------------------------------------

    private void WireSession()
    {
        _session.PhoneConnected += (_, ip) => InvokeOnUI(() =>
        {
            _statusLabel.Text           = $"CONNECTED  ·  {ip}";
            _statusLabel.ForeColor      = ConnectedGreen;
            _steeringValueLabel.Visible = true;
            _errorLabel.Text            = string.Empty;
            _pulseView.StopPulse();
            _pulseView.Visible    = false;
            _attitudeView.Update(0f);
            _attitudeView.Visible = true;
        });

        _session.PhoneDisconnected += (_, _) => InvokeOnUI(() =>
        {
            _statusLabel.Text           = "WAITING FOR PHONE";
            _statusLabel.ForeColor      = TextMuted;
            _steeringValueLabel.Visible = false;
            _attitudeView.Visible = false;
            _pulseView.Visible    = true;
            _pulseView.StartPulse();
        });

        _session.SteeringUpdated += (_, value) => InvokeOnUI(() =>
        {
            _steeringValueLabel.Text = $"{value:+0.00;-0.00;0.00}";
            _attitudeView.Update(value * 45f);
        });

        _session.ErrorOccurred += (_, ex) => InvokeOnUI(() =>
        {
            _errorLabel.Text = ex.Message;
        });
    }

    // -------------------------------------------------------------------------
    // Tray / window management
    // -------------------------------------------------------------------------

    private void MinimizeToTray()
    {
        _trayIcon.Visible = true;
        Hide();
        _trayIcon.ShowBalloonTip(3000, "REVV", "Still running. Right-click the tray icon to exit.", ToolTipIcon.None);
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState       = FormWindowState.Normal;
        _trayIcon.Visible = false;
        Activate();
    }

    private void ExitApp()
    {
        _exitRequested    = true;
        _trayIcon.Visible = false;
        Application.Exit();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_exitRequested && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            MinimizeToTray();
            return;
        }

        _telemetryTimer.Stop();
        _ = _session.StopAsync();
        _trayIcon.Dispose();
        base.OnFormClosing(e);
    }

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        _pulseView.StartPulse();
        _telemetryTimer.Start();

        try
        {
            await _session.StartAsync();
        }
        catch (Exception ex)
        {
            _errorLabel.Text = ex.Message;
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private void InvokeOnUI(Action action)
    {
        if (IsDisposed) return;
        if (InvokeRequired)
            Invoke(action);
        else
            action();
    }

    private static string GetLocalIpAddresses()
    {
        var ips = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                     && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(a => a.Address.ToString());
        return string.Join("  ·  ", ips);
    }
}
