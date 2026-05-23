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
    private bool _isConnected = false;
    private float _currentSteering = 0f;
    private bool _exitRequested = false;

    // Pulse animation: three rings, each with an independent phase (0.0–1.0)
    private float _p1 = 0f, _p2 = 0f, _p3 = 0f;
    private System.Windows.Forms.Timer _pulseTimer = null!;

    // -------------------------------------------------------------------------
    // Controls
    // -------------------------------------------------------------------------

    private BufferedPanel _visualPanel = null!;
    private Label _titleLabel = null!;
    private Label _steeringValueLabel = null!;
    private Label _statusLabel = null!;
    private Label _ipLabel = null!;
    private Label _errorLabel = null!;
    private Button _minimizeButton = null!;
    private NotifyIcon _trayIcon = null!;

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

        // Visual panel (custom-painted wheel / pulse rings)
        _visualPanel = new BufferedPanel
        {
            Bounds    = new Rectangle(80, 56, 200, 200),
            BackColor = BgColor,
        };
        _visualPanel.Paint += PaintVisual;

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

        // Error
        _errorLabel = new Label
        {
            Text      = string.Empty,
            ForeColor = AccentRed,
            BackColor = Color.Transparent,
            Font      = new Font("Segoe UI", 8f),
            TextAlign = ContentAlignment.MiddleCenter,
            Bounds    = new Rectangle(8, 350, 344, 36),
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

        // Pulse animation timer
        _pulseTimer = new System.Windows.Forms.Timer { Interval = 16 }; // ~60fps
        _pulseTimer.Tick += (_, _) =>
        {
            _p1 = (_p1 + 0.008f) % 1f;
            _p2 = (_p2 + 0.008f) % 1f;
            _p3 = (_p3 + 0.008f) % 1f;
            _visualPanel.Invalidate();
        };

        Controls.AddRange(new Control[] { _titleLabel, _visualPanel, _steeringValueLabel,
                                          _statusLabel, _ipLabel, _errorLabel, _minimizeButton });
    }

    private void BuildTrayIcon()
    {
        var menu = new ContextMenuStrip { BackColor = SurfaceColor, ForeColor = TextPrimary };
        menu.Items.Add("Show REVV").Click   += (_, _) => RestoreFromTray();
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit").Click        += (_, _) => ExitApp();

        _trayIcon = new NotifyIcon
        {
            Text             = "REVV — PC Receiver",
            Icon             = SystemIcons.Application,
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
            _isConnected = true;
            _pulseTimer.Stop();
            _statusLabel.Text      = $"CONNECTED  ·  {ip}";
            _statusLabel.ForeColor = ConnectedGreen;
            _steeringValueLabel.Visible = true;
            _errorLabel.Text       = string.Empty;
            _visualPanel.Invalidate();
        });

        _session.PhoneDisconnected += (_, _) => InvokeOnUI(() =>
        {
            _isConnected         = false;
            _currentSteering     = 0f;
            _statusLabel.Text    = "WAITING FOR PHONE";
            _statusLabel.ForeColor = TextMuted;
            _steeringValueLabel.Visible = false;
            _p1 = 0f; _p2 = 0.33f; _p3 = 0.66f;
            _pulseTimer.Start();
            _visualPanel.Invalidate();
        });

        _session.SteeringUpdated += (_, value) => InvokeOnUI(() =>
        {
            _currentSteering         = value;
            _steeringValueLabel.Text = $"{value:+0.00;-0.00;0.00}";
            _visualPanel.Invalidate();
        });

        _session.ErrorOccurred += (_, ex) => InvokeOnUI(() =>
        {
            _errorLabel.Text = ex.Message;
        });
    }

    // -------------------------------------------------------------------------
    // Painting
    // -------------------------------------------------------------------------

    private void PaintVisual(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        var cx = _visualPanel.Width / 2f;
        var cy = _visualPanel.Height / 2f;

        if (_isConnected)
            DrawWheel(g, cx, cy);
        else
            DrawPulse(g, cx, cy);
    }

    private void DrawWheel(Graphics g, float cx, float cy)
    {
        // Rotate around the center by steering angle (±135°)
        g.TranslateTransform(cx, cy);
        g.RotateTransform(_currentSteering * 135f);
        g.TranslateTransform(-cx, -cy);

        float r = 70f;
        using var rimPen    = new Pen(AccentRed, 10f);
        using var spokePen  = new Pen(AccentRed, 5f);

        // Outer rim
        g.DrawEllipse(rimPen, cx - r, cy - r, r * 2, r * 2);

        // Vertical spoke
        g.DrawLine(spokePen, cx, cy - r + 5, cx, cy + r - 5);

        // Horizontal spoke
        g.DrawLine(spokePen, cx - r + 5, cy, cx + r - 5, cy);

        g.ResetTransform();

        // Hub (always upright — drawn after reset)
        float hr = 15f;
        using var hubPen  = new Pen(ConnectedGreen, 5f);
        using var hubBrush = new SolidBrush(BgColor);
        g.FillEllipse(hubBrush, cx - hr, cy - hr, hr * 2, hr * 2);
        g.DrawEllipse(hubPen,   cx - hr, cy - hr, hr * 2, hr * 2);
    }

    private void DrawPulse(Graphics g, float cx, float cy)
    {
        // Center dot
        using var dotBrush = new SolidBrush(AccentRed);
        g.FillEllipse(dotBrush, cx - 6, cy - 6, 12, 12);

        // Three rings, each offset 1/3 of the cycle
        DrawPulseRing(g, cx, cy, _p1, 60f, 2f,   0.75f);
        DrawPulseRing(g, cx, cy, _p2, 52f, 1.5f, 0.5f);
        DrawPulseRing(g, cx, cy, _p3, 44f, 1f,   0.3f);
    }

    private static void DrawPulseRing(Graphics g, float cx, float cy,
                                      float phase, float maxR, float strokeW, float maxAlpha)
    {
        // phase 0→1: ring expands from 0→maxR and fades from maxAlpha→0
        float r       = phase * maxR;
        if (!float.IsFinite(r) || r <= 0) return;  // GDI+ rejects zero/invalid-dimension ellipses
        float alpha   = (1f - phase) * maxAlpha;
        int   a       = (int)(alpha * 255);
        if (a <= 0) return;

        using var pen = new Pen(Color.FromArgb(a, AccentRed), strokeW);
        g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);
    }

    // -------------------------------------------------------------------------
    // Tray / window management
    // -------------------------------------------------------------------------

    private void MinimizeToTray()
    {
        _trayIcon.Visible = true;
        Hide();
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

        _pulseTimer.Stop();
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

        // Stagger ring phases so they don't all start at 0
        _p1 = 0f; _p2 = 0.33f; _p3 = 0.66f;
        _pulseTimer.Start();

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

    private sealed class BufferedPanel : Panel
    {
        public BufferedPanel() => DoubleBuffered = true;
    }
}
