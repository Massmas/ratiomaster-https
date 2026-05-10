using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Xml;
using Microsoft.Win32;

namespace RatioMaster {
  public partial class MainWindow : Window {

    private readonly RMCollection<RMView> data = new RMCollection<RMView>();
    private int items = 0;
    private int allit = 0;
    private bool startthem;
    private bool stopthem;

    // Tray icon
    private System.Windows.Forms.NotifyIcon trayIcon;

    public MainWindow() {
      InitializeComponent();

      // Window icon from embedded resource
      try {
        var iconStream = System.Windows.Application.GetResourceStream(
          new Uri("Resources/Main.ico", UriKind.Relative));
        if (iconStream != null)
          Icon = System.Windows.Media.Imaging.BitmapFrame.Create(iconStream.Stream);
      } catch { }

      InitTrayIcon();
      LoadSettings();
      ApplyThemeButtons();

      // Update titlebar buttons when active tab changes
      tabControl.SelectionChanged += (s, e) => {
        if (e.Source == tabControl)
          UpdateTitlebarButtons(CurrentRM()?.IsRunning ?? false);
      };

      // Start with one tab
      lblIp.Text = "IP: " + Functions.GetIp();
      AddTab("");
    }

    // ── Titlebar action buttons ──────────────────────────────────────────────
    private void TbStart_Click(object sender, RoutedEventArgs e)   => CurrentRM()?.StartButton_Click(sender, e);
    private void TbStop_Click(object sender, RoutedEventArgs e)    => CurrentRM()?.StopButton_Click(sender, e);
    private void TbUpdate_Click(object sender, RoutedEventArgs e)  => CurrentRM()?.manualUpdateButton_Click(sender, e);
    private void TbDefault_Click(object sender, RoutedEventArgs e) => CurrentRM()?.btnDefault_Click(sender, e);

    /// <summary>Called by RMView.SetRunningState() and on tab change.</summary>
    public void UpdateTitlebarButtons(bool running) {
      tbStart.IsEnabled   = !running;
      tbStop.IsEnabled    =  running;
      tbUpdate.IsEnabled  =  running;
      tbDefault.IsEnabled = !running;
    }

    // ── Theme ──────────────────────────────────────────────────────────────
    private void ApplyThemeButtons() {
      if (App.IsDarkTheme) {
        btnDarkTheme.Foreground = System.Windows.Media.Brushes.White;
        btnLightTheme.Foreground = new System.Windows.Media.SolidColorBrush(
          System.Windows.Media.Color.FromArgb(100, 255, 255, 255));
      } else {
        btnDarkTheme.Foreground = new System.Windows.Media.SolidColorBrush(
          System.Windows.Media.Color.FromArgb(100, 255, 255, 255));
        btnLightTheme.Foreground = System.Windows.Media.Brushes.White;
      }
    }

    private void btnDarkTheme_Click(object sender, RoutedEventArgs e) {
      App.SetTheme(true);
      ApplyThemeButtons();
    }

    private void btnLightTheme_Click(object sender, RoutedEventArgs e) {
      App.SetTheme(false);
      ApplyThemeButtons();
    }

    // ── Tray ────────────────────────────────────────────────────────────────
    private void InitTrayIcon() {
      trayIcon = new System.Windows.Forms.NotifyIcon {
        Text = "RatioMaster",
        Visible = false
      };
      try {
        var asm = System.Reflection.Assembly.GetExecutingAssembly();
        // Try to load icon from resources
        trayIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(asm.Location);
      } catch { }

      var ctx = new System.Windows.Forms.ContextMenuStrip();
      var restoreItem = new System.Windows.Forms.ToolStripMenuItem("Restore");
      restoreItem.Click += (s, e2) => WinRestore();
      var exitItem = new System.Windows.Forms.ToolStripMenuItem("Exit");
      exitItem.Click += (s, e2) => ExitApp();
      ctx.Items.Add(restoreItem);
      ctx.Items.Add(new System.Windows.Forms.ToolStripSeparator());
      ctx.Items.Add(exitItem);
      trayIcon.ContextMenuStrip = ctx;
      trayIcon.DoubleClick += (s, e2) => WinRestore();
    }

    private void WinRestore() {
      Show();
      WindowState = WindowState.Normal;
      trayIcon.Visible = false;
      Activate();
    }

    // ── Window events ────────────────────────────────────────────────────────
    protected override void OnStateChanged(EventArgs e) {
      base.OnStateChanged(e);
      if (WindowState == WindowState.Minimized && menuMinimizeToTray.IsChecked) {
        Hide();
        trayIcon.Visible = true;
      }
    }

    private void Window_StateChanged(object sender, EventArgs e) {
      // handled by OnStateChanged
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e) {
      if (menuCloseToTray.IsChecked && menuMinimizeToTray.IsChecked) {
        e.Cancel = true;
        WindowState = WindowState.Minimized;
        Hide();
        trayIcon.Visible = true;
      } else {
        ExitApp();
      }
    }

    private void ExitApp() {
      SaveSettings(CurrentRM());
      foreach (TabItem tab in tabControl.Items) {
        (tab.Content as RMView)?.ExitRatioMaster();
      }
      trayIcon.Dispose();
      System.Windows.Application.Current.Shutdown();
    }

    // ── Drag & Drop ──────────────────────────────────────────────────────────
    private void Window_DragEnter(object sender, DragEventArgs e) {
      if (e.Data.GetDataPresent(DataFormats.FileDrop))
        e.Effects = DragDropEffects.All;
      else
        e.Effects = DragDropEffects.None;
    }

    private void Window_DragDrop(object sender, DragEventArgs e) {
      var files = e.Data.GetData(DataFormats.FileDrop) as string[];
      if (files == null) return;
      foreach (var fileName in files) {
        var ext = Path.GetExtension(fileName).ToLower();
        if (ext == ".torrent") {
          var result = MessageBox.Show(
            "Charger ce torrent dans un nouvel onglet ?\n" + fileName,
            "RatioMaster", MessageBoxButton.YesNo, MessageBoxImage.Question);
          if (result == MessageBoxResult.Yes) AddTab(fileName);
          else CurrentRM()?.LoadTorrentFileInfo(fileName);
        } else if (ext == ".session") {
          startthem = false;
          LoadSessionFromFile(fileName);
        }
      }
    }

    // ── Tab management ───────────────────────────────────────────────────────
    private RMView CurrentRM() {
      var tab = tabControl.SelectedItem as TabItem;
      return tab?.Content as RMView;
    }

    private void AddTab(string fileName) {
      items++;
      allit++;
      var rm = new RMView();
      data.Add(rm);

      var tabItem = new TabItem {
        Header = "RM " + allit,
        Content = rm
      };
      tabControl.Items.Add(tabItem);
      tabControl.SelectedItem = tabItem;
      lblTabs.Text = allit.ToString();

      if (!string.IsNullOrEmpty(fileName))
        rm.LoadTorrentFileInfo(fileName);
    }

    private void RemoveCurrentTab() {
      if (tabControl.Items.Count < 2) return;
      var tab = tabControl.SelectedItem as TabItem;
      if (tab == null) return;
      (tab.Content as RMView)?.StopButton_Click(null, null);
      allit--;
      tabControl.Items.Remove(tab);
      lblTabs.Text = allit.ToString();
    }

    private void RenameCurrentTab() {
      var tab = tabControl.SelectedItem as TabItem;
      if (tab == null) return;
      var dlg = new InputDialog("Rename tab", "Enter new tab name:", tab.Header?.ToString() ?? "");
      if (dlg.ShowDialog() == true)
        tab.Header = dlg.Result;
    }

    // ── Menu handlers ────────────────────────────────────────────────────────
    private void NewTab_Click(object sender, RoutedEventArgs e)       => AddTab("");
    private void RemoveTab_Click(object sender, RoutedEventArgs e)    => RemoveCurrentTab();
    private void RenameTab_Click(object sender, RoutedEventArgs e)    => RenameCurrentTab();
    private void Exit_Click(object sender, RoutedEventArgs e)         => ExitApp();

    private void MenuStart_Click(object sender, RoutedEventArgs e)    => CurrentRM()?.StartButton_Click(null, null);
    private void MenuUpdate_Click(object sender, RoutedEventArgs e)   => CurrentRM()?.manualUpdateButton_Click(null, null);
    private void MenuStop_Click(object sender, RoutedEventArgs e)     => CurrentRM()?.StopButton_Click(null, null);
    private void MenuSaveSettings_Click(object sender, RoutedEventArgs e) {
      var rm = CurrentRM();
      if (rm != null) SaveSettings(rm);
    }

    private void StartAll_Click(object sender, RoutedEventArgs e) {
      foreach (TabItem t in tabControl.Items)
        (t.Content as RMView)?.StartButton_Click(null, null);
    }
    private void UpdateAll_Click(object sender, RoutedEventArgs e) {
      foreach (TabItem t in tabControl.Items)
        (t.Content as RMView)?.manualUpdateButton_Click(null, null);
    }
    private void StopAll_Click(object sender, RoutedEventArgs e) {
      foreach (TabItem t in tabControl.Items)
        (t.Content as RMView)?.StopButton_Click(null, null);
    }

    private void SetAllUpload_Click(object sender, RoutedEventArgs e) {
      var dlg = new InputDialog("Set upload speed", "Upload speed (kB/s) for all tabs:", "100");
      if (dlg.ShowDialog() == true && int.TryParse(dlg.Result, out _)) {
        foreach (TabItem t in tabControl.Items) {
          var rm = t.Content as RMView;
          if (rm != null) rm.UpdateTextBox(rm.uploadRate, dlg.Result);
        }
      }
    }
    private void SetAllDownload_Click(object sender, RoutedEventArgs e) {
      var dlg = new InputDialog("Set download speed", "Download speed (kB/s) for all tabs:", "100");
      if (dlg.ShowDialog() == true && int.TryParse(dlg.Result, out _)) {
        foreach (TabItem t in tabControl.Items) {
          var rm = t.Content as RMView;
          if (rm != null) rm.UpdateTextBox(rm.downloadRate, dlg.Result);
        }
      }
    }

    private void Enable24h_Click(object sender, RoutedEventArgs e) {
      App._24h_format_enabled = menuEnable24h.IsChecked;
    }

    // ── Sessions ─────────────────────────────────────────────────────────────
    private void SaveSession_Click(object sender, RoutedEventArgs e) {
      stopthem = false;
      ShowSaveSessionDialog();
    }
    private void SaveStopSession_Click(object sender, RoutedEventArgs e) {
      stopthem = true;
      ShowSaveSessionDialog();
    }
    private void LoadSession_Click(object sender, RoutedEventArgs e) {
      startthem = false;
      ShowLoadSessionDialog();
    }
    private void LoadStartSession_Click(object sender, RoutedEventArgs e) {
      startthem = true;
      ShowLoadSessionDialog();
    }

    private void ShowSaveSessionDialog() {
      var dlg = new SaveFileDialog { Filter = "Session files|*.session", Title = "Save session" };
      if (dlg.ShowDialog() == true) SaveSessionToFile(dlg.FileName);
    }
    private void ShowLoadSessionDialog() {
      var dlg = new OpenFileDialog { Filter = "Session files|*.session", Title = "Load session" };
      if (dlg.ShowDialog() == true) LoadSessionFromFile(dlg.FileName);
    }

    private void SaveSessionToFile(string path) {
      var doc = new XmlDocument();
      var main = doc.CreateElement("main");
      doc.AppendChild(main);
      foreach (TabItem t in tabControl.Items) {
        var rm = t.Content as RMView;
        if (rm == null) continue;
        if (stopthem) rm.StopButton_Click(null, null);
        var child = doc.CreateElement("RatioMaster");
        main.AppendChild(child);
        AppendSessionItem(doc, child, rm, t.Header?.ToString() ?? "RM");
      }
      doc.Save(path);
    }

    private void LoadSessionFromFile(string path) {
      var doc = new XmlDocument();
      doc.Load(path);
      XmlNode root = doc.DocumentElement;
      foreach (XmlNode node in root.ChildNodes) {
        AddTab("");
        var tab = tabControl.SelectedItem as TabItem;
        var rm = tab?.Content as RMView;
        if (rm == null || tab == null) continue;
        tab.Header = node["Name"]?.InnerText ?? "RM";
        rm.torrentFile.Text = node["Address"]?.InnerText ?? "";
        if (!string.IsNullOrEmpty(rm.torrentFile.Text))
          rm.LoadTorrentFileInfo(rm.torrentFile.Text);
        rm.trackerAddress.Text      = node["Tracker"]?.InnerText ?? "";
        rm.uploadRate.Text          = node["UploadSpeed"]?.InnerText ?? "";
        rm.txtRandUpMin.Text        = node["UploadRandMin"]?.InnerText ?? "";
        rm.txtRandUpMax.Text        = node["UploadRandMax"]?.InnerText ?? "";
        rm.downloadRate.Text        = node["DownloadSpeed"]?.InnerText ?? "";
        rm.txtRandDownMin.Text      = node["DownloadRandMin"]?.InnerText ?? "";
        rm.txtRandDownMax.Text      = node["DownloadRandMax"]?.InnerText ?? "";
        rm.chkRandUP.IsChecked      = bool.TryParse(node["UploadRandom"]?.InnerText, out var ru) && ru;
        rm.chkRandDown.IsChecked    = bool.TryParse(node["DownloadRandom"]?.InnerText, out var rd) && rd;
        rm.cmbClient.SelectedItem   = node["Client"]?.InnerText;
        rm.cmbVersion.SelectedItem  = node["Version"]?.InnerText;
        rm.fileSize.Text            = node["Finished"]?.InnerText ?? "100";
        rm.cmbStopAfter.SelectedItem = node["StopType"]?.InnerText;
        rm.txtStopValue.Text        = node["StopValue"]?.InnerText ?? "0";
        rm.customPort.Text          = node["Port"]?.InnerText ?? "";
        rm.checkTCPListen.IsChecked    = bool.TryParse(node["UseTCP"]?.InnerText, out var tcp) && tcp;
        rm.checkRequestScrap.IsChecked = bool.TryParse(node["UseScrape"]?.InnerText, out var sc) && sc;
        rm.comboProxyType.SelectedItem = node["ProxyType"]?.InnerText;
        rm.textProxyUser.Text        = node["ProxyUser"]?.InnerText ?? "";
        rm.textProxyPass.Text        = node["ProxyPass"]?.InnerText ?? "";
        rm.textProxyHost.Text        = node["ProxyHost"]?.InnerText ?? "";
        rm.textProxyPort.Text        = node["ProxyPort"]?.InnerText ?? "";
        rm.checkRandomUpload.IsChecked   = bool.TryParse(node["NextUpdateUpload"]?.InnerText, out var nuu) && nuu;
        rm.checkRandomDownload.IsChecked = bool.TryParse(node["NextUpdateDownload"]?.InnerText, out var nud) && nud;
        rm.RandomUploadFrom.Text    = node["NextUpdateUploadFrom"]?.InnerText ?? "";
        rm.RandomUploadTo.Text      = node["NextUpdateUploadTo"]?.InnerText ?? "";
        rm.RandomDownloadFrom.Text  = node["NextUpdateDownloadFrom"]?.InnerText ?? "";
        rm.RandomDownloadTo.Text    = node["NextUpdateDownloadTo"]?.InnerText ?? "";
        rm.checkIgnoreFailureReason.IsChecked = bool.TryParse(node["IgnoreFailureReason"]?.InnerText, out var ig) && ig;
        if (startthem) rm.StartButton_Click(null, null);
      }
    }

    private static void AppendSessionItem(XmlDocument doc, XmlElement el, RMView rm, string name) {
      void Add(string tag, string val) {
        var n = doc.CreateElement(tag);
        n.InnerText = val ?? "";
        el.AppendChild(n);
      }
      Add("Name",                   name);
      Add("Address",                rm.currentTorrent.filename ?? rm.torrentFile.Text);
      Add("Tracker",                rm.trackerAddress.Text);
      Add("UploadSpeed",            rm.uploadRate.Text);
      Add("UploadRandom",           (rm.chkRandUP.IsChecked == true).ToString());
      Add("UploadRandMin",          rm.txtRandUpMin.Text);
      Add("UploadRandMax",          rm.txtRandUpMax.Text);
      Add("DownloadSpeed",          rm.downloadRate.Text);
      Add("DownloadRandom",         (rm.chkRandDown.IsChecked == true).ToString());
      Add("DownloadRandMin",        rm.txtRandDownMin.Text);
      Add("DownloadRandMax",        rm.txtRandDownMax.Text);
      Add("Client",                 rm.cmbClient.SelectedItem?.ToString());
      Add("Version",                rm.cmbVersion.SelectedItem?.ToString());
      Add("Finished",               rm.fileSize.Text);
      Add("StopType",               rm.cmbStopAfter.SelectedItem?.ToString());
      Add("StopValue",              rm.txtStopValue.Text);
      Add("Port",                   rm.customPort.Text);
      Add("UseTCP",                 (rm.checkTCPListen.IsChecked == true).ToString());
      Add("UseScrape",              (rm.checkRequestScrap.IsChecked == true).ToString());
      Add("ProxyType",              rm.comboProxyType.SelectedItem?.ToString());
      Add("ProxyUser",              rm.textProxyUser.Text);
      Add("ProxyPass",              rm.textProxyPass.Text);
      Add("ProxyHost",              rm.textProxyHost.Text);
      Add("ProxyPort",              rm.textProxyPort.Text);
      Add("NextUpdateUpload",       (rm.checkRandomUpload.IsChecked == true).ToString());
      Add("NextUpdateUploadFrom",   rm.RandomUploadFrom.Text);
      Add("NextUpdateUploadTo",     rm.RandomUploadTo.Text);
      Add("NextUpdateDownload",     (rm.checkRandomDownload.IsChecked == true).ToString());
      Add("NextUpdateDownloadFrom", rm.RandomDownloadFrom.Text);
      Add("NextUpdateDownloadTo",   rm.RandomDownloadTo.Text);
      Add("IgnoreFailureReason",    (rm.checkIgnoreFailureReason.IsChecked == true).ToString());
    }

    // ── Registry Settings ────────────────────────────────────────────────────
    private void LoadSettings() {
      try {
        var reg = Registry.CurrentUser.OpenSubKey("Software\\RatioMaster", false);
        if (reg == null) return;
        menuShowBalloon.IsChecked     = ItoB((int)(reg.GetValue("BallonTip", 0)));
        menuMinimizeToTray.IsChecked  = ItoB((int)(reg.GetValue("MinimizeToTray", 1)));
        menuCloseToTray.IsChecked     = ItoB((int)(reg.GetValue("CloseToTray", 1)));
        menuEnable24h.IsChecked       = ItoB((int)(reg.GetValue("Enable24h", 0)));
        App._24h_format_enabled       = menuEnable24h.IsChecked;
        var theme                     = reg.GetValue("WpfTheme")?.ToString();
        if (theme == "light") {
          App.SetTheme(false);
        }
      } catch { }
    }

    private void SaveSettings(RMView rm) {
      if (rm == null) return;
      try {
        var reg = Registry.CurrentUser.OpenSubKey("Software\\RatioMaster", true)
                  ?? Registry.CurrentUser.CreateSubKey("Software\\RatioMaster");

        reg.SetValue("BallonTip",         BtoI(menuShowBalloon.IsChecked),    RegistryValueKind.DWord);
        reg.SetValue("MinimizeToTray",    BtoI(menuMinimizeToTray.IsChecked), RegistryValueKind.DWord);
        reg.SetValue("CloseToTray",       BtoI(menuCloseToTray.IsChecked),    RegistryValueKind.DWord);
        reg.SetValue("Enable24h",         BtoI(menuEnable24h.IsChecked),      RegistryValueKind.DWord);
        reg.SetValue("WpfTheme",          App.IsDarkTheme ? "dark" : "light", RegistryValueKind.String);

        // RM settings (delegate to RMView)
        reg.SetValue("Version",       "2.0",                                          RegistryValueKind.String);
        reg.SetValue("NewValues",     BtoI(rm.chkNewValues.IsChecked),                RegistryValueKind.DWord);
        reg.SetValue("Client",        rm.cmbClient.SelectedItem?.ToString() ?? "",    RegistryValueKind.String);
        reg.SetValue("ClientVersion", rm.cmbVersion.SelectedItem?.ToString() ?? "",   RegistryValueKind.String);
        reg.SetValue("UploadRate",    rm.uploadRate.Text,                             RegistryValueKind.String);
        reg.SetValue("DownloadRate",  rm.downloadRate.Text,                           RegistryValueKind.String);
        reg.SetValue("Interval",      rm.interval.Text,                               RegistryValueKind.String);
        reg.SetValue("fileSize",      rm.fileSize.Text,                               RegistryValueKind.String);
        reg.SetValue("Directory",     rm.DefaultDirectory,                            RegistryValueKind.String);
        reg.SetValue("TCPlistener",   BtoI(rm.checkTCPListen.IsChecked),             RegistryValueKind.DWord);
        reg.SetValue("ScrapeInfo",    BtoI(rm.checkRequestScrap.IsChecked),          RegistryValueKind.DWord);
        reg.SetValue("GetRandUp",     BtoI(rm.chkRandUP.IsChecked),                  RegistryValueKind.DWord);
        reg.SetValue("GetRandDown",   BtoI(rm.chkRandDown.IsChecked),                RegistryValueKind.DWord);
        reg.SetValue("MinRandUp",     rm.txtRandUpMin.Text,                          RegistryValueKind.String);
        reg.SetValue("MaxRandUp",     rm.txtRandUpMax.Text,                          RegistryValueKind.String);
        reg.SetValue("MinRandDown",   rm.txtRandDownMin.Text,                        RegistryValueKind.String);
        reg.SetValue("MaxRandDown",   rm.txtRandDownMax.Text,                        RegistryValueKind.String);
        reg.SetValue("CustomKey",     rm.customKey.Text,                             RegistryValueKind.String);
        reg.SetValue("CustomPeerID",  rm.customPeerID.Text,                          RegistryValueKind.String);
        reg.SetValue("CustomPeers",   rm.customPeersNum.Text,                        RegistryValueKind.String);
        reg.SetValue("CustomPort",    rm.customPort.Text,                            RegistryValueKind.String);
        reg.SetValue("StopWhen",      rm.cmbStopAfter.SelectedItem?.ToString() ?? "", RegistryValueKind.String);
        reg.SetValue("StopAfter",     rm.txtStopValue.Text,                          RegistryValueKind.String);
        reg.SetValue("ProxyType",     rm.comboProxyType.SelectedItem?.ToString() ?? "", RegistryValueKind.String);
        reg.SetValue("ProxyAdress",   rm.textProxyHost.Text,                         RegistryValueKind.String);
        reg.SetValue("ProxyUser",     rm.textProxyUser.Text,                         RegistryValueKind.String);
        reg.SetValue("ProxyPass",     rm.textProxyPass.Text,                         RegistryValueKind.String);
        reg.SetValue("ProxyPort",     rm.textProxyPort.Text,                         RegistryValueKind.String);
        reg.SetValue("GetRandUpNext",   BtoI(rm.checkRandomUpload.IsChecked),        RegistryValueKind.DWord);
        reg.SetValue("GetRandDownNext", BtoI(rm.checkRandomDownload.IsChecked),      RegistryValueKind.DWord);
        reg.SetValue("MinRandUpNext",   rm.RandomUploadFrom.Text,                    RegistryValueKind.String);
        reg.SetValue("MaxRandUpNext",   rm.RandomUploadTo.Text,                      RegistryValueKind.String);
        reg.SetValue("MinRandDownNext", rm.RandomDownloadFrom.Text,                  RegistryValueKind.String);
        reg.SetValue("MaxRandDownNext", rm.RandomDownloadTo.Text,                    RegistryValueKind.String);
        reg.SetValue("IgnoreFailureReason", BtoI(rm.checkIgnoreFailureReason.IsChecked), RegistryValueKind.DWord);
      } catch { }
    }

    private static int BtoI(bool? b) => (b == true) ? 1 : 0;
    private static bool ItoB(int v)  => v != 0;
  }
}
