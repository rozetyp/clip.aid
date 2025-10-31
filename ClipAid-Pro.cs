using System;
using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Runtime.InteropServices;

// Settings: Configuration stored in ClipAid.config.json
class SettingsModel {
    public string Endpoint = "http://127.0.0.1:1234/v1/chat/completions";
    public int MaxTokens = 256;
    
    // Optimized system prompt (DirectEditor scored best for Gemma 4B)
    public string SystemPrompt = "Edit the text and return only the improved version. No explanations.";
    
    // Optimized action prompts (based on comprehensive testing with Gemma 4B)
    public string ActionImprove = "Fix all grammar, spelling, and punctuation errors. Preserve the original meaning, tone, acronyms, names, and technical terms";
    public string ActionFormal = "Make the following text more formal and professional while keeping the same meaning";
    public string ActionCasual = "Rewrite the following text in casual, conversational language";
    public string ActionShort = "Shorten this text";
    public string ActionReword = "Rewrite the following text with different wording but same meaning";
    
    // Optimized temperatures per action
    public double TempImprove = 0.3;
    public double TempFormal = 0.1;
    public double TempCasual = 0.5;
    public double TempShort = 0.3;
    public double TempReword = 0.7;
    
    public string DefaultAction = "Improve";
}

// ApiClient: Network calls to local AI server (ONLY network code)
class ApiClient {
    public static string CallApi(string endpoint, string systemPrompt, string userPrompt, string text, 
        int maxTokens, double temperature, CancellationToken token) {
        try {
            token.ThrowIfCancellationRequested();
            var js = new JavaScriptSerializer();
            string body = js.Serialize(new {
                messages = new[] {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt + ":\n\n" + text }
                },
                max_tokens = maxTokens,
                temperature = temperature,
                top_p = 0.95
            });

            var req = (HttpWebRequest)WebRequest.Create(endpoint);
            req.Method = "POST";
            req.ContentType = "application/json";
            req.Timeout = 30000;
            token.Register(req.Abort);

            using (var requestStream = req.GetRequestStream())
            using (var sw = new StreamWriter(requestStream))
                sw.Write(body);

            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream()))
            {
                var json = sr.ReadToEnd();
                var obj = js.Deserialize<System.Collections.Generic.Dictionary<string, object>>(json);
                var choices = (System.Collections.ArrayList)obj["choices"];
                var choice = (System.Collections.Generic.Dictionary<string, object>)choices[0];
                var message = (System.Collections.Generic.Dictionary<string, object>)choice["message"];
                string content = (string)message["content"];
                return CleanResponse(content);
            }
        } catch (OperationCanceledException) {
            return null;
        } catch (WebException webEx) {
            return webEx.Status == WebExceptionStatus.ConnectFailure 
                ? "⚠️ Cannot connect to AI server" 
                : "⚠️ Network error";
        } catch (Exception ex) {
            return "⚠️ Error: " + ex.Message;
        }
    }

    public static string CleanResponse(string response) {
        if (string.IsNullOrWhiteSpace(response)) return response;
        response = response.Trim();
        
        if (response.StartsWith("```") && response.EndsWith("```")) {
            response = response.Substring(3, response.Length - 6).Trim();
            if (response.Contains("\n")) {
                int firstNewline = response.IndexOf('\n');
                string firstLine = response.Substring(0, firstNewline).Trim();
                if (firstLine.Length < 20 && !firstLine.Contains(" "))
                    response = response.Substring(firstNewline + 1).Trim();
            }
        }
        
        string[] commentaryPrefixes = { "Here is", "Here's", "Okay", "Sure", "Certainly" };
        foreach (var prefix in commentaryPrefixes) {
            if (response.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && response.Contains(":")) {
                int colonIndex = response.IndexOf(':');
                if (colonIndex < 60) {
                    response = response.Substring(colonIndex + 1).Trim();
                    break;
                }
            }
        }
        
        return response;
    }
}

// ActionHelper: Maps action names to prompts and temperatures
class ActionHelper {
    public static string GetPrompt(SettingsModel cfg, string action) {
        switch (action) {
            case "Improve": return cfg.ActionImprove;
            case "Formal": return cfg.ActionFormal;
            case "Casual": return cfg.ActionCasual;
            case "Short": return cfg.ActionShort;
            case "Reword": return cfg.ActionReword;
            default: return cfg.ActionImprove;
        }
    }
    
    public static double GetTemperature(SettingsModel cfg, string action) {
        switch (action) {
            case "Improve": return cfg.TempImprove;
            case "Formal": return cfg.TempFormal;
            case "Casual": return cfg.TempCasual;
            case "Short": return cfg.TempShort;
            case "Reword": return cfg.TempReword;
            default: return 0.5;
        }
    }
}

// SettingsStore: Load/save config file (ONLY file I/O code)
class SettingsStore {
    public static string Folder { get { return Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location); } }
    public static string PathJson { get { return Path.Combine(Folder, "ClipAid.config.json"); } }
    
    static readonly JavaScriptSerializer JsonSerializer = new JavaScriptSerializer();
    
    public static SettingsModel Load() {
        try {
            if (File.Exists(PathJson)) {
                return JsonSerializer.Deserialize<SettingsModel>(File.ReadAllText(PathJson, Encoding.UTF8));
            }
        } catch { }
        return new SettingsModel();
    }
    
    public static void Save(SettingsModel s) {
        try {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(PathJson, JsonSerializer.Serialize(s), Encoding.UTF8);
        } catch (Exception ex) { 
            MessageBox.Show("Could not save settings: " + ex.Message, "ClipAid", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}

// ClipAidService: Main logic - clipboard monitoring and preemptive processing
class ClipAidService : ApplicationContext
{
    public const int WM_CLIPBOARDUPDATE = 0x031D;
    private const int MAX_CLIPBOARD_CHARS = 5000;
    private const int MOUSE_TOLERANCE = 8;
    private const int CLIPBOARD_RETRY_ATTEMPTS = 3;
    private const int CLIPBOARD_RETRY_DELAY_MS = 40;

    [DllImport("user32.dll", SetLastError = true)] private static extern bool AddClipboardFormatListener(IntPtr hWnd);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RemoveClipboardFormatListener(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point lpPoint);

    public SettingsModel cfg;
    private NotifyIcon tray;
    private bool popupIsOpen = false;
    private readonly object popupLock = new object();
    private HiddenWindow hiddenWindow;
    private System.Windows.Forms.Timer selectionTimer;
    private Point mousePosAtCopy;
    private string preemptiveText;
    private string preemptiveResult;
    private CancellationTokenSource preemptiveCts;
    private bool preemptiveProcessing = false;

    public ClipAidService()
    {
        cfg = SettingsStore.Load();
        hiddenWindow = new HiddenWindow(this);
        AddClipboardFormatListener(hiddenWindow.Handle); // Register for clipboard change notifications

        selectionTimer = new System.Windows.Forms.Timer();
        selectionTimer.Tick += SelectionTimer_Tick;

        tray = new NotifyIcon() {
            Text = "ClipAid - AI Text Assistant",
            Visible = true,
            Icon = SystemIcons.Information
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("⚙️ Settings", null, (s, e) => ShowSettings());
        menu.Items.Add("-");
        menu.Items.Add("❌ Exit", null, (s, e) => { tray.Visible = false; Application.Exit(); });
        tray.ContextMenuStrip = menu;
    }

    void ShowSettings()
    {
        using (var settingsForm = new SettingsForm(cfg))
        {
            if (settingsForm.ShowDialog() == DialogResult.OK)
            {
                cfg = settingsForm.GetSettings();
                SettingsStore.Save(cfg);
            }
        }
    }

    public void OnClipboardChanged()
    {
        selectionTimer.Stop();
        if (preemptiveCts != null) {
            preemptiveCts.Cancel();
            preemptiveCts.Dispose();
            preemptiveCts = null;
        }
        GetCursorPos(out mousePosAtCopy);
        try {
            if (Clipboard.ContainsText()) {
                string clipText = Clipboard.GetText();
                if (!string.IsNullOrWhiteSpace(clipText) && clipText.Length >= 5 && clipText.Length <= MAX_CLIPBOARD_CHARS) {
                    StartPreemptiveProcessing(clipText);
                }
            }
        } catch { }
        
        selectionTimer.Interval = 500;
        selectionTimer.Start();
    }

    private void SelectionTimer_Tick(object sender, EventArgs e)
    {
        selectionTimer.Stop();

        Point currentMousePos;
        GetCursorPos(out currentMousePos);
        // Ignore if mouse moved too much (accidental copy)
        if (Math.Abs(currentMousePos.X - mousePosAtCopy.X) > MOUSE_TOLERANCE || 
            Math.Abs(currentMousePos.Y - mousePosAtCopy.Y) > MOUSE_TOLERANCE) return;

        if (popupIsOpen) return;

        try
        {
            if (!Clipboard.ContainsText()) return;
            string clipboardText = Clipboard.GetText();
            if (string.IsNullOrWhiteSpace(clipboardText) || clipboardText.Length < 5 || clipboardText.Length > MAX_CLIPBOARD_CHARS)
                return;

            lock (popupLock)
            {
                if (popupIsOpen) return;
                popupIsOpen = true;
            }

            var popupWindow = new PopupWindow(this);
            popupWindow.FormClosed += (s, args) => {
                lock (popupLock) { popupIsOpen = false; }
            };
            popupWindow.ShowForText(clipboardText, mousePosAtCopy, preemptiveResult, preemptiveProcessing);
            preemptiveText = null;
            preemptiveResult = null;
            preemptiveProcessing = false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Error: " + ex.Message);
            lock(popupLock) { popupIsOpen = false; }
        }
    }

    public void CopyTextFromPopup(string text)
    {
        RemoveClipboardFormatListener(hiddenWindow.Handle); // Prevent infinite loop
        try
        {
            RetryAction(() => Clipboard.SetText(text), CLIPBOARD_RETRY_ATTEMPTS, CLIPBOARD_RETRY_DELAY_MS); // Handle clipboard lock contention
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to copy text to clipboard. Please try again.\\n\\nError: " + ex.Message, 
                            "ClipAid", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            AddClipboardFormatListener(hiddenWindow.Handle);
        }
    }

    void RetryAction(Action action, int attempts, int delayMs)
    {
        for (int i = 0; i < attempts; i++)
        {
            try { action(); return; }
            catch { if (i == attempts - 1) throw; Thread.Sleep(delayMs); }
        }
    }

    void StartPreemptiveProcessing(string text)
    {
        // Start AI processing in background before popup shows (~500ms speedup)
        preemptiveText = text;
        preemptiveResult = null;
        preemptiveProcessing = true;
        preemptiveCts = new CancellationTokenSource();
        var token = preemptiveCts.Token;
        
        var thread = new Thread(() => {
            try {
                string prompt = ActionHelper.GetPrompt(cfg, cfg.DefaultAction);
                double temp = ActionHelper.GetTemperature(cfg, cfg.DefaultAction);
                var result = ApiClient.CallApi(cfg.Endpoint, cfg.SystemPrompt, prompt, text, cfg.MaxTokens, temp, token);
                
                if (!token.IsCancellationRequested && result != null) {
                    preemptiveResult = result;
                }
            } catch {
                preemptiveResult = null;
            } finally {
                preemptiveProcessing = false;
            }
        });
        thread.IsBackground = true;
        thread.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (preemptiveCts != null) { preemptiveCts.Cancel(); preemptiveCts.Dispose(); }
            if(hiddenWindow != null && hiddenWindow.IsHandleCreated) RemoveClipboardFormatListener(hiddenWindow.Handle);
            if (tray != null) tray.Dispose();
            if (hiddenWindow != null) hiddenWindow.Dispose();
        }
        base.Dispose(disposing);
    }
}

// HiddenWindow: Receives Windows clipboard change notifications
class HiddenWindow : Form
{
    private ClipAidService service;
    public HiddenWindow(ClipAidService svc) { service = svc; }
    
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == ClipAidService.WM_CLIPBOARDUPDATE)
        {
            service.OnClipboardChanged();
            return;
        }
        base.WndProc(ref m);
    }
}

// PopupWindow: UI that appears when you copy text
class PopupWindow : Form
{
    [DllImport("user32.dll")] static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool ShowScrollBar(IntPtr hWnd, int wBar, bool bShow);
    
    const uint KEYEVENTF_KEYUP = 0x0002;
    const byte VK_CONTROL = 0x11;
    const byte VK_V = 0x56;
    const int CURSOR_OFFSET = 15;
    const int SCREEN_MARGIN = 20;
    const int FADE_INTERVAL_MS = 20;
    const int ANIMATION_INTERVAL_MS = 300;

    readonly Font RegularFont;
    readonly Font SmallFont;
    static readonly string[] ActionNames = new[] { "Improve", "Formal", "Casual", "Short", "Reword" };

    private Panel contentPanel;
    private TextBox txtEnhanced;
    private Button btnUse, btnTryAgain, btnTryDifferent;
    private Panel pnlActions;
    private System.Collections.Generic.List<Button> actionButtons;
    private ClipAidService parentService;
    private string currentAction;
    private IntPtr lastForegroundWindow;
    private string fullOriginalText;
    private CancellationTokenSource processingCts;
    private bool actionsExpanded = false;
    private bool showingDialog = false;
    private System.Windows.Forms.Timer animTimer;

    public PopupWindow(ClipAidService service)
    {
        parentService = service;
        currentAction = service.cfg.DefaultAction;
        this.DoubleBuffered = true;
        
        RegularFont = new Font("Segoe UI", 9.5F);
        SmallFont = new Font("Segoe UI", 8.75F);
        
        InitializeUI();
    }

    System.Drawing.Drawing2D.GraphicsPath CreateRoundedPath(int width, int height, int radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        int d = radius * 2;
        path.AddArc(0, 0, d, d, 180, 90);
        path.AddArc(width - d, 0, d, d, 270, 90);
        path.AddArc(width - d, height - d, d, d, 0, 90);
        path.AddArc(0, height - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    void RoundButton(Button b, int radius = 5)
    {
        using (var path = new System.Drawing.Drawing2D.GraphicsPath())
        {
            int d = radius * 2;
            path.AddArc(0, 0, d, d, 180, 90);
            path.AddArc(b.Width - d, 0, d, d, 270, 90);
            path.AddArc(b.Width - d, b.Height - d, d, d, 0, 90);
            path.AddArc(0, b.Height - d, d, d, 90, 90);
            path.CloseFigure();
            b.Region = new System.Drawing.Region(path);
        }
    }

    Button CreateButton(string text, int width, Point location, EventHandler clickHandler, Font font = null, bool isPrimary = false)
    {
        var btn = new Button {
            Text = text,
            Location = location,
            Size = new Size(width, 32),
            FlatStyle = FlatStyle.Flat,
            Font = font ?? RegularFont,
            BackColor = isPrimary ? Color.FromArgb(70, 150, 230) : Color.FromArgb(245, 245, 245),
            ForeColor = isPrimary ? Color.White : Color.Black,
            UseVisualStyleBackColor = false
        };
        btn.FlatAppearance.BorderSize = 0;
        btn.HandleCreated += (s, e) => RoundButton((Button)s);
        btn.Click += clickHandler;
        if (isPrimary) btn.EnabledChanged += (s, e) => btn.BackColor = btn.Enabled ? Color.FromArgb(70, 150, 230) : Color.FromArgb(180, 180, 180);
        return btn;
    }

    void InitializeUI()
    {
        Text = "ClipAid";
        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        KeyPreview = true;
        BackColor = Color.FromArgb(200, 200, 200);
        ClientSize = new Size(420, 146);
        Opacity = 0;
        Padding = new Padding(1);

        contentPanel = new Panel {
            Dock = DockStyle.Fill,
            BackColor = Color.White
        };
        Controls.Add(contentPanel);

        var dragStart = Point.Empty;
        contentPanel.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) dragStart = e.Location; };
        contentPanel.MouseMove += (s, e) => { if (e.Button == MouseButtons.Left && dragStart != Point.Empty) Location = new Point(Location.X + e.X - dragStart.X, Location.Y + e.Y - dragStart.Y); };

        ClientSizeChanged += (s, e) => {
            using (var path = CreateRoundedPath(ClientSize.Width, ClientSize.Height, 8)) {
                Region = new System.Drawing.Region(path);
            }
        };
        contentPanel.SizeChanged += (s, e) => {
            using (var path = CreateRoundedPath(contentPanel.Width, contentPanel.Height, 7)) {
                contentPanel.Region = new System.Drawing.Region(path);
            }
        };

        KeyDown += HandleKeyDown;
        Deactivate += (s, e) => BeginInvoke(new Action(() => { if (!showingDialog && !Focused && !ContainsFocus) Close(); }));
        FormClosing += (s, e) => { 
            if (processingCts != null) { processingCts.Cancel(); processingCts.Dispose(); }
            if (animTimer != null) { animTimer.Stop(); animTimer.Dispose(); }
        };

        animTimer = new System.Windows.Forms.Timer { Interval = ANIMATION_INTERVAL_MS, Tag = 0 };
        animTimer.Tick += (s, e) => {
            if (IsDisposed || txtEnhanced == null || txtEnhanced.IsDisposed) return;
            if (txtEnhanced.Text.StartsWith("⏳")) {
                int frame = (int)animTimer.Tag;
                string[] dots = { "", ".", "..", "..." };
                txtEnhanced.Text = "⏳ Processing" + dots[frame % 4];
                animTimer.Tag = frame + 1;
            }
        };

        int y = 12;
        int margin = 12;
        int gap = 12;
        int controlWidth = 420 - (margin * 2);

        txtEnhanced = new TextBox {
            Location = new Point(margin, y),
            Size = new Size(controlWidth, 40),
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Font = RegularFont,
            BorderStyle = BorderStyle.None,
            BackColor = Color.White,
            WordWrap = true,
            TabStop = false
        };
        txtEnhanced.GotFocus += (s, e) => { txtEnhanced.SelectionStart = txtEnhanced.Text.Length; };
        txtEnhanced.HandleCreated += (s, e) => ShowScrollBar(txtEnhanced.Handle, 1, false);
        txtEnhanced.TextChanged += (s, e) => AdjustToContent();
        contentPanel.Controls.Add(txtEnhanced);
        y += 40 + gap;

        int btnGap = 8;
        int btnRegenWidth = 40;
        int btnUseWidth = 100;
        int btnDiffWidth = 120;
        
        btnUse = CreateButton("Use This", btnUseWidth, new Point(margin, y), (s, e) => {
            bool pasteAlso = (ModifierKeys & Keys.Shift) != Keys.Shift;
            DoUse(pasteAlso);
        }, null, true);
        contentPanel.Controls.Add(btnUse);

        btnTryAgain = CreateButton("↻", btnRegenWidth, new Point(margin + btnUseWidth + btnGap, y), 
            (s, e) => ProcessWithPrompt(ActionHelper.GetPrompt(parentService.cfg, currentAction)));
        contentPanel.Controls.Add(btnTryAgain);

        btnTryDifferent = CreateButton("⚙ Try Different", btnDiffWidth, 
            new Point(margin + controlWidth - btnDiffWidth, y), (s, e) => ToggleActions());
        contentPanel.Controls.Add(btnTryDifferent);

        y += 32 + gap;
        pnlActions = new Panel {
            Location = new Point(margin, y),
            Size = new Size(controlWidth, 32),
            BackColor = Color.White,
            BorderStyle = BorderStyle.None,
            Visible = false
        };
        contentPanel.Controls.Add(pnlActions);

        var actions = new System.Collections.Generic.List<string>(ActionNames);
        actions.Add("✏️");
        actionButtons = new System.Collections.Generic.List<Button>(actions.Count);
        foreach (var a in actions) {
            var b = CreateActionButton(a);
            b.Tag = a;
            if (a == "✏️") {
                b.Click += (s, e) => RunCustomPrompt();
            } else {
                b.Click += (s, e) => RunAction((string)((Button)s).Tag);
            }
            pnlActions.Controls.Add(b);
            actionButtons.Add(b);
        }
        LayoutActionButtons();
    }

    Button CreateActionButton(string text)
    {
        var btn = new Button {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            Font = SmallFont,
            BackColor = Color.FromArgb(245, 245, 245),
            ForeColor = Color.Black,
            TabStop = false
        };
        btn.FlatAppearance.BorderSize = 0;
        btn.HandleCreated += (s, e) => RoundButton((Button)s);
        return btn;
    }

    void LayoutActionButtons()
    {
        if (pnlActions == null || actionButtons == null) return;
        
        int panelW = pnlActions.Width;
        int n = actionButtons.Count;
        int gap = 6;
        int totalGaps = (n - 1) * gap;
        int btnW = (panelW - totalGaps) / n;
        int btnH = 32;
        int x = 0;

        for (int i = 0; i < n; i++)
        {
            int width = (i == n - 1) ? (panelW - x) : btnW;
            actionButtons[i].Size = new Size(width, btnH);
            actionButtons[i].Location = new Point(x, 0);
            x += width + gap;
        }
    }

    void ToggleActions()
    {
        actionsExpanded = !actionsExpanded;
        pnlActions.Visible = actionsExpanded;
        AdjustToContent();
        btnTryDifferent.Text = actionsExpanded ? "▲ Collapse" : "⚙ Try Different";
    }

    void AdjustToContent()
    {
        if (txtEnhanced == null || IsDisposed) return;
        
        const int minHeight = 40;
        const int maxHeight = 120;
        const int margin = 12;
        const int gap = 12;
        const int buttonHeight = 32;
        
        using (var g = CreateGraphics())
        {
            var size = g.MeasureString(txtEnhanced.Text, txtEnhanced.Font, txtEnhanced.Width - 10);
            int contentHeight = Math.Max(minHeight, Math.Min(maxHeight, (int)size.Height + 10));
            
            txtEnhanced.Height = contentHeight;
            
            int yButtons = margin + contentHeight + gap;
            btnUse.Top = yButtons;
            btnTryAgain.Top = yButtons;
            btnTryDifferent.Top = yButtons;
            
            int totalHeight = yButtons + buttonHeight + margin;
            if (actionsExpanded) totalHeight += 32 + gap;
            
            ClientSize = new Size(420, totalHeight);
            pnlActions.Top = yButtons + buttonHeight + gap;
        }
    }

    void HandleKeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) { Close(); return; }
        if (e.KeyCode == Keys.Enter && e.Control) { DoUse(true); return; }
    }

    void RunAction(string action)
    {
        currentAction = action;
        var verbs = new System.Collections.Generic.Dictionary<string, string> {
            {"Improve", "Improving"}, {"Reword", "Rewording"}, {"Short", "Shortening"},
            {"Formal", "Making formal"}, {"Casual", "Making casual"}
        };
        Text = "ClipAid - " + (verbs.ContainsKey(action) ? verbs[action] : action);
        ProcessWithPrompt(ActionHelper.GetPrompt(parentService.cfg, action));
    }

    void RunCustomPrompt()
    {
        showingDialog = true;
        using (var dlg = new Form())
        {
            dlg.Text = "Custom Prompt";
            dlg.FormBorderStyle = FormBorderStyle.None;
            dlg.StartPosition = FormStartPosition.CenterParent;
            dlg.ClientSize = new Size(400, 140);
            dlg.Font = RegularFont;
            dlg.BackColor = Color.White;
            dlg.Load += (s, e) => {
                using (var path = CreateRoundedPath(400, 140, 8)) {
                    dlg.Region = new System.Drawing.Region(path);
                }
            };

            var lbl = new Label {
                Text = "Enter custom instruction (will be applied to selected text):",
                Location = new Point(12, 12),
                Size = new Size(376, 20),
                Font = RegularFont
            };
            dlg.Controls.Add(lbl);

            var txt = new TextBox {
                Location = new Point(12, 36),
                Size = new Size(376, 56),
                Multiline = true,
                Font = RegularFont,
                BorderStyle = BorderStyle.FixedSingle,
                ScrollBars = ScrollBars.Vertical,
                Text = "Make it more "
            };
            dlg.Controls.Add(txt);

            var btnOK = CreateButton("OK", 75, new Point(224, 100), null, null, true);
            btnOK.DialogResult = DialogResult.OK;
            dlg.Controls.Add(btnOK);
            dlg.AcceptButton = btnOK;

            var btnCancel = CreateButton("Cancel", 75, new Point(309, 100), null);
            btnCancel.DialogResult = DialogResult.Cancel;
            dlg.Controls.Add(btnCancel);
            dlg.CancelButton = btnCancel;

            var result = dlg.ShowDialog(this);
            showingDialog = false;
            
            if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(txt.Text))
            {
                Text = "ClipAid - Processing";
                ProcessWithPrompt(txt.Text.Trim());
            }
        }
    }

    void ProcessWithPrompt(string prompt)
    {
        if (processingCts != null) { processingCts.Cancel(); processingCts.Dispose(); }
        processingCts = new CancellationTokenSource();
        var token = processingCts.Token;

        txtEnhanced.Text = "⏳ Processing";
        btnUse.Enabled = false;
        animTimer.Start();

        var thread = new Thread(() => {
            string result = ApiClient.CallApi(parentService.cfg.Endpoint, parentService.cfg.SystemPrompt, 
                prompt, fullOriginalText, parentService.cfg.MaxTokens, ActionHelper.GetTemperature(parentService.cfg, currentAction), token);
            UpdateUI(result ?? "", token);
        });
        thread.IsBackground = true;
        thread.Start();
    }

    void UpdateUI(string result, CancellationToken token)
    {
        if (IsHandleCreated && !IsDisposed)
        {
            BeginInvoke((Action)(() => {
                if (!token.IsCancellationRequested && !IsDisposed)
                {
                    if (animTimer != null && animTimer.Enabled) animTimer.Stop();
                    Text = "ClipAid";
                    if (txtEnhanced != null && !txtEnhanced.IsDisposed) txtEnhanced.Text = result.Trim();
                    if (btnUse != null && !btnUse.IsDisposed) btnUse.Enabled = true;
                }
            }));
        }
    }

    public void ShowForText(string selectedText, Point cursorPos, string preemptiveResult = null, bool stillProcessing = false)
    {
        lastForegroundWindow = GetForegroundWindow();
        fullOriginalText = selectedText;
        
        if (!string.IsNullOrEmpty(preemptiveResult)) {
            txtEnhanced.Text = preemptiveResult;
            btnUse.Enabled = true;
        } else if (stillProcessing) {
            txtEnhanced.Text = "⏳ Processing";
            btnUse.Enabled = false;
            animTimer.Start();
            ProcessWithPrompt(ActionHelper.GetPrompt(parentService.cfg, currentAction));
        } else {
            txtEnhanced.Text = "⏳ Processing";
            ProcessWithPrompt(ActionHelper.GetPrompt(parentService.cfg, currentAction));
        }

        var cursorScreen = Screen.FromPoint(cursorPos);
        Location = new Point(
            Math.Max(cursorScreen.WorkingArea.Left + SCREEN_MARGIN, 
                    Math.Min(cursorPos.X + CURSOR_OFFSET, cursorScreen.WorkingArea.Right - Width - SCREEN_MARGIN)),
            Math.Max(cursorScreen.WorkingArea.Top + SCREEN_MARGIN, 
                    Math.Min(cursorPos.Y + CURSOR_OFFSET, cursorScreen.WorkingArea.Bottom - Height - SCREEN_MARGIN))
        );

        Show();
        var fadeTimer = new System.Windows.Forms.Timer { Interval = FADE_INTERVAL_MS };
        fadeTimer.Tick += (s, e) => {
            if (Opacity < 1.0) Opacity += 0.1;
            else { fadeTimer.Stop(); fadeTimer.Dispose(); }
        };
        fadeTimer.Start();
        SetForegroundWindow(Handle);
    }

    void DoUse(bool pasteAlso)
    {
        string result = txtEnhanced.Text;
        if (!string.IsNullOrEmpty(result) && !result.StartsWith("⏳") && !result.StartsWith("⚠️"))
        {
            parentService.CopyTextFromPopup(result);
            Close();
            if (pasteAlso)
            {
                Thread.Sleep(100);
                SimulatePaste();
            }
        }
    }

    void SimulatePaste()
    {
        if (lastForegroundWindow != IntPtr.Zero)
        {
            SetForegroundWindow(lastForegroundWindow);
            Thread.Sleep(50);
        }
        
        // Simulate Ctrl+V keypress
        keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
        keybd_event(VK_V, 0, 0, UIntPtr.Zero);
        Thread.Sleep(10);
        keybd_event(VK_V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (RegularFont != null) RegularFont.Dispose();
            if (SmallFont != null) SmallFont.Dispose();
        }
        base.Dispose(disposing);
    }
}

// SettingsForm: Configuration UI
class SettingsForm : Form
{
    TextBox txtEndpoint, txtSystemPrompt, txtActionImprove, txtActionFormal, txtActionCasual, txtActionShort, txtActionReword;
    NumericUpDown numMaxTokens;
    NumericUpDown numTempImprove, numTempFormal, numTempCasual, numTempShort, numTempReword;
    ComboBox cmbDefault;
    SettingsModel model;

    public SettingsForm(SettingsModel cfg)
    {
        model = new SettingsModel {
            Endpoint = cfg.Endpoint,
            SystemPrompt = cfg.SystemPrompt,
            ActionImprove = cfg.ActionImprove,
            ActionFormal = cfg.ActionFormal,
            ActionCasual = cfg.ActionCasual,
            ActionShort = cfg.ActionShort,
            ActionReword = cfg.ActionReword,
            MaxTokens = cfg.MaxTokens,
            TempImprove = cfg.TempImprove,
            TempFormal = cfg.TempFormal,
            TempCasual = cfg.TempCasual,
            TempShort = cfg.TempShort,
            TempReword = cfg.TempReword,
            DefaultAction = cfg.DefaultAction
        };

        Text = "ClipAid Settings";
        Size = new Size(550, 620);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(15) };
        int y = 15;

        txtEndpoint = AddTextBox(panel, "API Endpoint:", model.Endpoint, ref y);
        y += 10;
        
        AddLabel(panel, "Max Tokens:", ref y);
        numMaxTokens = new NumericUpDown { Location = new Point(130, y), Size = new Size(80, 23), Minimum = 50, Maximum = 2000, Value = model.MaxTokens };
        panel.Controls.Add(numMaxTokens);
        y += 30;

        AddLabel(panel, "Default Action:", ref y);
        cmbDefault = new ComboBox { Location = new Point(130, y), Size = new Size(120, 23), DropDownStyle = ComboBoxStyle.DropDownList };
        cmbDefault.Items.AddRange(new object[] { "Improve", "Formal", "Casual", "Short", "Reword" });
        cmbDefault.SelectedItem = model.DefaultAction;
        panel.Controls.Add(cmbDefault);
        y += 40;

        panel.Controls.Add(new Label { Text = "Temperatures by Action:", Location = new Point(15, y), Size = new Size(510, 20), Font = new Font(panel.Font, FontStyle.Bold) });
        y += 25;
        numTempImprove = AddTempControl(panel, "  Improve:", model.TempImprove, ref y);
        numTempFormal = AddTempControl(panel, "  Formal:", model.TempFormal, ref y);
        numTempCasual = AddTempControl(panel, "  Casual:", model.TempCasual, ref y);
        numTempShort = AddTempControl(panel, "  Short:", model.TempShort, ref y);
        numTempReword = AddTempControl(panel, "  Reword:", model.TempReword, ref y);
        y += 20;

        txtActionImprove = AddPrompt(panel, "Improve:", model.ActionImprove, ref y);
        txtActionFormal = AddPrompt(panel, "Formal:", model.ActionFormal, ref y);
        txtActionCasual = AddPrompt(panel, "Casual:", model.ActionCasual, ref y);
        txtActionShort = AddPrompt(panel, "Short:", model.ActionShort, ref y);
        txtActionReword = AddPrompt(panel, "Reword:", model.ActionReword, ref y);
        txtSystemPrompt = AddPrompt(panel, "System Prompt:", model.SystemPrompt, ref y);

        y += 10;
        var btnOK = new Button { Text = "OK", Location = new Point(340, y), Size = new Size(80, 32), DialogResult = DialogResult.OK };
        var btnCancel = new Button { Text = "Cancel", Location = new Point(430, y), Size = new Size(80, 32), DialogResult = DialogResult.Cancel };
        
        btnOK.Click += (s, e) => {
            model.Endpoint = txtEndpoint.Text.Trim();
            model.SystemPrompt = txtSystemPrompt.Text.Trim();
            model.ActionImprove = txtActionImprove.Text.Trim();
            model.ActionFormal = txtActionFormal.Text.Trim();
            model.ActionCasual = txtActionCasual.Text.Trim();
            model.ActionShort = txtActionShort.Text.Trim();
            model.ActionReword = txtActionReword.Text.Trim();
            model.MaxTokens = (int)numMaxTokens.Value;
            model.TempImprove = (double)numTempImprove.Value;
            model.TempFormal = (double)numTempFormal.Value;
            model.TempCasual = (double)numTempCasual.Value;
            model.TempShort = (double)numTempShort.Value;
            model.TempReword = (double)numTempReword.Value;
            model.DefaultAction = cmbDefault.SelectedItem != null ? cmbDefault.SelectedItem.ToString() : "Improve";
        };

        panel.Controls.AddRange(new Control[] { btnOK, btnCancel });
        Controls.Add(panel);
        AcceptButton = btnOK;
        CancelButton = btnCancel;
    }

    NumericUpDown AddTempControl(Panel panel, string label, double value, ref int y)
    {
        panel.Controls.Add(new Label { Text = label, Location = new Point(30, y), Size = new Size(80, 20) });
        var nud = new NumericUpDown { 
            Location = new Point(115, y), 
            Size = new Size(60, 20), 
            Minimum = 0.1m, 
            Maximum = 0.9m, 
            DecimalPlaces = 1, 
            Increment = 0.1m, 
            Value = (decimal)value 
        };
        panel.Controls.Add(nud);
        y += 25;
        return nud;
    }

    TextBox AddPrompt(Panel panel, string label, string text, ref int y)
    {
        panel.Controls.Add(new Label { Text = label, Location = new Point(15, y), Size = new Size(510, 20) });
        y += 25;
        var textBox = new TextBox { Location = new Point(15, y), Size = new Size(510, 80), Multiline = true, ScrollBars = ScrollBars.Vertical, Text = text };
        panel.Controls.Add(textBox);
        y += 90;
        return textBox;
    }

    TextBox AddTextBox(Panel panel, string label, string text, ref int y)
    {
        AddLabel(panel, label, ref y);
        var textBox = new TextBox { Location = new Point(130, y), Size = new Size(390, 23), Text = text };
        panel.Controls.Add(textBox);
        y += 30;
        return textBox;
    }

    void AddLabel(Panel panel, string text, ref int y)
    {
        panel.Controls.Add(new Label { Text = text, Location = new Point(15, y), Size = new Size(110, 23) });
    }

    public SettingsModel GetSettings() { return model; }
}

// Program: Entry point
class Program
{
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new ClipAidService());
    }
}