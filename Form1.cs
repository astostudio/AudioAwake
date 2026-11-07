using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SoundbarKeepAlive;

public partial class Form1 : Form
{
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 9001;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

    private static readonly string PrimaryFontFamily = ResolveBestFontFamily();

    private static string ResolveBestFontFamily()
    {
        string[] candidates =
        {
            "Segoe UI Variable Display",
            "Bahnschrift",
            "Century Gothic",
            "Segoe UI"
        };

        using var installed = new InstalledFontCollection();
        foreach (string candidate in candidates)
        {
            foreach (var fam in installed.Families)
            {
                if (fam.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return fam.Name;
                }
            }
        }
        return "Segoe UI";
    }

    private static Font UiFont(float size, FontStyle style = FontStyle.Regular) =>
        new(PrimaryFontFamily, size, style);

    private readonly AppSettings _settings;
    private readonly AudioKeepAliveEngine _engine;
    private readonly NotifyIcon _trayIcon;
    private readonly ContextMenuStrip _trayMenu;
    private readonly System.Windows.Forms.Timer _uiTimer;
    private readonly DateTime _startTime = DateTime.UtcNow;
    private EventWaitHandle? _showWindowEvent;

    private Icon? _iconActive;
    private Icon? _iconStandby;
    private Icon? _iconOffRed;

    private ToolStripMenuItem _menuOpenItem = null!;
    private ToolStripMenuItem _menuToggleItem = null!;
    private ToolStripMenuItem _menuStartupItem = null!;
    private ToolStripMenuItem _menuExitItem = null!;

    private bool _isRecordingHotkey = false;
    private bool _isTestingAudio = false;
    private bool _allowExit = false;
    private bool _shownTrayBalloon = false;
    private List<AudioOutputDeviceItem> _currentDevices = new();
    private readonly int[] _idleMinuteOptions = { 15, 30, 60, 120 };

    private Rectangle _rectDevicePill;
    private Rectangle _rectBrandPill;
    private Rectangle _rectTestSliderBar;

    private Rectangle _rectHotkeyCard;
    private Rectangle _rectIdleTimeCard;

    private Rectangle _rectTilePower;
    private Rectangle _rectTileEco;
    private Rectangle _rectTileStartup;
    private Rectangle _rectTileLang;

    private Rectangle _rectTopMinBtn;
    private Rectangle _rectTopCloseBtn;
    private Rectangle _rectSidebarSwitch;
    private Rectangle _rectDialMinus;
    private Rectangle _rectDialPlus;
    private Rectangle _rectModeBtn1;
    private Rectangle _rectModeBtn2;
    private Rectangle _rectModeBtn3;
    private Rectangle _rectHideToTrayCard;

    private string _hoveredZone = "";

    private bool IsEn => string.Equals(_settings.Language, "en", StringComparison.OrdinalIgnoreCase);

    public Form1(bool startMinimized)
    {
        _settings = AppSettings.Load();
        _engine = new AudioKeepAliveEngine(_settings);

        SetStyle(ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);

        FormBorderStyle = FormBorderStyle.None;
        Size = new Size(860, 520);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(46, 44, 43);
        KeyPreview = true;

        CreateDynamicIcons();
        _currentDevices = _engine.GetOutputDevices(IsEn);
        AutoSelectPreferredDeviceIfEmpty();

        _trayMenu = BuildTrayMenu();
        _trayIcon = new NotifyIcon
        {
            Text = "AudioAwake (Clic: On/Off | Der: Menú)",
            Icon = _iconActive,
            ContextMenuStrip = _trayMenu,
            Visible = true
        };
        _trayIcon.MouseClick += OnTrayIconMouseClick;

        SetupSimplifiedGeometry();

        MouseDown += OnDashboardMouseDown;
        MouseUp += OnDashboardMouseUp;
        MouseMove += OnDashboardMouseMove;
        KeyDown += OnFormKeyDownForHotkey;

        _engine.StateChanged += () =>
        {
            if (IsHandleCreated) BeginInvoke(new Action(SyncTrayAndInvalidate));
        };
        _engine.StartOrRestart();

        RegisterCustomGlobalHotkey();
        StartSingleInstanceListener();

        _uiTimer = new System.Windows.Forms.Timer { Interval = 300 };
        _uiTimer.Tick += (_, _) =>
        {
            _engine.TickMonitor();
            SyncTrayAndInvalidate();
        };
        _uiTimer.Start();

        if (startMinimized || _settings.StartMinimized)
        {
            WindowState = FormWindowState.Minimized;
            ShowInTaskbar = false;
            Opacity = 0;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyRoundedWindowRegion();
        RegisterCustomGlobalHotkey();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (IsHandleCreated) ApplyRoundedWindowRegion();
    }

    private void ApplyRoundedWindowRegion()
    {
        try
        {
            IntPtr rgn = CreateRoundRectRgn(0, 0, Width + 1, Height + 1, 32, 32);
            SetWindowRgn(Handle, rgn, true);
        }
        catch
        {
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (Opacity == 0)
        {
            Hide();
            Opacity = 1;
        }
        SyncTrayAndInvalidate();
    }

    private void SetupSimplifiedGeometry()
    {
        _rectDevicePill = new Rectangle(196, 138, 172, 46);
        _rectBrandPill = new Rectangle(378, 138, 172, 46);
        _rectTestSliderBar = new Rectangle(196, 196, 354, 32);

        _rectHotkeyCard = new Rectangle(24, 258, 265, 82);
        _rectIdleTimeCard = new Rectangle(303, 258, 265, 82);

        int tileW = 127;
        int gap = 12;
        _rectTilePower = new Rectangle(24, 354, tileW, 142);
        _rectTileEco = new Rectangle(24 + (tileW + gap) * 1, 354, tileW, 142);
        _rectTileStartup = new Rectangle(24 + (tileW + gap) * 2, 354, tileW, 142);
        _rectTileLang = new Rectangle(24 + (tileW + gap) * 3, 354, tileW, 142);

        _rectTopMinBtn = new Rectangle(788, 16, 26, 26);
        _rectTopCloseBtn = new Rectangle(820, 16, 26, 26);

        _rectSidebarSwitch = new Rectangle(796, 76, 42, 22);

        _rectDialMinus = new Rectangle(694, 270, 30, 30);
        _rectDialPlus = new Rectangle(730, 270, 30, 30);

        _rectModeBtn1 = new Rectangle(614, 318, 70, 60);
        _rectModeBtn2 = new Rectangle(692, 318, 70, 60);
        _rectModeBtn3 = new Rectangle(770, 318, 70, 60);

        _rectHideToTrayCard = new Rectangle(614, 396, 226, 100);
    }

    // ==================== PINTADO SIMPLIFICADO ESTILO VISION-OS ====================

    protected override void OnPaint(PaintEventArgs e)
    {
        RenderDashboardToGraphics(e.Graphics);
    }

    private void RenderDashboardToGraphics(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        DrawSpatialGlassBackground(g);
        DrawHeroSpeakerCard(g);
        DrawMiddleActionCards(g);
        DrawBottomSmartTiles(g);
        DrawRightSidebar(g);
    }

    private void DrawSpatialGlassBackground(Graphics g)
    {
        Rectangle rect = new(0, 0, 860, 520);

        if (AppSettings.IsGamerEdition)
        {
            // Tema Esports Obsidian + Neon Violeta / Cian
            using (var baseGrad = new LinearGradientBrush(
                rect,
                Color.FromArgb(18, 20, 32),
                Color.FromArgb(10, 12, 20),
                LinearGradientMode.ForwardDiagonal))
            {
                g.FillRectangle(baseGrad, rect);
            }

            DrawAmbientGlow(g, -60, -50, 500, 360, Color.FromArgb(95, 139, 92, 246)); // Neon Violeta
            DrawAmbientGlow(g, 180, 250, 460, 320, Color.FromArgb(75, 6, 182, 212));  // Electric Cyan

            if (!_settings.IsEnabled)
            {
                DrawAmbientGlow(g, 480, 80, 380, 360, Color.FromArgb(85, 239, 68, 68));
            }
            else
            {
                DrawAmbientGlow(g, 520, 80, 380, 360, Color.FromArgb(85, 0, 240, 255));
            }

            Rectangle sidebarRectG = new(592, 0, 860 - 592, 520);
            using (var sideBrush = new SolidBrush(Color.FromArgb(85, 8, 10, 18)))
            {
                g.FillRectangle(sideBrush, sidebarRectG);
            }
            using (var divPen = new Pen(Color.FromArgb(80, 0, 240, 255), 1.2f))
            {
                g.DrawLine(divPen, 592, 0, 592, 520);
            }

            using var outerPathG = CreateRoundedPath(new Rectangle(1, 1, 860 - 3, 520 - 3), 30);
            using var outerPenG = new Pen(Color.FromArgb(130, 139, 92, 246), 1.8f);
            g.DrawPath(outerPenG, outerPathG);
            return;
        }

        using (var baseGrad = new LinearGradientBrush(
            rect,
            Color.FromArgb(106, 96, 90),
            Color.FromArgb(40, 42, 47),
            LinearGradientMode.ForwardDiagonal))
        {
            g.FillRectangle(baseGrad, rect);
        }

        DrawAmbientGlow(g, -50, -40, 480, 340, Color.FromArgb(85, 172, 156, 142));
        DrawAmbientGlow(g, 180, 260, 440, 300, Color.FromArgb(70, 180, 162, 146));

        if (!_settings.IsEnabled)
        {
            DrawAmbientGlow(g, 480, 80, 380, 360, Color.FromArgb(70, 239, 68, 68));
        }
        else
        {
            DrawAmbientGlow(g, 520, 90, 360, 340, Color.FromArgb(60, 72, 168, 255));
        }

        Rectangle sidebarRect = new(592, 0, 860 - 592, 520);
        using (var sideBrush = new SolidBrush(Color.FromArgb(52, 18, 18, 22)))
        {
            g.FillRectangle(sideBrush, sidebarRect);
        }
        using (var divPen = new Pen(Color.FromArgb(45, 255, 255, 255), 1f))
        {
            g.DrawLine(divPen, 592, 0, 592, 520);
        }

        using var outerPath = CreateRoundedPath(new Rectangle(1, 1, 860 - 3, 520 - 3), 30);
        using var outerPen = new Pen(Color.FromArgb(85, 255, 255, 255), 1.5f);
        g.DrawPath(outerPen, outerPath);
    }

    private static void DrawAmbientGlow(Graphics g, int x, int y, int w, int h, Color centerColor)
    {
        using var path = new GraphicsPath();
        path.AddEllipse(x, y, w, h);
        using var pgb = new PathGradientBrush(path)
        {
            CenterColor = centerColor,
            SurroundColors = new[] { Color.Transparent }
        };
        g.FillPath(pgb, path);
    }

    private void DrawHeroSpeakerCard(Graphics g)
    {
        Rectangle card = new(24, 24, 544, 220);
        DrawFrostedCard(g, card, 24, Color.FromArgb(76, 215, 210, 205), Color.FromArgb(70, 255, 255, 255));

        DrawSoundbarIllustration(g, new Rectangle(38, 38, 142, 184));

        using var fontSub = UiFont(8.5F, FontStyle.Regular);
        using var fontTitle = UiFont(12.5F, FontStyle.Bold);
        using var brushMuted = new SolidBrush(Color.FromArgb(200, 245, 245, 245));
        using var brushWhite = new SolidBrush(Color.White);

        bool hasGame = !string.IsNullOrEmpty(_engine.DetectedGameName);
        string statusHeader = !_settings.IsEnabled
            ? (IsEn ? "● PROTECTION PAUSED (OFF)" : "● PROTECCIÓN EN PAUSA")
            : (hasGame
                ? (IsEn ? $"★ GAME ACTIVE: {_engine.DetectedGameName} (0.0ms)" : $"★ JUEGO ACTIVO: {_engine.DetectedGameName} (0.0ms)")
                : (_engine.IsSleepingDueToIdle
                    ? (IsEn ? "☾ ECO SLEEP MODE" : "☾ MODO AHORRO ACTIVO")
                    : (IsEn ? "● ZERO-DELAY GAMING & AUX GUARD" : "● GUARDIA GAMING & AUX (0.0ms)")));

        Color statusColor = !_settings.IsEnabled
            ? Color.FromArgb(255, 120, 120)
            : Color.FromArgb(120, 235, 185);
        using var brushStatus = new SolidBrush(statusColor);
        g.DrawString(statusHeader, fontSub, brushStatus, 196, 36);

        int profIdx = Math.Clamp(_settings.BrandProfileIndex, 0, AudioKeepAliveEngine.Profiles.Count - 1);
        string profName = IsEn
            ? AudioKeepAliveEngine.Profiles[profIdx].NameEn
            : AudioKeepAliveEngine.Profiles[profIdx].NameEs;
        g.DrawString(ShortenText(profName, 34), fontTitle, brushWhite, 194, 54);

        Rectangle statPill = new(196, 86, 354, 40);
        DrawFrostedCard(g, statPill, 12, Color.FromArgb(42, 255, 255, 255), Color.FromArgb(35, 255, 255, 255));

        TimeSpan uptime = DateTime.UtcNow - _startTime;
        string uptimeStr = uptime.TotalHours >= 1
            ? $"{(int)uptime.TotalHours}h {uptime.Minutes}m"
            : $"{uptime.Minutes}m {uptime.Seconds}s";

        using var fontMetric = UiFont(9.2F, FontStyle.Bold);
        using var fontMetricSub = UiFont(8.1F, FontStyle.Regular);

        string leftStat = IsEn ? $"Awake: {uptimeStr}" : $"Activo: {uptimeStr}";
        string rightStat = IsEn
            ? "0.0ms Reflex | Anti-Gate (19.2kHz)"
            : "0.0ms Reflejo | Anti-Gate (19.2kHz)";

        g.DrawString(leftStat, fontMetric, brushWhite, statPill.X + 14, statPill.Y + 11);
        using (var sepPen = new Pen(Color.FromArgb(65, 255, 255, 255), 1f))
        {
            g.DrawLine(sepPen, statPill.X + 124, statPill.Y + 8, statPill.X + 124, statPill.Bottom - 8);
        }
        g.DrawString(rightStat, fontMetricSub, brushWhite, statPill.X + 136, statPill.Y + 11);

        DrawDropdownPill(
            g,
            _rectDevicePill,
            IsEn ? "1. Output Device" : "1. Salida de Audio",
            ShortenText(_engine.CurrentDeviceName, 19),
            _hoveredZone == "device");

        DrawDropdownPill(
            g,
            _rectBrandPill,
            IsEn ? "2. Gaming / Speaker Profile" : "2. Perfil Gamer / Altavoz",
            ShortenText(profName, 19),
            _hoveredZone == "brand");

        DrawFrostedCard(g, _rectTestSliderBar, 15, Color.FromArgb(52, 20, 20, 24), Color.FromArgb(45, 255, 255, 255));
        int fillW = _isTestingAudio ? _rectTestSliderBar.Width : 246;
        Rectangle fillRect = new(_rectTestSliderBar.X, _rectTestSliderBar.Y, fillW, _rectTestSliderBar.Height);
        using (var fillPath = CreateRoundedPath(fillRect, 15))
        using (var fillBrush = new SolidBrush(_isTestingAudio ? Color.FromArgb(84, 174, 255) : Color.White))
        {
            g.FillPath(fillBrush, fillPath);
        }

        using var fontTest = UiFont(8.5F, FontStyle.Bold);
        using var darkBrush = new SolidBrush(Color.FromArgb(32, 34, 40));
        string testText = _isTestingAudio
            ? (IsEn ? "► Stereo Radar L / R Testing..." : "► Probando Radar Estéreo L / R...")
            : (IsEn ? "► Hold for Stereo L/R Reflex Test" : "► Mantén: Prueba Radar Estéreo L/R");
        g.DrawString(testText, fontTest, darkBrush, _rectTestSliderBar.X + 14, _rectTestSliderBar.Y + 8);
    }

    private void DrawSoundbarIllustration(Graphics g, Rectangle bounds)
    {
        int cx = bounds.X + bounds.Width / 2;

        if (AppSettings.IsGamerEdition)
        {
            // Ilustración Gamer: Audífonos Pro RGB + Radar Táctico de Pasos (L / R)
            int cy = bounds.Y + bounds.Height / 2 + 6;

            // Radar táctico circular de fondo
            using (var radarPen = new Pen(Color.FromArgb(45, 0, 240, 255), 1.2f))
            {
                g.DrawEllipse(radarPen, cx - 56, cy - 56, 112, 112);
                g.DrawEllipse(radarPen, cx - 36, cy - 36, 72, 72);
                g.DrawLine(radarPen, cx - 60, cy, cx + 60, cy);
                g.DrawLine(radarPen, cx, cy - 60, cx, cy + 60);
            }

            // Diadema superior de los audífonos gaming
            using (var bandPen = new Pen(Color.FromArgb(52, 62, 82), 9f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawArc(bandPen, cx - 42, cy - 52, 84, 80, 180, 180);
            }
            using (var rgbBandPen = new Pen(_settings.IsEnabled ? Color.FromArgb(168, 85, 247) : Color.FromArgb(239, 68, 68), 3f))
            {
                g.DrawArc(rgbBandPen, cx - 42, cy - 52, 84, 80, 195, 150);
            }

            // Copas izquierda (L) y derecha (R) con anillo RGB Neón
            Rectangle cupL = new(cx - 52, cy - 16, 24, 42);
            Rectangle cupR = new(cx + 28, cy - 16, 24, 42);
            Color neonColor = !_settings.IsEnabled
                ? Color.FromArgb(255, 70, 70)
                : Color.FromArgb(0, 240, 255);

            using (var cupBrush = new SolidBrush(Color.FromArgb(26, 30, 42)))
            using (var neonPen = new Pen(neonColor, 2.5f))
            {
                using var pathL = CreateRoundedPath(cupL, 8);
                using var pathR = CreateRoundedPath(cupR, 8);
                g.FillPath(cupBrush, pathL);
                g.FillPath(cupBrush, pathR);
                g.DrawPath(neonPen, pathL);
                g.DrawPath(neonPen, pathR);
            }

            // Micrófono boom táctico
            using (var micPen = new Pen(Color.FromArgb(180, 200, 225), 2.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawLine(micPen, cx - 40, cy + 22, cx - 16, cy + 38);
            }
            using (var micTip = new SolidBrush(neonColor))
            {
                g.FillEllipse(micTip, cx - 18, cy + 34, 8, 8);
            }

            using var fTag = UiFont(8.5F, FontStyle.Bold);
            using var bNeon = new SolidBrush(neonColor);
            string tag = _settings.IsEnabled ? "0.0ms FPS" : "OFF";
            var tSz = g.MeasureString(tag, fTag);
            g.DrawString(tag, fTag, bNeon, cx - tSz.Width / 2f, bounds.Bottom - 26);
            return;
        }

        if (_settings.IsEnabled)
        {
            Point[] beam =
            {
                new(cx - 10, bounds.Y + 70),
                new(cx + 38, bounds.Y + 48),
                new(bounds.Right + 8, bounds.Bottom - 6),
                new(bounds.X + 6, bounds.Bottom - 6)
            };
            using var beamGrad = new LinearGradientBrush(
                new Point(cx, bounds.Y + 52),
                new Point(cx + 18, bounds.Bottom),
                Color.FromArgb(95, 255, 255, 255),
                Color.FromArgb(0, 255, 255, 255));
            g.FillPolygon(beamGrad, beam);
        }

        using var standPen = new Pen(Color.FromArgb(38, 48, 58), 6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(standPen, bounds.X + 24, bounds.Bottom - 12, bounds.X + 24, bounds.Y + 32);
        g.DrawLine(standPen, bounds.X + 24, bounds.Y + 32, cx, bounds.Y + 32);

        using var bodyPath = CreateRoundedPath(new Rectangle(cx - 28, bounds.Y + 20, 68, 46), 18);
        using (var bodyGrad = new LinearGradientBrush(
            new Rectangle(cx - 28, bounds.Y + 20, 68, 46),
            Color.FromArgb(64, 80, 96),
            Color.FromArgb(30, 40, 50),
            LinearGradientMode.ForwardDiagonal))
        {
            g.FillPath(bodyGrad, bodyPath);
        }

        Color coneColor = !_settings.IsEnabled
            ? Color.FromArgb(255, 90, 90)
            : Color.FromArgb(245, 250, 255);
        using var coneBrush = new SolidBrush(coneColor);
        g.FillEllipse(coneBrush, cx - 20, bounds.Y + 50, 54, 22);
        using var ringPen = new Pen(!_settings.IsEnabled ? Color.FromArgb(239, 68, 68) : Color.FromArgb(84, 174, 255), 2f);
        g.DrawEllipse(ringPen, cx - 20, bounds.Y + 50, 54, 22);
    }

    private static void DrawDropdownPill(Graphics g, Rectangle rect, string label, string value, bool hovered)
    {
        Color fill = hovered ? Color.FromArgb(82, 255, 255, 255) : Color.FromArgb(50, 255, 255, 255);
        DrawFrostedCard(g, rect, 12, fill, Color.FromArgb(50, 255, 255, 255));

        using var fLbl = UiFont(7.5F, FontStyle.Regular);
        using var fVal = UiFont(8.8F, FontStyle.Bold);
        using var bMuted = new SolidBrush(Color.FromArgb(195, 240, 240, 240));
        using var bWhite = new SolidBrush(Color.White);

        g.DrawString(label, fLbl, bMuted, rect.X + 12, rect.Y + 6);
        g.DrawString(value, fVal, bWhite, rect.X + 12, rect.Y + 23);

        using var pen = new Pen(Color.FromArgb(215, 255, 255, 255), 1.6f);
        int ax = rect.Right - 16;
        int ay = rect.Y + 27;
        g.DrawLine(pen, ax - 3, ay - 2, ax, ay + 1);
        g.DrawLine(pen, ax, ay + 1, ax + 3, ay - 2);
    }

    private void DrawMiddleActionCards(Graphics g)
    {
        Color hkFill = _isRecordingHotkey
            ? Color.FromArgb(150, 234, 88, 12)
            : (_hoveredZone == "hotkey" ? Color.FromArgb(95, 75, 85, 100) : Color.FromArgb(75, 145, 140, 136));
        DrawFrostedCard(g, _rectHotkeyCard, 20, hkFill, Color.FromArgb(60, 255, 255, 255));

        string hkTitle = _isRecordingHotkey
            ? (IsEn ? "Press new shortcut key (ESC cancels)" : "Presiona tu tecla ahora (ESC cancela)")
            : (IsEn ? "Special Key Shortcut (Click to change)" : "Tecla Rápida On/Off (Clic para cambiar)");
        string hkVal = _isRecordingHotkey ? "⌨ ..." : _settings.HotkeyText;
        DrawMetricContent(g, _rectHotkeyCard, hkTitle, hkVal);

        Color idleFill = _hoveredZone == "idletime"
            ? Color.FromArgb(95, 75, 85, 100)
            : (AppSettings.IsGamerEdition ? Color.FromArgb(95, 36, 24, 68) : Color.FromArgb(80, 52, 52, 56));
        DrawFrostedCard(g, _rectIdleTimeCard, 20, idleFill, Color.FromArgb(75, 139, 92, 246));

        if (AppSettings.IsGamerEdition)
        {
            string swTitle = IsEn
                ? "Quick Output Switcher (1-Click Headset ⇄ Speaker)"
                : "Cambio Rápido Salida (1 Clic: Audífonos ⇄ Barra)";
            string swVal = ShortenText(_engine.CurrentDeviceName, 21);
            DrawMetricContent(g, _rectIdleTimeCard, swTitle, swVal);
        }
        else
        {
            string idleTitle = IsEn
                ? "Eco Sleep Timer (Click to change)"
                : "Dormir si no uso la PC (Clic cambia)";
            string idleVal = _settings.IdleSleepEnabled
                ? $"{_settings.IdleSleepMinutes} min"
                : (IsEn ? "Off" : "Desactivado");
            DrawMetricContent(g, _rectIdleTimeCard, idleTitle, idleVal);
        }
    }

    private static void DrawMetricContent(Graphics g, Rectangle card, string label, string bigValue)
    {
        using var fLbl = UiFont(8.5F, FontStyle.Regular);
        using var fVal = UiFont(16F, FontStyle.Bold);
        using var bMuted = new SolidBrush(Color.FromArgb(205, 240, 240, 240));
        using var bVal = new SolidBrush(Color.White);

        g.DrawString(label, fLbl, bMuted, card.X + 18, card.Y + 14);
        g.DrawString(bigValue, fVal, bVal, card.X + 16, card.Y + 38);
    }

    private void DrawBottomSmartTiles(Graphics g)
    {
        DrawSmartToggleTile(
            g,
            _rectTilePower,
            IsEn ? "Protection" : "Protección",
            _settings.IsEnabled ? (IsEn ? "Active" : "Encendido") : (IsEn ? "Off (Red)" : "Apagado"),
            _settings.IsEnabled,
            lightCard: true,
            offIsRed: true);

        DrawSmartToggleTile(
            g,
            _rectTileEco,
            IsEn ? "Energy Save" : "Ahorro Luz",
            IsEn ? "Eco Sleep" : "Modo Ahorro",
            _settings.IdleSleepEnabled,
            lightCard: false,
            offIsRed: false);

        DrawSmartToggleTile(
            g,
            _rectTileStartup,
            IsEn ? "System Boot" : "Windows",
            IsEn ? "Auto-Start" : "Auto-Inicio",
            AppSettings.IsStartupEnabled(),
            lightCard: true,
            offIsRed: false);

        DrawSmartToggleTile(
            g,
            _rectTileLang,
            IsEn ? "Interface" : "Idioma",
            IsEn ? "English" : "Español",
            IsEn,
            lightCard: false,
            offIsRed: false);
    }

    private static void DrawSmartToggleTile(
        Graphics g,
        Rectangle rect,
        string subtitle,
        string mainTitle,
        bool isOn,
        bool lightCard,
        bool offIsRed)
    {
        Color tint = !isOn && offIsRed
            ? Color.FromArgb(120, 190, 45, 55)
            : (lightCard
                ? Color.FromArgb(92, 210, 206, 202)
                : Color.FromArgb(80, 92, 90, 88));

        DrawFrostedCard(g, rect, 22, tint, Color.FromArgb(65, 255, 255, 255));

        Rectangle iconCircle = new(rect.X + 14, rect.Y + 14, 28, 28);
        using (var icBrush = new SolidBrush(Color.FromArgb(55, 255, 255, 255)))
        {
            g.FillEllipse(icBrush, iconCircle);
        }
        DrawFourDotsIcon(g, iconCircle, Color.White);

        Rectangle swRect = new(rect.Right - 52, rect.Y + 17, 38, 21);
        DrawIosSwitch(g, swRect, isOn, offIsRed);

        using var fSub = UiFont(8F, FontStyle.Regular);
        using var fMain = UiFont(10.8F, FontStyle.Bold);
        using var bSub = new SolidBrush(Color.FromArgb(205, 245, 245, 245));
        using var bMain = new SolidBrush(Color.White);

        g.DrawString(subtitle, fSub, bSub, rect.X + 14, rect.Bottom - 52);
        g.DrawString(mainTitle, fMain, bMain, rect.X + 13, rect.Bottom - 33);
    }

    private static void DrawIosSwitch(Graphics g, Rectangle rect, bool isOn, bool offIsRed = false)
    {
        using var path = CreateRoundedPath(rect, rect.Height / 2);
        Color trackColor = isOn
            ? Color.FromArgb(72, 168, 255)
            : (offIsRed ? Color.FromArgb(239, 68, 68) : Color.FromArgb(110, 120, 120, 125));

        using (var trackBrush = new SolidBrush(trackColor))
        {
            g.FillPath(trackBrush, path);
        }

        int knobSize = rect.Height - 4;
        int knobX = isOn ? rect.Right - knobSize - 2 : rect.X + 2;
        using var knobBrush = new SolidBrush(Color.White);
        g.FillEllipse(knobBrush, knobX, rect.Y + 2, knobSize, knobSize);
    }

    private void DrawRightSidebar(Graphics g)
    {
        using var brushWhite = new SolidBrush(Color.White);
        using var brushMuted = new SolidBrush(Color.FromArgb(195, 235, 235, 235));

        DrawWindowControlButton(g, _rectTopMinBtn, "—", _hoveredZone == "topmin");
        DrawWindowControlButton(g, _rectTopCloseBtn, "✕", _hoveredZone == "topclose");

        string clockText = DateTime.Now.ToString("hh:mm tt");
        using var fontClock = UiFont(15.5F, FontStyle.Bold);
        var clockSize = g.MeasureString(clockText, fontClock);
        float clockX = 592 + (268 - clockSize.Width) / 2f;
        g.DrawString(clockText, fontClock, brushWhite, clockX, 22);

        using (var clockLine = new Pen(Color.FromArgb(90, 255, 255, 255), 1.5f))
        {
            g.DrawLine(clockLine, 708, 56, 744, 56);
        }

        using var fontSideHeader = UiFont(10F, FontStyle.Bold);
        g.DrawString(IsEn ? "3. Anti-Gate Power" : "3. Fuerza Anti-Gate", fontSideHeader, brushWhite, 614, 76);
        DrawIosSwitch(g, _rectSidebarSwitch, _settings.IsEnabled, offIsRed: true);

        Rectangle dialRect = new(646, 120, 160, 160);
        using (var trackPen = new Pen(Color.FromArgb(85, 190, 190, 195), 6.5f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        })
        {
            g.DrawArc(trackPen, dialRect, 135, 270);
        }

        float sweep = !_settings.IsEnabled ? 30f : (_settings.StrengthLevel switch
        {
            1 => 70f,
            2 => 125f,
            3 => 175f,
            4 => 225f,
            _ => 268f
        });
        Color arcColor = !_settings.IsEnabled
            ? Color.FromArgb(255, 85, 85)
            : Color.FromArgb(84, 174, 255);

        using (var activePen = new Pen(arcColor, 6.5f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        })
        {
            g.DrawArc(activePen, dialRect, 135, sweep);
        }

        double endAngleRad = (135.0 + sweep) * Math.PI / 180.0;
        float cx = dialRect.X + dialRect.Width / 2f;
        float cy = dialRect.Y + dialRect.Height / 2f;
        float radius = dialRect.Width / 2f;
        float dotX = cx + (float)(Math.Cos(endAngleRad) * radius) - 6f;
        float dotY = cy + (float)(Math.Sin(endAngleRad) * radius) - 6f;
        g.FillEllipse(brushWhite, dotX, dotY, 12f, 12f);

        string dialBig = !_settings.IsEnabled ? "OFF" : $"Lv.{_settings.StrengthLevel}";
        using var fontDialBig = UiFont(25F, FontStyle.Bold);
        var bigSz = g.MeasureString(dialBig, fontDialBig);
        g.DrawString(dialBig, fontDialBig, brushWhite, cx - bigSz.Width / 2f, cy - 28);

        string dialSub = !_settings.IsEnabled
            ? (IsEn ? "(Paused - Red)" : "(Apagado - Rojo)")
            : (_settings.StrengthLevel switch
            {
                1 => IsEn ? "(Soft)" : "(Suave)",
                2 => IsEn ? "(Anti-Gate AUX)" : "(Anti-Gate AUX)",
                3 => IsEn ? "(Strong AUX)" : "(Firme AUX)",
                4 => IsEn ? "(Turbo AUX)" : "(Turbo AUX)",
                _ => IsEn ? "(Max Power)" : "(Máxima Fuerza)"
            });
        using var fontDialSub = UiFont(8.2F, FontStyle.Regular);
        var subSz = g.MeasureString(dialSub, fontDialSub);
        g.DrawString(dialSub, fontDialSub, brushMuted, cx - subSz.Width / 2f, cy + 16);

        DrawCircleMiniButton(g, _rectDialMinus, "–", _hoveredZone == "minus");
        DrawCircleMiniButton(g, _rectDialPlus, "+", _hoveredZone == "plus");

        if (AppSettings.IsGamerEdition)
        {
            DrawModeSquareButton(g, _rectModeBtn1, "⌖", "FPS", _settings.StrengthLevel == 1 && _settings.IsEnabled);
            DrawModeSquareButton(g, _rectModeBtn2, "⚔", "BR+", (_settings.StrengthLevel is 2 or 3) && _settings.IsEnabled);
            DrawModeSquareButton(g, _rectModeBtn3, "⚡", "MAX", _settings.StrengthLevel >= 4 && _settings.IsEnabled);
        }
        else
        {
            DrawModeSquareButton(g, _rectModeBtn1, "☼", IsEn ? "Soft" : "Suave", _settings.StrengthLevel == 1 && _settings.IsEnabled);
            DrawModeSquareButton(g, _rectModeBtn2, "≋", "AUX+", (_settings.StrengthLevel is 2 or 3) && _settings.IsEnabled);
            DrawModeSquareButton(g, _rectModeBtn3, "⚡", "Turbo", _settings.StrengthLevel >= 4 && _settings.IsEnabled);
        }

        Color trayFill = _hoveredZone == "tray"
            ? Color.FromArgb(135, 84, 174, 255)
            : Color.FromArgb(95, 175, 172, 168);
        DrawFrostedCard(g, _rectHideToTrayCard, 22, trayFill, Color.FromArgb(85, 255, 255, 255));

        using var fTrayTitle = UiFont(10.2F, FontStyle.Bold);
        using var fTraySub = UiFont(8F, FontStyle.Regular);

        string t1 = IsEn ? "▼ Minimize to System Tray" : "▼ Ocultar junto al Reloj";
        string t2 = IsEn
            ? "1-click tray icon = Off (Red) / On"
            : "1 clic en ícono = Apagar (Rojo) / Activar";

        var t1Sz = g.MeasureString(t1, fTrayTitle);
        var t2Sz = g.MeasureString(t2, fTraySub);

        g.DrawString(t1, fTrayTitle, brushWhite, _rectHideToTrayCard.X + (_rectHideToTrayCard.Width - t1Sz.Width) / 2f, _rectHideToTrayCard.Y + 26);
        g.DrawString(t2, fTraySub, brushMuted, _rectHideToTrayCard.X + (_rectHideToTrayCard.Width - t2Sz.Width) / 2f, _rectHideToTrayCard.Y + 54);
    }

    // ==================== EXPORTADOR AUTOMÁTICO DE SCREENSHOTS Y BANNERS PARA TIENDA ====================

    public void ExportStoreMarketingImages()
    {
        string outDir = Path.Combine(AppContext.BaseDirectory, "ImagenesTienda");
        string rootStoreDir = outDir;

        Directory.CreateDirectory(outDir);

        string prevLang = _settings.Language;
        bool prevEnabled = _settings.IsEnabled;
        _settings.IsEnabled = true;

        // 1. Screenshot App en Español
        _settings.Language = "es";
        using (var bmpEs = new Bitmap(860, 520))
        {
            using (var g = Graphics.FromImage(bmpEs))
            {
                RenderDashboardToGraphics(g);
            }
            SaveBothDirs(bmpEs, "1_Screenshot_App_Espanol.png", outDir, rootStoreDir);
        }

        // 2. Screenshot App en Inglés
        _settings.Language = "en";
        using var bmpEn = new Bitmap(860, 520);
        using (var g = Graphics.FromImage(bmpEn))
        {
            RenderDashboardToGraphics(g);
        }
        SaveBothDirs(bmpEn, "2_Screenshot_App_English.png", outDir, rootStoreDir);

        // 3. Banner Principal de Tienda 1920x1080 (Hero Banner con UI real incrustada)
        using (var heroBanner = new Bitmap(1920, 1080))
        {
            using (var g = Graphics.FromImage(heroBanner))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

                using (var bg = new LinearGradientBrush(
                    new Rectangle(0, 0, 1920, 1080),
                    Color.FromArgb(24, 28, 36),
                    Color.FromArgb(52, 46, 44),
                    LinearGradientMode.ForwardDiagonal))
                {
                    g.FillRectangle(bg, 0, 0, 1920, 1080);
                }

                DrawAmbientGlow(g, -100, -100, 950, 800, Color.FromArgb(75, 72, 168, 255));
                DrawAmbientGlow(g, 900, 150, 1000, 850, Color.FromArgb(85, 185, 165, 148));

                using var fBadge = UiFont(13F, FontStyle.Bold);
                using var fHero = UiFont(44F, FontStyle.Bold);
                using var fSub = UiFont(19F, FontStyle.Regular);
                using var fBullet = UiFont(15.2F, FontStyle.Bold);
                using var bWhite = new SolidBrush(Color.White);
                using var bBlue = new SolidBrush(Color.FromArgb(96, 185, 255));

                Rectangle pillRect = new(80, 150, 480, 48);
                DrawFrostedCard(g, pillRect, 24, Color.FromArgb(75, 72, 168, 255), Color.FromArgb(140, 130, 205, 255));
                g.DrawString("★ COMPETITIVE GAMING & STUDIO AUDIO GUARD", fBadge, bWhite, 106, 163);

                g.DrawString("AudioAwake", fHero, bWhite, 76, 230);
                g.DrawString("0.0ms Zero-Delay Footstep & Audio Guard\nfor Gaming Headsets, DACs & Soundbars", fSub, bBlue, 80, 325);

                string[] features =
                {
                    "✔  Never miss the first enemy footstep in Valorant, CS2, Warzone & Tarkov",
                    "✔  Eliminates 0.5s standby cut-offs on AUX, Optical, DACs & 2.4GHz Headsets",
                    "✔  Auto-Game Detector + Presets for Razer, Logitech, Hisense, JBL & KRK",
                    "✔  Smart Eco-Sleep when AFK + Instant Mouse Wake-Boost (< 0.05% CPU)",
                    "✔  One-Click Tray Toggle (Red/Blue) & Custom Global In-Game Hotkey"
                };

                int fy = 450;
                foreach (string feat in features)
                {
                    Rectangle fCard = new(80, fy, 805, 62);
                    DrawFrostedCard(g, fCard, 16, Color.FromArgb(55, 255, 255, 255), Color.FromArgb(45, 255, 255, 255));
                    g.DrawString(feat, fBullet, bWhite, 104, fy + 18);
                    fy += 78;
                }

                Rectangle appDest = new(930, 230, 910, 550);
                using (var shadowPath = CreateRoundedPath(new Rectangle(appDest.X + 14, appDest.Y + 22, appDest.Width, appDest.Height), 34))
                using (var shadowBrush = new SolidBrush(Color.FromArgb(110, 0, 0, 0)))
                {
                    g.FillPath(shadowBrush, shadowPath);
                }

                g.DrawImage(bmpEn, appDest);
            }
            SaveBothDirs(heroBanner, "3_Banner_Tienda_1920x1080_EN.png", outDir, rootStoreDir);
        }

        // 4. Banner en Español 1920x1080
        _settings.Language = "es";
        using (var bmpEsCapture = new Bitmap(860, 520))
        {
            using (var gCap = Graphics.FromImage(bmpEsCapture))
            {
                RenderDashboardToGraphics(gCap);
            }

            using var heroEs = new Bitmap(1920, 1080);
            using (var g = Graphics.FromImage(heroEs))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

                using (var bg = new LinearGradientBrush(
                    new Rectangle(0, 0, 1920, 1080),
                    Color.FromArgb(24, 28, 36),
                    Color.FromArgb(52, 46, 44),
                    LinearGradientMode.ForwardDiagonal))
                {
                    g.FillRectangle(bg, 0, 0, 1920, 1080);
                }

                DrawAmbientGlow(g, -100, -100, 950, 800, Color.FromArgb(75, 72, 168, 255));
                DrawAmbientGlow(g, 900, 150, 1000, 850, Color.FromArgb(85, 185, 165, 148));

                using var fBadge = UiFont(13F, FontStyle.Bold);
                using var fHero = UiFont(44F, FontStyle.Bold);
                using var fSub = UiFont(19F, FontStyle.Regular);
                using var fBullet = UiFont(15.2F, FontStyle.Bold);
                using var bWhite = new SolidBrush(Color.White);
                using var bBlue = new SolidBrush(Color.FromArgb(96, 185, 255));

                Rectangle pillRect = new(80, 150, 480, 48);
                DrawFrostedCard(g, pillRect, 24, Color.FromArgb(75, 72, 168, 255), Color.FromArgb(140, 130, 205, 255));
                g.DrawString("★ AUDIO COMPETITIVO GAMING & STUDIO GUARD", fBadge, bWhite, 104, 163);

                g.DrawString("AudioAwake", fHero, bWhite, 76, 230);
                g.DrawString("0.0ms Cero Retraso en Pasos y Sonido para\nBarras de Sonido, DACs y Audífonos Gaming", fSub, bBlue, 80, 325);

                string[] features =
                {
                    "✔  Escucha el primer paso enemigo en Valorant, CS2, Warzone y Tarkov",
                    "✔  Elimina el corte de 0.5s en AUX, Óptico, Interfaces USB y Headsets 2.4GHz",
                    "✔  Auto-Detector de Juegos + Perfiles Razer, Logitech, Hisense, JBL y KRK",
                    "✔  Modo Ahorro AFK + Despertar instantáneo con el mouse (< 0.05% CPU)",
                    "✔  Apagado en 1 Clic (Ícono Rojo/Azul) y Tecla Rápida In-Game"
                };

                int fy = 450;
                foreach (string feat in features)
                {
                    Rectangle fCard = new(80, fy, 805, 62);
                    DrawFrostedCard(g, fCard, 16, Color.FromArgb(55, 255, 255, 255), Color.FromArgb(45, 255, 255, 255));
                    g.DrawString(feat, fBullet, bWhite, 104, fy + 18);
                    fy += 78;
                }

                Rectangle appDest = new(930, 230, 910, 550);
                using (var shadowPath = CreateRoundedPath(new Rectangle(appDest.X + 14, appDest.Y + 22, appDest.Width, appDest.Height), 34))
                using (var shadowBrush = new SolidBrush(Color.FromArgb(110, 0, 0, 0)))
                {
                    g.FillPath(shadowBrush, shadowPath);
                }

                g.DrawImage(bmpEsCapture, appDest);
            }
            SaveBothDirs(heroEs, "4_Banner_Tienda_1920x1080_ES.png", outDir, rootStoreDir);
        }

        _settings.Language = prevLang;
        _settings.IsEnabled = prevEnabled;
    }

    private static void SaveBothDirs(Bitmap bmp, string fileName, string dir1, string dir2)
    {
        try { bmp.Save(Path.Combine(dir1, fileName), ImageFormat.Png); } catch { }
        try
        {
            if (!string.Equals(dir1, dir2, StringComparison.OrdinalIgnoreCase))
            {
                bmp.Save(Path.Combine(dir2, fileName), ImageFormat.Png);
            }
        }
        catch { }
    }

    // ==================== UTILIDADES DE DIBUJO CRISTAL ====================

    private static void DrawFrostedCard(Graphics g, Rectangle rect, int radius, Color fillColor, Color borderColor)
    {
        using GraphicsPath path = CreateRoundedPath(rect, radius);

        using (var brush = new LinearGradientBrush(
            rect,
            Color.FromArgb(Math.Min(255, fillColor.A + 18), fillColor.R, fillColor.G, fillColor.B),
            fillColor,
            LinearGradientMode.Vertical))
        {
            g.FillPath(brush, path);
        }

        using var pen = new Pen(borderColor, 1.1f);
        g.DrawPath(pen, path);
    }

    private static void DrawCircleMiniButton(Graphics g, Rectangle rect, string symbol, bool highlighted)
    {
        Color bg = highlighted ? Color.FromArgb(130, 84, 174, 255) : Color.FromArgb(65, 255, 255, 255);
        using (var b = new SolidBrush(bg))
        {
            g.FillEllipse(b, rect);
        }
        using (var p = new Pen(Color.FromArgb(80, 255, 255, 255), 1f))
        {
            g.DrawEllipse(p, rect);
        }

        using var f = UiFont(10F, FontStyle.Bold);
        using var tb = new SolidBrush(Color.White);
        var sz = g.MeasureString(symbol, f);
        g.DrawString(symbol, f, tb, rect.X + (rect.Width - sz.Width) / 2f, rect.Y + (rect.Height - sz.Height) / 2f);
    }

    private static void DrawWindowControlButton(Graphics g, Rectangle rect, string symbol, bool hovered)
    {
        using var b = new SolidBrush(hovered ? Color.FromArgb(105, 255, 255, 255) : Color.FromArgb(38, 255, 255, 255));
        g.FillEllipse(b, rect);
        using var f = UiFont(8.2F, FontStyle.Bold);
        using var tb = new SolidBrush(Color.White);
        var sz = g.MeasureString(symbol, f);
        g.DrawString(symbol, f, tb, rect.X + (rect.Width - sz.Width) / 2f, rect.Y + (rect.Height - sz.Height) / 2f);
    }

    private static void DrawModeSquareButton(Graphics g, Rectangle rect, string icon, string label, bool active)
    {
        Color fill = active ? Color.FromArgb(125, 84, 174, 255) : Color.FromArgb(55, 200, 200, 205);
        Color border = active ? Color.FromArgb(190, 140, 210, 255) : Color.FromArgb(55, 255, 255, 255);
        DrawFrostedCard(g, rect, 14, fill, border);

        using var fIcon = new Font("Segoe UI Symbol", 11F, FontStyle.Regular);
        using var fLbl = UiFont(8F, FontStyle.Bold);
        using var bWhite = new SolidBrush(Color.White);

        var isz = g.MeasureString(icon, fIcon);
        g.DrawString(icon, fIcon, bWhite, rect.X + (rect.Width - isz.Width) / 2f, rect.Y + 8);

        var lsz = g.MeasureString(label, fLbl);
        g.DrawString(label, fLbl, bWhite, rect.X + (rect.Width - lsz.Width) / 2f, rect.Y + 34);
    }

    private static void DrawFourDotsIcon(Graphics g, Rectangle circle, Color color)
    {
        using var b = new SolidBrush(color);
        int cx = circle.X + circle.Width / 2;
        int cy = circle.Y + circle.Height / 2;
        g.FillEllipse(b, cx - 5, cy - 5, 3, 3);
        g.FillEllipse(b, cx + 2, cy - 5, 3, 3);
        g.FillEllipse(b, cx - 5, cy + 2, 3, 3);
        g.FillEllipse(b, cx + 2, cy + 2, 3, 3);
    }

    private static GraphicsPath CreateRoundedPath(Rectangle rect, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static string ShortenText(string text, int maxLen)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Length <= maxLen ? text : text[..(maxLen - 1)] + "…";
    }

    // ==================== INTERACCIÓN Y EVENTOS ====================

    private void OnDashboardMouseMove(object? sender, MouseEventArgs e)
    {
        string zone = "";
        Point pt = e.Location;

        if (_rectDevicePill.Contains(pt)) zone = "device";
        else if (_rectBrandPill.Contains(pt)) zone = "brand";
        else if (_rectTestSliderBar.Contains(pt)) zone = "test";
        else if (_rectHotkeyCard.Contains(pt)) zone = "hotkey";
        else if (_rectIdleTimeCard.Contains(pt)) zone = "idletime";
        else if (_rectTilePower.Contains(pt) || _rectSidebarSwitch.Contains(pt)) zone = "power";
        else if (_rectTileEco.Contains(pt)) zone = "eco";
        else if (_rectTileStartup.Contains(pt)) zone = "startup";
        else if (_rectTileLang.Contains(pt)) zone = "lang";
        else if (_rectDialMinus.Contains(pt)) zone = "minus";
        else if (_rectDialPlus.Contains(pt)) zone = "plus";
        else if (_rectModeBtn1.Contains(pt) || _rectModeBtn2.Contains(pt) || _rectModeBtn3.Contains(pt)) zone = "level";
        else if (_rectHideToTrayCard.Contains(pt) || _rectTopMinBtn.Contains(pt)) zone = "tray";
        else if (_rectTopCloseBtn.Contains(pt)) zone = "topclose";

        Cursor = string.IsNullOrEmpty(zone) ? Cursors.Default : Cursors.Hand;

        if (zone != _hoveredZone)
        {
            _hoveredZone = zone;
            Invalidate();
        }
    }

    private void OnDashboardMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        Point pt = e.Location;

        if (_rectTestSliderBar.Contains(pt))
        {
            _isTestingAudio = true;
            _engine.SetTestTone(true);
            Invalidate();
            return;
        }

        if (_rectDevicePill.Contains(pt))
        {
            ShowDeviceSelectorPopup();
            return;
        }

        if (_rectBrandPill.Contains(pt))
        {
            if (!_settings.IsProUnlocked && PromptProLicenseUnlock()) return;
            ShowBrandSelectorPopup();
            return;
        }

        if (_rectHotkeyCard.Contains(pt))
        {
            if (!_settings.IsProUnlocked && PromptProLicenseUnlock()) return;
            _isRecordingHotkey = !_isRecordingHotkey;
            Invalidate();
            return;
        }

        if (_rectIdleTimeCard.Contains(pt))
        {
            if (!_settings.IsProUnlocked && PromptProLicenseUnlock()) return;
            CycleIdleMinutes();
            return;
        }

        if (_rectTilePower.Contains(pt) || _rectSidebarSwitch.Contains(pt))
        {
            ToggleEnabled();
            return;
        }

        if (_rectTileEco.Contains(pt))
        {
            if (!_settings.IsProUnlocked && PromptProLicenseUnlock()) return;
            _settings.IdleSleepEnabled = !_settings.IdleSleepEnabled;
            _settings.Save();
            _engine.ApplySettingsLive();
            Invalidate();
            return;
        }

        if (_rectTileStartup.Contains(pt))
        {
            if (!_settings.IsProUnlocked && PromptProLicenseUnlock()) return;
            AppSettings.SetStartupEnabled(!AppSettings.IsStartupEnabled());
            SyncTrayAndInvalidate();
            return;
        }

        if (_rectTileLang.Contains(pt))
        {
            _settings.Language = IsEn ? "es" : "en";
            _settings.Save();
            _currentDevices = _engine.GetOutputDevices(IsEn);
            SyncTrayAndInvalidate();
            return;
        }

        if (_rectDialMinus.Contains(pt))
        {
            if (!_settings.IsProUnlocked && PromptProLicenseUnlock()) return;
            SetStrengthLevel(Math.Max(1, _settings.StrengthLevel - 1));
            return;
        }
        if (_rectDialPlus.Contains(pt))
        {
            if (!_settings.IsProUnlocked && PromptProLicenseUnlock()) return;
            SetStrengthLevel(Math.Min(5, _settings.StrengthLevel + 1));
            return;
        }
        if (_rectModeBtn1.Contains(pt)) { SetStrengthLevel(1); return; }
        if (_rectModeBtn2.Contains(pt))
        {
            if (!_settings.IsProUnlocked && PromptProLicenseUnlock()) return;
            SetStrengthLevel(2);
            return;
        }
        if (_rectModeBtn3.Contains(pt))
        {
            if (!_settings.IsProUnlocked && PromptProLicenseUnlock()) return;
            SetStrengthLevel(4);
            return;
        }

        if (_rectHideToTrayCard.Contains(pt) || _rectTopMinBtn.Contains(pt))
        {
            HideToTray();
            return;
        }
        if (_rectTopCloseBtn.Contains(pt))
        {
            ExitApplication();
            return;
        }

        ReleaseCapture();
        SendMessage(Handle, 0xA1, 0x2, 0);
    }

    private void OnDashboardMouseUp(object? sender, MouseEventArgs e)
    {
        if (_isTestingAudio)
        {
            _isTestingAudio = false;
            _engine.SetTestTone(false);
            Invalidate();
        }
    }

    private bool PromptProLicenseUnlock()
    {
        if (_settings.IsProUnlocked) return false;

        using var dlg = new Form
        {
            Text = IsEn ? "Unlock AudioAwake PRO" : "Desbloquear AudioAwake PRO",
            Size = new Size(440, 230),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = Color.FromArgb(30, 32, 38),
            ForeColor = Color.White,
            Font = UiFont(9.5F)
        };

        var lblInfo = new Label
        {
            Location = new Point(20, 18),
            Size = new Size(385, 58),
            Text = IsEn
                ? "🔒 This feature requires AudioAwake PRO (Brand Presets, Lv.2-5 Anti-Gate Turbo, Eco-Sleep & Auto-Start).\n\nEnter your PRO License Key below:"
                : "🔒 Esta función es exclusiva de AudioAwake PRO (Perfiles de Marca, Niveles 2-5 Turbo, Modo Ahorro y Auto-Inicio).\n\nIngresa tu Clave de Licencia PRO:"
        };

        var txtKey = new TextBox
        {
            Location = new Point(20, 84),
            Size = new Size(385, 28),
            BackColor = Color.FromArgb(46, 50, 60),
            ForeColor = Color.White,
            Font = UiFont(10.5F, FontStyle.Bold),
            CharacterCasing = CharacterCasing.Upper
        };

        var btnActivate = new Button
        {
            Location = new Point(20, 126),
            Size = new Size(185, 38),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(56, 152, 255),
            ForeColor = Color.White,
            Font = UiFont(9.5F, FontStyle.Bold),
            Text = IsEn ? "Activate PRO" : "Activar PRO",
            Cursor = Cursors.Hand
        };
        btnActivate.FlatAppearance.BorderSize = 0;

        var btnClose = new Button
        {
            Location = new Point(220, 126),
            Size = new Size(185, 38),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(58, 62, 72),
            ForeColor = Color.White,
            Font = UiFont(9.2F),
            Text = IsEn ? "Continue Free Lite" : "Seguir en modo Gratis",
            Cursor = Cursors.Hand
        };
        btnClose.FlatAppearance.BorderSize = 0;
        btnClose.Click += (_, _) => dlg.Close();

        btnActivate.Click += (_, _) =>
        {
            if (AppSettings.VerifyLicenseKey(txtKey.Text))
            {
                _settings.LicenseKey = txtKey.Text.Trim().ToUpperInvariant();
                _settings.Save();
                MessageBox.Show(
                    IsEn ? "🎉 AudioAwake PRO Unlocked! All features are now active." : "🎉 ¡AudioAwake PRO Activado! Todas las funciones están desbloqueadas.",
                    "AudioAwake PRO",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                dlg.Close();
            }
            else
            {
                MessageBox.Show(
                    IsEn ? "Invalid License Key. Please check your key." : "Clave de licencia no válida. Verifica tu código.",
                    "AudioAwake PRO",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        };

        dlg.Controls.Add(lblInfo);
        dlg.Controls.Add(txtKey);
        dlg.Controls.Add(btnActivate);
        dlg.Controls.Add(btnClose);
        dlg.ShowDialog(this);

        Invalidate();
        return !_settings.IsProUnlocked;
    }

    private void ShowDeviceSelectorPopup()
    {
        _currentDevices = _engine.GetOutputDevices(IsEn);
        var menu = new ContextMenuStrip
        {
            BackColor = Color.FromArgb(42, 44, 48),
            ForeColor = Color.White,
            Font = UiFont(9.5F)
        };

        foreach (var dev in _currentDevices)
        {
            var item = new ToolStripMenuItem(dev.FriendlyName)
            {
                Checked = dev.Id == _settings.SelectedDeviceId
            };
            item.Click += (_, _) =>
            {
                _settings.SelectedDeviceId = dev.Id;
                _settings.Save();
                _engine.StartOrRestart();
                Invalidate();
            };
            menu.Items.Add(item);
        }

        menu.Show(this, new Point(_rectDevicePill.X, _rectDevicePill.Bottom + 4));
    }

    private void ShowBrandSelectorPopup()
    {
        var menu = new ContextMenuStrip
        {
            BackColor = Color.FromArgb(42, 44, 48),
            ForeColor = Color.White,
            Font = UiFont(9.5F)
        };

        for (int i = 0; i < AudioKeepAliveEngine.Profiles.Count; i++)
        {
            int index = i;
            var p = AudioKeepAliveEngine.Profiles[i];
            var item = new ToolStripMenuItem(IsEn ? p.NameEn : p.NameEs)
            {
                Checked = _settings.BrandProfileIndex == index
            };
            item.Click += (_, _) =>
            {
                _settings.BrandProfileIndex = index;
                _settings.Save();
                _engine.ApplySettingsLive();
                Invalidate();
            };
            menu.Items.Add(item);
        }

        menu.Show(this, new Point(_rectBrandPill.X, _rectBrandPill.Bottom + 4));
    }

    private void CycleIdleMinutes()
    {
        if (AppSettings.IsGamerEdition)
        {
            _currentDevices = _engine.GetOutputDevices(IsEn);
            if (_currentDevices.Count > 1)
            {
                int curIdx = _currentDevices.FindIndex(d => d.Id == _settings.SelectedDeviceId);
                int nextIdx = (curIdx + 1) % _currentDevices.Count;
                if (nextIdx == 0 && _currentDevices.Count > 1) nextIdx = 1; // Saltar "Automático" al alternar rápido
                _settings.SelectedDeviceId = _currentDevices[nextIdx].Id;
                _settings.Save();
                _engine.StartOrRestart();
                Invalidate();
            }
            return;
        }

        int idx = Array.IndexOf(_idleMinuteOptions, _settings.IdleSleepMinutes);
        idx = (idx + 1) % _idleMinuteOptions.Length;
        _settings.IdleSleepMinutes = _idleMinuteOptions[idx];
        _settings.IdleSleepEnabled = true;
        _settings.Save();
        _engine.ApplySettingsLive();
        Invalidate();
    }

    private void SetStrengthLevel(int level)
    {
        _settings.StrengthLevel = Math.Clamp(level, 1, 5);
        _settings.IsEnabled = true;
        _settings.Save();
        _engine.ApplySettingsLive();
        SyncTrayAndInvalidate();
    }

    private void AutoSelectPreferredDeviceIfEmpty()
    {
        if (!string.IsNullOrEmpty(_settings.SelectedDeviceId)) return;
        foreach (var dev in _currentDevices)
        {
            if (dev.IsPreferredInterface)
            {
                _settings.SelectedDeviceId = dev.Id;
                _settings.Save();
                break;
            }
        }
    }

    // ==================== TECLA ESPECIAL GLOBAL ====================

    private void OnFormKeyDownForHotkey(object? sender, KeyEventArgs e)
    {
        if (!_isRecordingHotkey) return;

        e.SuppressKeyPress = true;
        e.Handled = true;

        if (e.KeyCode == Keys.Escape)
        {
            _isRecordingHotkey = false;
            Invalidate();
            return;
        }

        if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
        {
            return;
        }

        uint mods = 0;
        var parts = new List<string>();
        if (e.Control) { mods |= MOD_CONTROL; parts.Add("Ctrl"); }
        if (e.Alt) { mods |= MOD_ALT; parts.Add("Alt"); }
        if (e.Shift) { mods |= MOD_SHIFT; parts.Add("Shift"); }

        parts.Add(e.KeyCode.ToString());
        _settings.HotkeyModifiers = mods;
        _settings.HotkeyVirtualKey = (int)e.KeyCode;
        _settings.HotkeyText = string.Join(" + ", parts);
        _settings.Save();

        _isRecordingHotkey = false;
        RegisterCustomGlobalHotkey();
        Invalidate();
    }

    private void RegisterCustomGlobalHotkey()
    {
        if (!IsHandleCreated) return;
        try
        {
            UnregisterHotKey(Handle, HOTKEY_ID);
            if (_settings.HotkeyVirtualKey > 0)
            {
                RegisterHotKey(Handle, HOTKEY_ID, _settings.HotkeyModifiers, (uint)_settings.HotkeyVirtualKey);
            }
        }
        catch
        {
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID && !_isRecordingHotkey)
        {
            ToggleEnabled(showBalloonNotification: true);
        }
        base.WndProc(ref m);
    }

    // ==================== BANDEJA DEL SISTEMA ====================

    private void CreateDynamicIcons()
    {
        _iconActive = GenerateBadgeIcon(
            Color.FromArgb(32, 42, 58),
            Color.FromArgb(84, 174, 255),
            Color.FromArgb(240, 248, 255),
            isOffRed: false);

        _iconStandby = GenerateBadgeIcon(
            Color.FromArgb(38, 44, 52),
            Color.FromArgb(125, 211, 252),
            Color.FromArgb(186, 230, 253),
            isOffRed: false);

        _iconOffRed = GenerateBadgeIcon(
            Color.FromArgb(72, 16, 22),
            Color.FromArgb(255, 65, 75),
            Color.FromArgb(255, 130, 130),
            isOffRed: true);

        Icon = _iconActive;
    }

    private static Icon GenerateBadgeIcon(Color bgFill, Color ringColor, Color waveColor, bool isOffRed)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using (var bgBrush = new SolidBrush(bgFill))
            {
                g.FillEllipse(bgBrush, 1, 1, 30, 30);
            }

            using (var borderPen = new Pen(ringColor, 2.6f))
            {
                g.DrawEllipse(borderPen, 2, 2, 28, 28);
            }

            using var wavePen = new Pen(waveColor, 2.7f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };

            if (isOffRed)
            {
                g.DrawLine(wavePen, 11, 11, 21, 21);
                g.DrawLine(wavePen, 21, 11, 11, 21);
            }
            else
            {
                g.DrawLine(wavePen, 9, 16, 9, 16);
                g.DrawLine(wavePen, 13, 11, 13, 21);
                g.DrawLine(wavePen, 17, 7, 17, 25);
                g.DrawLine(wavePen, 21, 11, 21, 21);
                g.DrawLine(wavePen, 25, 15, 25, 17);
            }
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    private ContextMenuStrip BuildTrayMenu()
    {
        var menu = new ContextMenuStrip();

        _menuOpenItem = new ToolStripMenuItem("🗔 Abrir Dashboard de AudioAwake", null, (_, _) => ShowFromTray())
        {
            Font = UiFont(9.2F, FontStyle.Bold)
        };
        menu.Items.Add(_menuOpenItem);
        menu.Items.Add(new ToolStripSeparator());

        _menuToggleItem = new ToolStripMenuItem("Activado", null, (_, _) => ToggleEnabled());
        menu.Items.Add(_menuToggleItem);

        _menuStartupItem = new ToolStripMenuItem("Iniciar con Windows", null, (_, _) =>
        {
            AppSettings.SetStartupEnabled(!AppSettings.IsStartupEnabled());
            SyncTrayAndInvalidate();
        });
        menu.Items.Add(_menuStartupItem);

        menu.Items.Add(new ToolStripSeparator());
        _menuExitItem = new ToolStripMenuItem("Cerrar programa", null, (_, _) => ExitApplication());
        menu.Items.Add(_menuExitItem);

        return menu;
    }

    private void OnTrayIconMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            ToggleEnabled(showBalloonNotification: true);
        }
    }

    private void ToggleEnabled(bool showBalloonNotification = false)
    {
        _settings.IsEnabled = !_settings.IsEnabled;
        _settings.Save();
        _engine.ApplySettingsLive();
        SyncTrayAndInvalidate();

        if (showBalloonNotification)
        {
            bool on = _settings.IsEnabled;
            string title = on
                ? (IsEn ? "🟢 AudioAwake: ON" : "🟢 AudioAwake: ACTIVADO")
                : (IsEn ? "🔴 AudioAwake: OFF" : "🔴 AudioAwake: APAGADO");
            string body = on
                ? (IsEn ? "Speaker standby protection is active." : "Tu barra de sonido se mantendrá despierta.")
                : (IsEn ? "Protection paused (Red icon)." : "Protección pausada (Ícono en rojo).");

            _trayIcon.ShowBalloonTip(1500, title, body, ToolTipIcon.None);
        }
    }

    private void SyncTrayAndInvalidate()
    {
        bool enabled = _settings.IsEnabled;
        bool en = IsEn;

        if (!enabled)
        {
            _trayIcon.Icon = _iconOffRed;
            Icon = _iconOffRed;
        }
        else if (_engine.IsSleepingDueToIdle)
        {
            _trayIcon.Icon = _iconStandby;
            Icon = _iconStandby;
        }
        else
        {
            _trayIcon.Icon = _iconActive;
            Icon = _iconActive;
        }

        _menuOpenItem.Text = en ? "🗔 Open AudioAwake Dashboard" : "🗔 Abrir Dashboard de AudioAwake";
        _menuToggleItem.Checked = enabled;
        _menuToggleItem.Text = en
            ? (enabled ? $"🟢 Active — Click to Turn Off ({_settings.HotkeyText})" : $"🔴 Off — Click to Turn On ({_settings.HotkeyText})")
            : (enabled ? $"🟢 Encendido — Clic para Apagar ({_settings.HotkeyText})" : $"🔴 Apagado — Clic para Encender ({_settings.HotkeyText})");
        _menuStartupItem.Checked = AppSettings.IsStartupEnabled();
        _menuStartupItem.Text = en ? "Start with Windows" : "Iniciar al prender la PC";
        _menuExitItem.Text = en ? "Quit AudioAwake" : "Cerrar programa";

        Invalidate();
    }

    private void StartSingleInstanceListener()
    {
        try
        {
            _showWindowEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowWindowEventName);
            Task.Run(() =>
            {
                while (!_allowExit)
                {
                    if (_showWindowEvent.WaitOne(500))
                    {
                        if (_allowExit) break;
                        BeginInvoke(new Action(ShowFromTray));
                    }
                }
            });
        }
        catch
        {
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_allowExit && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        try
        {
            if (IsHandleCreated)
            {
                UnregisterHotKey(Handle, HOTKEY_ID);
            }
        }
        catch
        {
        }

        _uiTimer.Stop();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _engine.Dispose();
        base.OnFormClosing(e);
    }

    private void HideToTray()
    {
        Hide();
        ShowInTaskbar = false;

        if (!_shownTrayBalloon)
        {
            _shownTrayBalloon = true;
            _trayIcon.ShowBalloonTip(
                3000,
                IsEn ? "AudioAwake is active in the tray" : "AudioAwake sigue activo junto al reloj",
                IsEn
                    ? $"Left-click tray icon or press {_settings.HotkeyText} to toggle On/Off (Red). Right-click to open."
                    : $"1 clic en el ícono o {_settings.HotkeyText} apaga (Rojo) / enciende. Clic derecho abre el panel.",
                ToolTipIcon.Info);
        }
    }

    private void ShowFromTray()
    {
        Show();
        ShowInTaskbar = true;
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        _allowExit = true;
        try { _showWindowEvent?.Set(); } catch { }
        Close();
    }
}
