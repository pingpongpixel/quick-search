using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace QuickSearch
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            bool silent = false;
            bool openSettings = false;
            bool installStartup = false;
            bool uninstallStartup = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--silent", StringComparison.OrdinalIgnoreCase)) silent = true;
                else if (string.Equals(args[i], "--settings", StringComparison.OrdinalIgnoreCase)) openSettings = true;
                else if (string.Equals(args[i], "--install-startup", StringComparison.OrdinalIgnoreCase)) installStartup = true;
                else if (string.Equals(args[i], "--uninstall-startup", StringComparison.OrdinalIgnoreCase)) uninstallStartup = true;
            }
            if (openSettings) silent = true;

            if (installStartup || uninstallStartup)
            {
                if (installStartup) StartupManager.Install();
                else StartupManager.Uninstall();
                return;
            }

            bool created;
            using (Mutex mutex = new Mutex(true, "QuickSearch.SingleInstance.v1", out created))
            {
                if (!created)
                {
                    IntPtr existing = FindWindow(null, "Quick Search");
                    if (existing != IntPtr.Zero) PostMessage(existing, WM_SHOW_LAUNCHER, IntPtr.Zero, IntPtr.Zero);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new SearchForm(silent, openSettings));
            }
        }

        internal const int WM_SHOW_LAUNCHER = 0x8001;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    }

    internal static class StartupManager
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "Quick Search";

        // Older builds used a Startup-folder shortcut. It is removed on install so the
        // app never gets launched twice at sign-in.
        private static string LegacyShortcutPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                    "Quick Search.lnk");
            }
        }

        private static string Command
        {
            get { return "\"" + Application.ExecutablePath + "\" --silent"; }
        }

        public static bool IsInstalled()
        {
            return IsInstalledForCurrentExe() || File.Exists(LegacyShortcutPath);
        }

        public static bool IsInstalledForCurrentExe()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    if (key == null) return false;
                    string value = key.GetValue(RunValueName) as string;
                    if (string.IsNullOrEmpty(value)) return false;
                    return value.IndexOf(Application.ExecutablePath, StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch
            {
                return false;
            }
        }

        public static void Install()
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
            {
                if (key == null)
                    throw new InvalidOperationException("Could not open the Windows startup registry key.");
                key.SetValue(RunValueName, Command, RegistryValueKind.String);
            }

            RemoveLegacyShortcut();
        }

        public static void Uninstall()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key != null) key.DeleteValue(RunValueName, false);
                }
            }
            catch
            {
            }

            RemoveLegacyShortcut();
        }

        private static void RemoveLegacyShortcut()
        {
            try
            {
                if (File.Exists(LegacyShortcutPath)) File.Delete(LegacyShortcutPath);
            }
            catch
            {
            }
        }
    }

    internal sealed class AppEntry
    {
        public string Name;
        public string NameLower;
        public string Initials;
        public string Keywords;
        public string Launch;
        public string Args;

        public AppEntry(string name, string launch, string args, string keywords)
        {
            Name = name;
            NameLower = name.ToLowerInvariant();
            Initials = MakeInitials(name);
            Keywords = (keywords == null ? string.Empty : keywords.ToLowerInvariant());
            Launch = launch;
            Args = args;
        }

        private static string MakeInitials(string name)
        {
            StringBuilder sb = new StringBuilder();
            bool boundary = true;
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (char.IsLetterOrDigit(c))
                {
                    if (boundary) sb.Append(char.ToLowerInvariant(c));
                    boundary = false;
                }
                else
                {
                    boundary = true;
                }
            }
            return sb.ToString();
        }
    }

    internal sealed class SearchForm : Form
    {
        private const int WM_HOTKEY = 0x0312;
        private const int WM_NCHITTEST = 0x0084;
        private const int WM_EXITSIZEMOVE = 0x0232;
        private const int HTCLIENT = 1;
        private const int HTCAPTION = 2;
        private const int HOTKEY_ID = 0x51;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_NOREPEAT = 0x4000;
        private const int EM_SETMARGINS = 0x00D3;
        private const int EC_LEFTMARGIN = 0x0001;
        private const int EC_RIGHTMARGIN = 0x0002;
        private const int EM_SETCUEBANNER = 0x1501;

        private const int BaseWidth = 584;
        private const int BaseTopPad = 7;
        private const int BaseRowHeight = 20;
        private const int BaseBottomPad = 8;
        private const int BaseMinWidth = 240;
        private const int BaseResizeBorder = 4;
        private const int BaseSettingsDotSize = 8;
        private const int BaseSettingsDotArea = 24;
        private const int DefaultRows = 5;

        private enum ResizeEdge
        {
            None,
            Left,
            Right,
            Top,
            Bottom,
            TopLeft,
            TopRight,
            BottomLeft,
            BottomRight
        }

        private readonly List<AppEntry> _all = new List<AppEntry>();
        private readonly List<AppEntry> _view = new List<AppEntry>();
        private readonly TextBox _input;
        private readonly NotifyIcon _tray;
        private readonly ContextMenuStrip _trayMenu;
        private readonly ToolStripMenuItem _startupItem;
        private readonly bool _silentStart;
        private readonly bool _openSettingsOnStart;

        private int _selected;
        private int _scroll;
        private float _scale = 1f;
        private bool _indexed;
        private bool _exiting;
        private bool _hotkeyOk;
        private bool _allowShow;
        private int _visibleRows = DefaultRows;
        private Size _clientSize = Size.Empty;
        private Point? _savedLocation;
        private int _opacityPercent = 100;
        private Form _settingsDialog;
        private bool _resizing;
        private bool _moving;
        private bool _startupEnabled;
        private bool _startupChoiceSaved;
        private bool _hasBeenShown;
        private Point _moveStartCursor;
        private Point _moveStartLocation;
        private ResizeEdge _resizeEdge = ResizeEdge.None;
        private ResizeEdge _hoverEdge = ResizeEdge.None;
        private bool _settingsHot;
        private Rectangle _resizeStartBounds;
        private Point _resizeStartCursor;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        public SearchForm(bool silentStart, bool openSettingsOnStart)
        {
            _silentStart = silentStart;
            _openSettingsOnStart = openSettingsOnStart;
            Text = "Quick Search";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Color.FromArgb(30, 30, 30);
            AutoScaleMode = AutoScaleMode.None;
            KeyPreview = true;
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);

            _input = new TextBox();
            _input.BorderStyle = BorderStyle.None;
            _input.BackColor = Color.FromArgb(45, 45, 45);
            _input.ForeColor = Color.White;
            _input.Font = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
            _input.AutoSize = false;
            _input.TabStop = true;
            _input.TextChanged += delegate { RefreshResults(false); };
            _input.KeyDown += InputKeyDown;
            Controls.Add(_input);

            _startupItem = new ToolStripMenuItem("Start with Windows");
            _startupItem.Click += delegate { ToggleStartup(); };

            _trayMenu = new ContextMenuStrip();
            _trayMenu.Items.Add("Show Quick Search", null, delegate { ShowLauncher(); });
            _trayMenu.Items.Add("Settings\u2026", null, delegate { ShowSettings(); });
            _trayMenu.Items.Add(_startupItem);
            _trayMenu.Items.Add("Reset Window", null, delegate { ResetWindow(); });
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add("Exit", null, delegate { ExitApp(); });
            _trayMenu.Opening += delegate { _startupItem.Checked = StartupManager.IsInstalled(); };

            _tray = new NotifyIcon();
            _tray.Icon = LoadIcon();
            _tray.Text = "Quick Search";
            _tray.ContextMenuStrip = _trayMenu;
            _tray.Visible = true;
            _tray.DoubleClick += delegate { ShowLauncher(); };
        }

        protected override void SetVisibleCore(bool value)
        {
            if (!IsHandleCreated)
            {
                CreateHandle();
                if (_silentStart && !_allowShow)
                {
                    base.SetVisibleCore(false);
                    return;
                }
            }
            base.SetVisibleCore(value);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Trace("OnHandleCreated");
            _scale = DeviceDpi / 96f;
            LoadSettings();
            Opacity = _opacityPercent / 100.0;
            ApplyLayout();

            IntPtr margins = new IntPtr((Scale(4)) | (Scale(4) << 16));
            SendMessage(_input.Handle, EM_SETMARGINS, (IntPtr)(EC_LEFTMARGIN | EC_RIGHTMARGIN), margins);
            SendMessage(_input.Handle, EM_SETCUEBANNER, (IntPtr)1, "Search apps\u2026");

            _hotkeyOk = RegisterHotKey(Handle, HOTKEY_ID, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, (uint)Keys.Space);

            // Start with Windows by default, and repair the entry if it is missing or
            // points at an old executable location.
            if (!_startupChoiceSaved || (_startupEnabled && !StartupManager.IsInstalledForCurrentExe()))
                TryEnableStartup();

            _startupItem.Checked = StartupManager.IsInstalled();

            if (_openSettingsOnStart)
            {
                BeginInvoke(new MethodInvoker(delegate { ShowSettings(); }));
            }
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            _scale = DeviceDpi / 96f;
            ApplyLayout();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ShowLauncher();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_exiting)
            {
                e.Cancel = true;
                HideLauncher();
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_hotkeyOk) UnregisterHotKey(Handle, HOTKEY_ID);
            _tray.Visible = false;
            _tray.Dispose();
            base.OnFormClosed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID)
            {
                if (Visible && ContainsFocus) HideLauncher();
                else ShowLauncher();
                return;
            }

            if (m.Msg == Program.WM_SHOW_LAUNCHER)
            {
                ShowLauncher();
                return;
            }

            if (m.Msg == WM_EXITSIZEMOVE)
            {
                SaveSettings();
            }

            base.WndProc(ref m);
        }

        private int Scale(int value)
        {
            return (int)Math.Round(value * _scale);
        }

        private int RowHeight { get { return Scale(BaseRowHeight); } }
        private int TopPad { get { return Scale(BaseTopPad); } }
        private int BottomPad { get { return Scale(BaseBottomPad); } }
        private int InputTop { get { return TopPad; } }
        private int ResultTop { get { return TopPad + RowHeight; } }

        private int DefaultHeight()
        {
            return HeightForRows(DefaultRows);
        }

        private int HeightForRows(int rows)
        {
            return TopPad + ((rows + 1) * RowHeight) + BottomPad;
        }

        private int RowsForHeight(int height)
        {
            int rows = (height - TopPad - RowHeight - BottomPad + (RowHeight / 2)) / RowHeight;
            if (rows < 1) rows = 1;
            return rows;
        }

        private void ApplyLayout()
        {
            if (_clientSize.Width <= 0 || _clientSize.Height <= 0)
                _clientSize = new Size(Scale(BaseWidth), DefaultHeight());

            _clientSize = ClampSize(_clientSize.Width, _clientSize.Height);
            ClientSize = _clientSize;
            _visibleRows = RowsForHeight(ClientSize.Height);
            PositionInput();
            ClampSelection();
            Invalidate();
        }

        private Size ClampSize(int width, int height)
        {
            int minWidth = Scale(BaseMinWidth);
            int maxHeight = HeightForRows(1);

            Rectangle wa = Screen.FromHandle(Handle).WorkingArea;
            int maxWidth = Math.Max(minWidth, wa.Width);
            maxHeight = Math.Max(maxHeight, wa.Height);

            if (width < minWidth) width = minWidth;
            if (width > maxWidth) width = maxWidth;

            int rows = RowsForHeight(height);
            int maxRows = Math.Max(1, RowsForHeight(maxHeight));
            if (rows > maxRows) rows = maxRows;

            return new Size(width, HeightForRows(rows));
        }

        private void PositionInput()
        {
            int pref = _input.PreferredHeight;
            int y = InputTop + Math.Max(0, (RowHeight - pref) / 2);
            int width = Math.Max(Scale(40), ClientSize.Width - Scale(8) - Scale(BaseSettingsDotArea));
            _input.SetBounds(Scale(4), y, width, pref);
        }

        private Rectangle SettingsDotBounds()
        {
            int size = Math.Max(6, Scale(BaseSettingsDotSize));
            int area = Scale(BaseSettingsDotArea);
            int x = ClientSize.Width - area + ((area - size) / 2);
            int y = InputTop + ((RowHeight - size) / 2);
            return new Rectangle(x, y, size, size);
        }

        private Rectangle SettingsHitBounds()
        {
            int area = Scale(BaseSettingsDotArea + 4);
            return new Rectangle(ClientSize.Width - area, InputTop, area, RowHeight);
        }

        private static string SettingsPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "QuickSearch", "settings.ini");
            }
        }

        private void LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return;

                int width = 0;
                int height = 0;
                int x = int.MinValue;
                int y = int.MinValue;
                int opacity = 100;
                int startup = -1;
                foreach (string raw in File.ReadAllLines(SettingsPath))
                {
                    string line = raw.Trim();
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;

                    string key = line.Substring(0, eq).Trim();
                    int value;
                    if (!int.TryParse(line.Substring(eq + 1).Trim(), out value)) continue;

                    if (key.Equals("width", StringComparison.OrdinalIgnoreCase)) width = value;
                    else if (key.Equals("height", StringComparison.OrdinalIgnoreCase)) height = value;
                    else if (key.Equals("x", StringComparison.OrdinalIgnoreCase)) x = value;
                    else if (key.Equals("y", StringComparison.OrdinalIgnoreCase)) y = value;
                    else if (key.Equals("opacity", StringComparison.OrdinalIgnoreCase)) opacity = value;
                    else if (key.Equals("startup", StringComparison.OrdinalIgnoreCase)) startup = value;
                }

                if (width > 0 && height > 0) _clientSize = new Size(width, height);
                if (x != int.MinValue && y != int.MinValue) _savedLocation = new Point(x, y);
                _opacityPercent = Math.Max(30, Math.Min(100, opacity));
                if (startup >= 0)
                {
                    _startupChoiceSaved = true;
                    _startupEnabled = startup != 0;
                }
            }
            catch
            {
            }
        }

        private void SaveSettings()
        {
            try
            {
                string dir = Path.GetDirectoryName(SettingsPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                Size size = ClientSize;
                if (size.Width <= 0 || size.Height <= 0) size = _clientSize;

                Point location = _hasBeenShown || !_savedLocation.HasValue ? Location : _savedLocation.Value;

                string text =
                    "width=" + size.Width + Environment.NewLine +
                    "height=" + size.Height + Environment.NewLine +
                    "x=" + location.X + Environment.NewLine +
                    "y=" + location.Y + Environment.NewLine +
                    "opacity=" + _opacityPercent + Environment.NewLine +
                    "startup=" + (_startupEnabled ? 1 : 0) + Environment.NewLine;

                File.WriteAllText(SettingsPath, text);
            }
            catch
            {
            }
        }

        private void SetOpacity(int percent)
        {
            _opacityPercent = Math.Max(30, Math.Min(100, percent));
            Opacity = _opacityPercent / 100.0;
            SaveSettings();
        }

        private void ResetWindow()
        {
            _clientSize = Size.Empty;
            _savedLocation = null;
            ApplyLayout();
            Location = RestoreLocation();
            EnsureOnScreen();
            SaveSettings();
        }

        private ResizeEdge EdgeAt(Point p)
        {
            int grip = Math.Max(8, Scale(10));
            int border = Math.Max(3, Scale(BaseResizeBorder));

            // The top strip is the drag handle. Only its far corners resize vertically.
            if (p.Y < TopPad)
            {
                if (p.X < grip) return ResizeEdge.TopLeft;
                if (p.X >= ClientSize.Width - grip) return ResizeEdge.TopRight;
                return ResizeEdge.None;
            }

            bool left = p.X < border;
            bool right = p.X >= ClientSize.Width - border;
            bool bottom = p.Y >= ClientSize.Height - border;

            if (left && bottom) return ResizeEdge.BottomLeft;
            if (right && bottom) return ResizeEdge.BottomRight;
            if (left) return ResizeEdge.Left;
            if (right) return ResizeEdge.Right;
            if (bottom) return ResizeEdge.Bottom;
            return ResizeEdge.None;
        }

        private static Cursor CursorForEdge(ResizeEdge edge)
        {
            switch (edge)
            {
                case ResizeEdge.Left:
                case ResizeEdge.Right:
                    return Cursors.SizeWE;
                case ResizeEdge.Top:
                case ResizeEdge.Bottom:
                    return Cursors.SizeNS;
                case ResizeEdge.TopLeft:
                case ResizeEdge.BottomRight:
                    return Cursors.SizeNWSE;
                case ResizeEdge.TopRight:
                case ResizeEdge.BottomLeft:
                    return Cursors.SizeNESW;
                default:
                    return Cursors.Default;
            }
        }

        private static bool HasLeft(ResizeEdge edge)
        {
            return edge == ResizeEdge.Left || edge == ResizeEdge.TopLeft || edge == ResizeEdge.BottomLeft;
        }

        private static bool HasRight(ResizeEdge edge)
        {
            return edge == ResizeEdge.Right || edge == ResizeEdge.TopRight || edge == ResizeEdge.BottomRight;
        }

        private static bool HasTop(ResizeEdge edge)
        {
            return edge == ResizeEdge.Top || edge == ResizeEdge.TopLeft || edge == ResizeEdge.TopRight;
        }

        private static bool HasBottom(ResizeEdge edge)
        {
            return edge == ResizeEdge.Bottom || edge == ResizeEdge.BottomLeft || edge == ResizeEdge.BottomRight;
        }

        private void BeginResize(ResizeEdge edge)
        {
            _resizing = true;
            _resizeEdge = edge;
            _resizeStartBounds = Bounds;
            _resizeStartCursor = Cursor.Position;
            Capture = true;
        }

        private static void Trace(string message)
        {
            try
            {
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "qs-trace.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + " " + message + Environment.NewLine);
            }
            catch
            {
            }
        }

        private void BeginMove()
        {
            Trace("BeginMove");
            _moving = true;
            _moveStartCursor = Cursor.Position;
            _moveStartLocation = Location;
            Capture = true;
            Cursor = Cursors.SizeAll;
        }

        private void UpdateMove()
        {
            if (!_moving) return;
            Point current = Cursor.Position;
            Location = new Point(
                _moveStartLocation.X + (current.X - _moveStartCursor.X),
                _moveStartLocation.Y + (current.Y - _moveStartCursor.Y));
            Trace("UpdateMove cursor=" + current + " loc=" + Location);
        }

        private void EndMove()
        {
            if (!_moving) return;
            Trace("EndMove loc=" + Location);
            _moving = false;
            Capture = false;
            Cursor = Cursors.Default;
            SaveSettings();
        }

        private void UpdateResize()
        {
            if (!_resizing) return;

            int dx = Cursor.Position.X - _resizeStartCursor.X;
            int dy = Cursor.Position.Y - _resizeStartCursor.Y;
            Rectangle bounds = _resizeStartBounds;
            int minWidth = Scale(BaseMinWidth);
            int minHeight = HeightForRows(1);

            if (HasLeft(_resizeEdge))
            {
                bounds.X += dx;
                bounds.Width -= dx;
            }
            if (HasRight(_resizeEdge)) bounds.Width += dx;
            if (HasTop(_resizeEdge))
            {
                bounds.Y += dy;
                bounds.Height -= dy;
            }
            if (HasBottom(_resizeEdge)) bounds.Height += dy;

            if (bounds.Width < minWidth)
            {
                if (HasLeft(_resizeEdge)) bounds.X = _resizeStartBounds.Right - minWidth;
                bounds.Width = minWidth;
            }

            if (bounds.Height < minHeight)
            {
                if (HasTop(_resizeEdge)) bounds.Y = _resizeStartBounds.Bottom - minHeight;
                bounds.Height = minHeight;
            }

            int rows = RowsForHeight(bounds.Height);
            int height = HeightForRows(rows);
            if (HasTop(_resizeEdge)) bounds.Y = _resizeStartBounds.Bottom - height;
            bounds.Height = height;

            Bounds = bounds;

            _clientSize = ClientSize;
            _visibleRows = rows;
            PositionInput();
            ClampSelection();
            Invalidate();
        }

        private void EndResize()
        {
            if (!_resizing) return;
            _resizing = false;
            _resizeEdge = ResizeEdge.None;
            Capture = false;
            SaveSettings();
        }

        private void ToggleStartup()
        {
            SetStartup(!StartupManager.IsInstalled());
        }

        private void TryEnableStartup()
        {
            try
            {
                StartupManager.Install();
                _startupEnabled = StartupManager.IsInstalledForCurrentExe();
            }
            catch
            {
                _startupEnabled = false;
            }
            _startupChoiceSaved = true;
            SaveSettings();
        }

        private void SetStartup(bool enabled)
        {
            try
            {
                if (enabled) StartupManager.Install();
                else StartupManager.Uninstall();

                _startupEnabled = enabled && StartupManager.IsInstalled();
                _startupChoiceSaved = true;
                _startupItem.Checked = StartupManager.IsInstalled();
                SaveSettings();
            }
            catch (Exception ex)
            {
                ShowBalloon("Could not update startup: " + ex.Message);
            }
        }

        private void ShowSettings()
        {
            if (_settingsDialog != null)
            {
                _settingsDialog.Activate();
                return;
            }

            Form dialog = new Form();
            dialog.Text = "Quick Search Settings";
            dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
            dialog.MaximizeBox = false;
            dialog.MinimizeBox = false;
            dialog.ShowInTaskbar = false;
            dialog.StartPosition = FormStartPosition.CenterScreen;
            dialog.ClientSize = new Size(340, 288);
            dialog.BackColor = Color.FromArgb(30, 30, 30);
            dialog.ForeColor = Color.White;
            dialog.TopMost = true;

            Label caption = new Label();
            caption.Text = "Search bar opacity";
            caption.ForeColor = Color.FromArgb(235, 235, 235);
            caption.SetBounds(16, 16, 190, 20);
            dialog.Controls.Add(caption);

            Label valueLabel = new Label();
            valueLabel.Text = _opacityPercent + "%";
            valueLabel.ForeColor = Color.White;
            valueLabel.TextAlign = ContentAlignment.MiddleRight;
            valueLabel.SetBounds(206, 16, 118, 20);
            dialog.Controls.Add(valueLabel);

            TrackBar slider = new TrackBar();
            slider.Minimum = 30;
            slider.Maximum = 100;
            slider.TickFrequency = 10;
            slider.SmallChange = 1;
            slider.LargeChange = 10;
            slider.Value = _opacityPercent;
            slider.SetBounds(10, 38, 320, 42);
            slider.ValueChanged += delegate
            {
                valueLabel.Text = slider.Value + "%";
                SetOpacity(slider.Value);
            };
            dialog.Controls.Add(slider);

            Label hint = new Label();
            hint.Text = "Lower values make the search bar more transparent.";
            hint.ForeColor = Color.FromArgb(165, 165, 165);
            hint.SetBounds(16, 78, 308, 18);
            dialog.Controls.Add(hint);

            CheckBox startup = new CheckBox();
            startup.Text = "Start with Windows";
            startup.ForeColor = Color.FromArgb(235, 235, 235);
            startup.Checked = StartupManager.IsInstalled();
            startup.SetBounds(16, 106, 190, 24);
            startup.CheckedChanged += delegate { SetStartup(startup.Checked); };
            dialog.Controls.Add(startup);

            Panel divider = new Panel();
            divider.BackColor = Color.FromArgb(70, 70, 70);
            divider.SetBounds(16, 142, 308, 1);
            dialog.Controls.Add(divider);

            Label aboutTitle = new Label();
            aboutTitle.Text = "About";
            aboutTitle.Font = new Font(dialog.Font, FontStyle.Bold);
            aboutTitle.ForeColor = Color.White;
            aboutTitle.SetBounds(16, 154, 100, 20);
            dialog.Controls.Add(aboutTitle);

            Label aboutText = new Label();
            aboutText.Text = "iwin is the excellent creator of this app.";
            aboutText.ForeColor = Color.FromArgb(210, 210, 210);
            aboutText.SetBounds(16, 178, 308, 42);
            dialog.Controls.Add(aboutText);

            Button close = new Button();
            close.Text = "Close";
            close.SetBounds(236, 244, 88, 28);
            close.Click += delegate { dialog.Close(); };
            dialog.Controls.Add(close);

            dialog.FormClosed += delegate
            {
                if (_settingsDialog == dialog) _settingsDialog = null;
            };

            _settingsDialog = dialog;
            dialog.Show();
            dialog.Activate();
        }

        private void ShowBalloon(string message)
        {
            _tray.BalloonTipTitle = "Quick Search";
            _tray.BalloonTipText = message;
            _tray.ShowBalloonTip(3000);
        }

        private void ShowLauncher()
        {
            EnsureIndex();
            _input.Clear();
            _selected = 0;
            _scroll = 0;
            RefreshResults(false);

            _allowShow = true;
            _hasBeenShown = true;
            Location = RestoreLocation();
            Show();
            ApplyLayout();
            EnsureOnScreen();

            TopMost = true;
            SetForegroundWindow(Handle);
            Activate();
            _input.Focus();
            Invalidate();
        }

        private Point RestoreLocation()
        {
            if (_savedLocation.HasValue && IsLocationVisible(_savedLocation.Value))
                return _savedLocation.Value;

            Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            int x = wa.Left + (wa.Width - Width) / 2;
            int y = wa.Top + (int)(wa.Height * 0.18);
            return new Point(x, y);
        }

        private bool IsLocationVisible(Point location)
        {
            Rectangle probe = new Rectangle(location, Size);
            foreach (Screen screen in Screen.AllScreens)
            {
                Rectangle overlap = Rectangle.Intersect(probe, screen.WorkingArea);
                if (overlap.Width > 40 && overlap.Height > 20) return true;
            }
            return false;
        }

        private void EnsureOnScreen()
        {
            Rectangle wa = Screen.FromRectangle(Bounds).WorkingArea;
            int x = Bounds.X;
            int y = Bounds.Y;

            if (x + Width > wa.Right) x = wa.Right - Width;
            if (y + Height > wa.Bottom) y = wa.Bottom - Height;
            if (x < wa.Left) x = wa.Left;
            if (y < wa.Top) y = wa.Top;

            if (x != Bounds.X || y != Bounds.Y) Location = new Point(x, y);
        }

        private void HideLauncher()
        {
            if (!Visible) return;
            SaveSettings();
            Hide();
            _input.Clear();
            _selected = 0;
            _scroll = 0;
        }

        private void ExitApp()
        {
            _exiting = true;
            _tray.Visible = false;
            Close();
        }

        private static Icon LoadIcon()
        {
            try
            {
                Icon icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (icon != null) return icon;
            }
            catch
            {
            }
            return SystemIcons.Application;
        }

        /* -----------------------------------------------------------------
           search index
           ----------------------------------------------------------------- */

        private void EnsureIndex()
        {
            if (_indexed) return;
            _indexed = true;

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            AddShortcuts(seen, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu));
            AddShortcuts(seen, Environment.GetFolderPath(Environment.SpecialFolder.StartMenu));
            AddShortcuts(seen, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            AddShortcuts(seen, Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));
            AddUwpApps(seen);
            AddCommands(seen);

            _all.Sort(delegate (AppEntry a, AppEntry b)
            {
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
        }

        private void AddShortcuts(HashSet<string> seen, string root)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;

            string[] files;
            try
            {
                files = Directory.GetFiles(root, "*.lnk", SearchOption.AllDirectories);
            }
            catch
            {
                return;
            }

            foreach (string file in files)
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (name.EndsWith(" - Shortcut", StringComparison.OrdinalIgnoreCase))
                    name = name.Substring(0, name.Length - " - Shortcut".Length);
                name = name.Trim();
                if (!IsUsefulName(name)) continue;

                string folder = Path.GetFileName(Path.GetDirectoryName(file));
                string keywords = string.IsNullOrEmpty(folder) ? "app" : "app " + folder;
                AddEntry(seen, new AppEntry(name, file, null, keywords));
            }
        }

        private static bool IsUsefulName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string n = name.ToLowerInvariant();
            if (n.Contains("uninstall")) return false;
            if (n.StartsWith("readme") || n.StartsWith("read me")) return false;
            if (n.StartsWith("license") || n.StartsWith("licence")) return false;
            if (n.StartsWith("release notes")) return false;
            if (n.StartsWith("documentation")) return false;
            if (n.StartsWith("website")) return false;
            if (n.StartsWith("remove ")) return false;
            return true;
        }

        private void AddUwpApps(HashSet<string> seen)
        {
            object shell = null;
            object folder = null;
            object items = null;
            try
            {
                Type shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null) return;
                shell = Activator.CreateInstance(shellType);
                folder = shellType.InvokeMember("Namespace", BindingFlags.InvokeMethod, null, shell, new object[] { "shell:AppsFolder" });
                if (folder == null) return;
                items = folder.GetType().InvokeMember("Items", BindingFlags.InvokeMethod, null, folder, null);
                if (items == null) return;

                int count = Convert.ToInt32(items.GetType().InvokeMember("Count", BindingFlags.GetProperty, null, items, null));
                for (int i = 0; i < count; i++)
                {
                    object item = null;
                    try
                    {
                        item = items.GetType().InvokeMember("Item", BindingFlags.InvokeMethod, null, items, new object[] { i });
                        if (item == null) continue;

                        string name = Convert.ToString(item.GetType().InvokeMember("Name", BindingFlags.GetProperty, null, item, null));
                        string id = Convert.ToString(item.GetType().InvokeMember("Path", BindingFlags.GetProperty, null, item, null));
                        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(id)) continue;
                        if (id.IndexOf('!') < 0) continue;
                        if (!IsUsefulName(name)) continue;

                        AddEntry(seen, new AppEntry(name, "explorer.exe", "shell:AppsFolder\\" + id, "app windows store"));
                    }
                    catch
                    {
                    }
                    finally
                    {
                        if (item != null && Marshal.IsComObject(item)) Marshal.ReleaseComObject(item);
                    }
                }
            }
            catch
            {
            }
            finally
            {
                if (items != null && Marshal.IsComObject(items)) Marshal.ReleaseComObject(items);
                if (folder != null && Marshal.IsComObject(folder)) Marshal.ReleaseComObject(folder);
                if (shell != null && Marshal.IsComObject(shell)) Marshal.ReleaseComObject(shell);
            }
        }

        private void AddCommands(HashSet<string> seen)
        {
            AddCommand(seen, "File Explorer", "explorer.exe", null, "files folders explorer");
            AddCommand(seen, "Settings", "ms-settings:", null, "windows settings control panel");
            AddCommand(seen, "Control Panel", "control.exe", null, "windows settings");
            AddCommand(seen, "Task Manager", "taskmgr.exe", null, "processes performance startup");
            AddCommand(seen, "Task Scheduler", "taskschd.msc", null, "tasks scheduled");
            AddCommand(seen, "Device Manager", "devmgmt.msc", null, "drivers hardware devices");
            AddCommand(seen, "Services", "services.msc", null, "windows services");
            AddCommand(seen, "Event Viewer", "eventvwr.msc", null, "events logs");
            AddCommand(seen, "Registry Editor", "regedit.exe", null, "registry regedit");
            AddCommand(seen, "Command Prompt", "cmd.exe", null, "terminal console shell");
            AddCommand(seen, "Windows PowerShell", "powershell.exe", null, "terminal console shell");
            AddCommand(seen, "Windows Terminal", "wt.exe", null, "terminal console shell");
            AddCommand(seen, "Notepad", "notepad.exe", null, "text editor");
            AddCommand(seen, "Paint", "mspaint.exe", null, "drawing image editor");
            AddCommand(seen, "Calculator", "calc.exe", null, "math calculator");
            AddCommand(seen, "Snipping Tool", "snippingtool.exe", null, "screenshot snip capture");
            AddCommand(seen, "System Information", "msinfo32.exe", null, "about computer specs");
            AddCommand(seen, "Resource Monitor", "resmon.exe", null, "performance cpu memory disk network");
            AddCommand(seen, "Disk Management", "diskmgmt.msc", null, "disks partitions drives");
            AddCommand(seen, "Programs and Features", "appwiz.cpl", null, "uninstall programs software");
            AddCommand(seen, "Power Options", "powercfg.cpl", null, "battery sleep power plan");
            AddCommand(seen, "Sound Settings", "mmsys.cpl", null, "audio speakers microphone");
            AddCommand(seen, "Network Connections", "ncpa.cpl", null, "network adapter internet");
            AddCommand(seen, "Windows Update", "ms-settings:windowsupdate", null, "updates windows");
            AddCommand(seen, "Display Settings", "ms-settings:display", null, "screen monitors resolution");
            AddCommand(seen, "Bluetooth Settings", "ms-settings:bluetooth", null, "devices bluetooth");
            AddCommand(seen, "Printer Settings", "ms-settings:printers", null, "printers scanners");
            AddCommand(seen, "Default Apps", "ms-settings:defaultapps", null, "default applications file types");
            AddCommand(seen, "Storage Settings", "ms-settings:storagesense", null, "disk space storage");
            AddCommand(seen, "About Your PC", "ms-settings:about", null, "system specs windows version");
            AddCommand(seen, "Recycle Bin", "shell:RecycleBinFolder", null, "deleted files trash");
            AddCommand(seen, "Downloads", "shell:Downloads", null, "folder files");
            AddCommand(seen, "Documents", "shell:Personal", null, "folder files");
            AddCommand(seen, "Pictures", "shell:MyPictures", null, "folder images");
            AddCommand(seen, "Desktop", "shell:Desktop", null, "folder files");
        }

        private void AddCommand(HashSet<string> seen, string name, string launch, string args, string keywords)
        {
            AddEntry(seen, new AppEntry(name, launch, args, "command " + keywords));
        }

        private void AddEntry(HashSet<string> seen, AppEntry entry)
        {
            if (!seen.Add(entry.NameLower)) return;
            _all.Add(entry);
        }

        /* -----------------------------------------------------------------
           results
           ----------------------------------------------------------------- */

        private void RefreshResults(bool keepSelection)
        {
            string query = _input.Text.Trim();
            string q = query.ToLowerInvariant();

            _view.Clear();
            if (q.Length == 0)
            {
                // No suggestions until the user starts typing.
            }
            else
            {
                List<KeyValuePair<int, AppEntry>> matches = new List<KeyValuePair<int, AppEntry>>();
                bool hasExactMatch = false;
                for (int i = 0; i < _all.Count; i++)
                {
                    if (string.Equals(_all[i].NameLower, q, StringComparison.Ordinal))
                    {
                        matches.Add(new KeyValuePair<int, AppEntry>(0, _all[i]));
                        hasExactMatch = true;
                    }
                }

                if (!hasExactMatch)
                {
                    for (int i = 0; i < _all.Count; i++)
                    {
                        int score = Score(_all[i], q);
                        if (score >= 0) matches.Add(new KeyValuePair<int, AppEntry>(score, _all[i]));
                    }
                }

                matches.Sort(delegate (KeyValuePair<int, AppEntry> a, KeyValuePair<int, AppEntry> b)
                {
                    int c = a.Key.CompareTo(b.Key);
                    if (c != 0) return c;
                    return string.Compare(a.Value.Name, b.Value.Name, StringComparison.OrdinalIgnoreCase);
                });

                for (int i = 0; i < matches.Count; i++) _view.Add(matches[i].Value);
            }

            if (!keepSelection)
            {
                _selected = 0;
                _scroll = 0;
            }

            ClampSelection();
            Invalidate();
        }

        private static int Score(AppEntry e, string q)
        {
            string n = e.NameLower;
            int rank = -1;

            if (n.StartsWith(q, StringComparison.Ordinal)) rank = 0;
            else if (n.IndexOf(" " + q, StringComparison.Ordinal) >= 0) rank = 1;
            else if (e.Initials.StartsWith(q, StringComparison.Ordinal)) rank = 2;
            else if (n.IndexOf(q, StringComparison.Ordinal) >= 0) rank = 3;
            else if (e.Keywords.IndexOf(q, StringComparison.Ordinal) >= 0) rank = 4;

            if (rank < 0)
            {
                string[] parts = q.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 1)
                {
                    bool all = true;
                    for (int i = 0; i < parts.Length; i++)
                    {
                        if (n.IndexOf(parts[i], StringComparison.Ordinal) < 0 &&
                            e.Keywords.IndexOf(parts[i], StringComparison.Ordinal) < 0)
                        {
                            all = false;
                            break;
                        }
                    }
                    if (all) rank = 5;
                }
            }

            if (rank < 0) return -1;
            return rank * 1000 + Math.Min(n.Length, 999);
        }

        private void ClampSelection()
        {
            if (_view.Count == 0)
            {
                _selected = 0;
                _scroll = 0;
                return;
            }

            if (_selected < 0) _selected = _view.Count - 1;
            if (_selected >= _view.Count) _selected = 0;

            int maxScroll = Math.Max(0, _view.Count - _visibleRows);
            if (_selected < _scroll) _scroll = _selected;
            if (_selected >= _scroll + _visibleRows) _scroll = _selected - _visibleRows + 1;
            if (_scroll < 0) _scroll = 0;
            if (_scroll > maxScroll) _scroll = maxScroll;
        }

        private void MoveSelection(int delta)
        {
            if (_view.Count == 0) return;
            _selected += delta;
            ClampSelection();
            Invalidate();
        }

        private void ScrollBy(int delta)
        {
            if (_view.Count <= _visibleRows) return;
            int maxScroll = Math.Max(0, _view.Count - _visibleRows);
            _scroll += delta;
            if (_scroll < 0) _scroll = 0;
            if (_scroll > maxScroll) _scroll = maxScroll;
            Invalidate();
        }

        private void LaunchSelected()
        {
            if (_view.Count == 0) return;
            AppEntry entry = _view[Math.Max(0, Math.Min(_selected, _view.Count - 1))];
            HideLauncher();

            try
            {
                ProcessStartInfo psi;
                if (string.IsNullOrEmpty(entry.Args))
                    psi = new ProcessStartInfo(entry.Launch);
                else
                    psi = new ProcessStartInfo(entry.Launch, entry.Args);
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                ShowError(entry, ex);
            }
        }

        private void ShowError(AppEntry entry, Exception ex)
        {
            _tray.BalloonTipTitle = "Quick Search";
            _tray.BalloonTipText = "Could not open " + entry.Name + ": " + ex.Message;
            _tray.ShowBalloonTip(3000);
        }

        private void InputKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Down:
                    MoveSelection(1);
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                    break;
                case Keys.Up:
                    MoveSelection(-1);
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                    break;
                case Keys.PageDown:
                    MoveSelection(_visibleRows);
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                    break;
                case Keys.PageUp:
                    MoveSelection(-_visibleRows);
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                    break;
                case Keys.Enter:
                    LaunchSelected();
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                    break;
                case Keys.Escape:
                    HideLauncher();
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                    break;
            }
        }

        /* -----------------------------------------------------------------
           painting / mouse
           ----------------------------------------------------------------- */

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Color.FromArgb(30, 30, 30));

            using (SolidBrush inputBrush = new SolidBrush(Color.FromArgb(45, 45, 45)))
            {
                g.FillRectangle(inputBrush, 0, InputTop, Width, RowHeight);
            }

            Rectangle settingsDot = SettingsDotBounds();
            using (SolidBrush dotBrush = new SolidBrush(_settingsHot ? Color.White : Color.FromArgb(150, 150, 150)))
            {
                SmoothingMode smoothing = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillEllipse(dotBrush, settingsDot);
                g.SmoothingMode = smoothing;
            }

            int top = ResultTop;
            int left = Scale(4);

            using (SolidBrush selectedBrush = new SolidBrush(Color.FromArgb(0, 120, 212)))
            {
                for (int i = 0; i < _visibleRows; i++)
                {
                    int index = _scroll + i;
                    Rectangle row = new Rectangle(0, top + (i * RowHeight), Width, RowHeight);
                    if (index < _view.Count && index == _selected)
                    {
                        g.FillRectangle(selectedBrush, row);
                    }
                }
            }

            TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left |
                                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix |
                                    TextFormatFlags.NoPadding;

            if (_view.Count == 0)
            {
                if (_input.Text.Trim().Length > 0)
                {
                    Rectangle empty = new Rectangle(left, resultTopSafe(), Width - (left * 2), RowHeight);
                    TextRenderer.DrawText(g, "No matches", Font, empty, Color.FromArgb(140, 140, 140), flags);
                }
                return;
            }

            for (int i = 0; i < _visibleRows; i++)
            {
                int index = _scroll + i;
                if (index >= _view.Count) break;

                Rectangle textRect = new Rectangle(left, top + (i * RowHeight) + Scale(1), Width - (left * 2), RowHeight);
                Color color = index == _selected ? Color.White : Color.FromArgb(235, 235, 235);
                TextRenderer.DrawText(g, _view[index].Name, Font, textRect, color, flags);
            }
        }

        private int resultTopSafe()
        {
            return ResultTop;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (_resizing)
            {
                UpdateResize();
                return;
            }

            if (_moving)
            {
                UpdateMove();
                return;
            }

            if (e.Y < TopPad)
            {
                _hoverEdge = ResizeEdge.None;
                Cursor = Cursors.SizeAll;
                return;
            }

            ResizeEdge edge = EdgeAt(e.Location);
            if (edge != _hoverEdge)
            {
                _hoverEdge = edge;
                if (edge != ResizeEdge.None)
                {
                    Cursor = CursorForEdge(edge);
                    return;
                }
            }

            if (edge != ResizeEdge.None)
            {
                Cursor = CursorForEdge(edge);
                return;
            }

            if (SettingsHitBounds().Contains(e.Location))
            {
                if (!_settingsHot)
                {
                    _settingsHot = true;
                    Invalidate();
                }
                Cursor = Cursors.Hand;
                return;
            }

            if (_settingsHot)
            {
                _settingsHot = false;
                Invalidate();
            }

            int index = RowIndexAt(e.Y);
            Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
            if (index >= 0 && index != _selected)
            {
                _selected = index;
                ClampSelection();
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (!_resizing && !_moving)
            {
                _hoverEdge = ResizeEdge.None;
                Cursor = Cursors.Default;
            }
            if (_settingsHot)
            {
                _settingsHot = false;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Trace("MouseDown at " + e.Location + " button=" + e.Button);
            if (e.Button != MouseButtons.Left) return;

            ResizeEdge edge = EdgeAt(e.Location);
            if (edge != ResizeEdge.None)
            {
                BeginResize(edge);
                return;
            }

            if (e.Y < TopPad)
            {
                BeginMove();
                return;
            }

            if (SettingsHitBounds().Contains(e.Location))
            {
                ShowSettings();
                return;
            }

            if (e.Y >= InputTop && e.Y < InputTop + RowHeight)
            {
                _input.Focus();
                return;
            }

            int index = RowIndexAt(e.Y);
            if (index >= 0)
            {
                _selected = index;
                ClampSelection();
                LaunchSelected();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;

            if (_moving)
            {
                EndMove();
                _hoverEdge = ResizeEdge.None;
                return;
            }

            if (_resizing)
            {
                EndResize();
                _hoverEdge = ResizeEdge.None;
                Cursor = Cursors.Default;
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            ScrollBy(e.Delta > 0 ? -3 : 3);
        }

        private int RowIndexAt(int y)
        {
            if (_view.Count == 0) return -1;
            int top = ResultTop;
            if (y < top) return -1;
            int index = (y - top) / RowHeight;
            if (index < 0 || index >= _visibleRows) return -1;
            index += _scroll;
            if (index >= _view.Count) return -1;
            return index;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Space))
            {
                HideLauncher();
                return true;
            }
            if (keyData == (Keys.Control | Keys.Oemcomma))
            {
                ShowSettings();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
