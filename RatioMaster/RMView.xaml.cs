using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using RatioMaster.BitTorrent;
using RatioMaster.BytesRoads;

namespace RatioMaster {

  public partial class RMView : UserControl {

    #region Variables

    private bool getnew = true;
    private readonly Random rand = new Random((int)DateTime.Now.Ticks);
    private long _nextAnnounceUploadRate  = -1;
    private long _nextAnnounceDownloadRate = -1;
    private int remWork;
    internal string DefaultDirectory = "";
    private const string DefaultClient        = "uTorrent";
    private const string DefaultClientVersion = "3.3.2";

    private TorrentClient currentClient;
    private ProxyInfo currentProxy;
    internal TorrentInfo currentTorrent;
    internal Torrent currentTorrentFile = new Torrent();
    internal TcpListener localListen;
    private bool seedMode;
    private bool updateProcessStarted;
    internal bool IsRunning => updateProcessStarted;
    private bool requestScrap;
    private bool scrapStatsUpdated;
    private int temporaryIntervalCounter;
    private bool IsExit;
    private readonly string version = "";

    // Timers (WPF DispatcherTimer replaces WinForms Timer)
    private DispatcherTimer serverUpdateTimer;
    private DispatcherTimer RemaningWork;

    #endregion

    public RMView() {
      InitializeComponent();

      // Init timers
      serverUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
      serverUpdateTimer.Tick += serverUpdateTimer_Tick;

      RemaningWork = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
      RemaningWork.Tick += RemaningWork_Tick;

      deployDefaultValues();
      ReadSettings();

      // Default combo selections
      if (cmbClient.SelectedItem == null)
        cmbClient.SelectedIndex = 0;  // uTorrent
      if (cmbStopAfter.SelectedItem == null)
        cmbStopAfter.SelectedIndex = 0;  // Never
      if (comboProxyType.SelectedItem == null)
        comboProxyType.SelectedIndex = 0;  // None
    }

    // ── Log ─────────────────────────────────────────────────────────────────

    private static SolidColorBrush GetLogBrush(string line) {
      var lower = line.ToLowerInvariant();
      if (lower.Contains("error") || lower.Contains("exception") ||
          lower.Contains("failed") || lower.Contains("stopped because"))
        return new SolidColorBrush(Color.FromRgb(244, 71, 71));
      if (lower.Contains("started") || lower.Contains("success") ||
          lower.Contains("complete") || lower.Contains("finished"))
        return new SolidColorBrush(Color.FromRgb(78, 201, 160));
      if (lower.Contains("tracker") || lower.Contains("peers") ||
          lower.Contains("scrape") || lower.Contains("https") || lower.Contains("connecting"))
        return new SolidColorBrush(Color.FromRgb(220, 220, 170));
      if (lower.Contains("torrent info") || lower.Contains("client emulation") ||
          lower.Contains("upload") || lower.Contains("download") ||
          lower.Contains("interval") || lower.Contains("ratio"))
        return new SolidColorBrush(Color.FromRgb(156, 220, 254));
      return new SolidColorBrush(Color.FromRgb(212, 212, 212));
    }

    internal void AddLog(string logLine) {
      if (!Dispatcher.CheckAccess()) {
        Dispatcher.Invoke(() => AddLog(logLine));
        return;
      }
      if (IsExit) return;
      try {
        // Append to last paragraph or create new one
        var para = logWindow.Document.Blocks.LastBlock as Paragraph;
        if (para == null) {
          para = new Paragraph { Margin = new Thickness(0), LineHeight = 15 };
          logWindow.Document.Blocks.Add(para);
        }
        para.Inlines.Add(new Run(logLine));
        logWindow.ScrollToEnd();
      } catch { }
    }

    internal void AddLogLine(string logLine) {
      if (!Dispatcher.CheckAccess()) {
        Dispatcher.Invoke(() => AddLogLine(logLine));
        return;
      }
      if (IsExit) return;
      try {
        var dtNow = DateTime.Now;
        string ts = App._24h_format_enabled
          ? "[" + dtNow.ToString("HH:mm:ss") + "] "
          : "[" + dtNow.ToString("hh:mm:ss") + "] ";
        var brush = GetLogBrush(logLine);
        var para = new Paragraph(new Run(ts + logLine)) {
          Foreground    = brush,
          Margin        = new Thickness(0),
          LineHeight    = 15,
          FontFamily    = new FontFamily("Consolas"),
          FontSize      = 11.5
        };
        logWindow.Document.Blocks.Add(para);
        logWindow.ScrollToEnd();
      } catch { }
    }

    internal void AddClientInfo() {
      AddLogLine("CLIENT EMULATION INFO:");
      AddLogLine("Name: "         + currentClient.Name);
      AddLogLine("HttpProtocol: " + currentClient.HttpProtocol);
      AddLogLine("HashUpperCase: "+ currentClient.HashUpperCase);
      AddLogLine("Key: "          + currentClient.Key);
      AddLogLine("Headers:......");
      AddLog(currentClient.Headers);
      AddLogLine("PeerID: "       + currentClient.PeerID);
      AddLogLine("Query: "        + currentClient.Query + "\n\n");
    }

    // ── Drag & Drop ──────────────────────────────────────────────────────────

    internal void Form1_DragDrop(object sender, DragEventArgs e) {
      var s = e.Data.GetData(DataFormats.FileDrop) as string[];
      if (s == null) return;
      LoadTorrentFileInfo(s[0]);
    }

    internal void Form1_DragEnter(object sender, DragEventArgs e) {
      e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
        ? DragDropEffects.All
        : DragDropEffects.None;
    }

    private void UserControl_Drop(object sender, DragEventArgs e)     => Form1_DragDrop(sender, e);
    private void UserControl_DragEnter(object sender, DragEventArgs e) => Form1_DragEnter(sender, e);

    // ── Exit ────────────────────────────────────────────────────────────────

    internal void ExitRatioMaster() {
      IsExit = true;
      if (updateProcessStarted) StopButton_Click(null, null);
    }

    // ── Default values ───────────────────────────────────────────────────────

    internal void deployDefaultValues() {
      var torrent = new TorrentInfo(0, 0);
      trackerAddress.Text = torrent.tracker;
      shaHash.Text        = torrent.hash;
      uploadRate.Text     = (torrent.uploadRate / 1024).ToString();
      downloadRate.Text   = (torrent.downloadRate / 1024).ToString();
      interval.Text       = torrent.interval.ToString();
    }

    // ── TCP Listener ─────────────────────────────────────────────────────────

    private void OpenTcpListener() {
      try {
        if (checkTCPListen.IsChecked == true && localListen == null &&
            currentProxy.ProxyType == ProxyType.None) {
          localListen = new TcpListener(IPAddress.Any, int.Parse(currentTorrent.port));
          try {
            localListen.Start();
            AddLogLine("Started TCP listener on port " + currentTorrent.port);
          } catch {
            AddLogLine("TCP listener is already started from other torrent or from your torrent client");
            return;
          }
          var t = new Thread(AcceptTcpConnection) { Name = "AcceptTcpConnection() Thread" };
          t.Start();
        }
      } catch (Exception ex) {
        AddLogLine("Error in OpenTcpListener(): " + ex.Message);
        localListen?.Stop();
        localListen = null;
        return;
      }
      AddLogLine("OpenTcpListener() successfully finished!");
    }

    private void AcceptTcpConnection() {
      Socket socket1 = null;
      try {
        var enc = Encoding.GetEncoding(0x6faf);
        while (true) {
          socket1 = localListen.AcceptSocket();
          var buf = new byte[0x43];
          if (socket1.Connected) {
            var stream = new NetworkStream(socket1);
            stream.ReadTimeout = 0x3e8;
            try { stream.Read(buf, 0, buf.Length); } catch { }
            var text = enc.GetString(buf, 0, buf.Length);
            if (text.IndexOf("BitTorrent protocol") >= 0 &&
                text.IndexOf(enc.GetString(currentTorrentFile.InfoHash)) >= 0) {
              var resp = CreateHandshakeResponse();
              stream.Write(resp, 0, resp.Length);
            }
            socket1.Close(); stream.Close(); stream.Dispose();
          }
        }
      } catch (Exception ex) {
        AddLogLine("Error in AcceptTcpConnection(): " + ex.Message);
      } finally {
        socket1?.Close();
        AddLogLine("Closed socket");
        CloseTcpListener();
      }
    }

    private byte[] CreateHandshakeResponse() {
      var i = 0;
      var enc = Encoding.GetEncoding(0x6faf);
      var protocol = "BitTorrent protocol";
      var buf = new byte[0x100];
      buf[i++] = (byte)protocol.Length;
      enc.GetBytes(protocol, 0, protocol.Length, buf, i);
      i += protocol.Length;
      for (int k = 0; k < 8; k++) buf[i++] = 0;
      Buffer.BlockCopy(currentTorrentFile.InfoHash, 0, buf, i, currentTorrentFile.InfoHash.Length);
      i += currentTorrentFile.InfoHash.Length;
      enc.GetBytes(currentTorrent.peerID.ToCharArray(), 0, currentTorrent.peerID.Length, buf, i);
      return buf;
    }

    internal void CloseTcpListener() {
      if (localListen != null) {
        localListen.Stop();
        localListen = null;
        AddLogLine("TCP Listener closed");
      }
    }

    // ── Client selection ─────────────────────────────────────────────────────

    internal string GetClientName() {
      return (cmbClient.SelectedItem as ComboBoxItem)?.Content?.ToString()
             + " "
             + (cmbVersion.SelectedItem as ComboBoxItem)?.Content?.ToString();
    }

    // Helper to get selected text from ComboBox (handles both ComboBoxItem and string)
    private string GetComboText(ComboBox combo) {
      if (combo.SelectedItem is ComboBoxItem ci) return ci.Content?.ToString() ?? "";
      return combo.SelectedItem?.ToString() ?? "";
    }

    internal void cmbClient_SelectedIndexChanged(object sender, SelectionChangedEventArgs e) {
      if (!IsLoaded) return;  // skip during XAML init
      cmbVersion.Items.Clear();
      var client = GetComboText(cmbClient);
      switch (client) {
        case "BitComet":
          AddVersions(cmbVersion, "1.20","1.03","0.98","0.96","0.93","0.92"); cmbVersion.SelectedIndex = 0;
          if (IsZero(customPeersNum.Text)) customPeersNum.Text = "200"; break;
        case "Vuze":
          AddVersions(cmbVersion, "4.2.0.8"); cmbVersion.SelectedIndex = 0;
          if (IsZero(customPeersNum.Text)) customPeersNum.Text = "50"; break;
        case "Azureus":
          AddVersions(cmbVersion, "3.1.1.0","3.0.5.0","3.0.4.2","3.0.3.4","3.0.2.2","2.5.0.4"); cmbVersion.SelectedIndex = 0;
          if (IsZero(customPeersNum.Text)) customPeersNum.Text = "50"; break;
        case "uTorrent":
          AddVersions(cmbVersion, "3.3.2","3.3.0","3.2.0","2.0.1 (build 19078)","1.8.5 (build 17414)","1.8.2","1.8.1-beta(11903)","1.8.0","1.7.7","1.7.6","1.7.5","1.6.1","1.6");
          cmbVersion.SelectedIndex = 0;
          if (IsZero(customPeersNum.Text)) customPeersNum.Text = "200"; break;
        case "BitTorrent":
          AddVersions(cmbVersion, "6.0.3 (8642)"); cmbVersion.SelectedIndex = 0;
          if (IsZero(customPeersNum.Text)) customPeersNum.Text = "200"; break;
        case "Transmission":
          AddVersions(cmbVersion, "2.82 (14160)","2.92 (14714)"); cmbVersion.SelectedIndex = 1;
          if (IsZero(customPeersNum.Text)) customPeersNum.Text = "200"; break;
        case "BitLord":
          AddVersions(cmbVersion, "1.1"); cmbVersion.SelectedIndex = 0;
          if (IsZero(customPeersNum.Text)) customPeersNum.Text = "200"; break;
        case "Deluge":
          AddVersions(cmbVersion, "1.2.0","0.5.8.7","0.5.8.6"); cmbVersion.SelectedIndex = 0;
          if (IsZero(customPeersNum.Text)) customPeersNum.Text = "200"; break;
        case "KTorrent":
          AddVersions(cmbVersion, "2.2.1"); cmbVersion.SelectedIndex = 0;
          if (IsZero(customPeersNum.Text)) customPeersNum.Text = "100"; break;
        case "BitSpirit":
          AddVersions(cmbVersion, "3.6.0.200","3.1.0.077"); cmbVersion.SelectedIndex = 0;
          if (IsZero(customPeersNum.Text)) customPeersNum.Text = "200"; break;
        case "BitTyrant":
          AddVersions(cmbVersion, "1.1"); cmbVersion.SelectedIndex = 0;
          if (IsZero(customPeersNum.Text)) customPeersNum.Text = "50"; break;
        case "Burst":
          AddVersions(cmbVersion, "3.1.0b"); cmbVersion.SelectedIndex = 0;
          if (IsZero(customPeersNum.Text)) customPeersNum.Text = "200"; break;
        case "BitTornado":
          AddVersions(cmbVersion, "0.3.17"); cmbVersion.SelectedIndex = 0;
          if (IsZero(customPeersNum.Text)) customPeersNum.Text = "200"; break;
        case "BTuga":
          AddVersions(cmbVersion, "2.1.8"); cmbVersion.SelectedIndex = 0;
          if (IsZero(customPeersNum.Text)) customPeersNum.Text = "200"; break;
        case "ABC":
          AddVersions(cmbVersion, "3.1"); cmbVersion.SelectedIndex = 0;
          if (IsZero(customPeersNum.Text)) customPeersNum.Text = "200"; break;
        case "Gnome BT":
          AddVersions(cmbVersion, "0.0.28-1"); cmbVersion.SelectedIndex = 0;
          if (IsZero(customPeersNum.Text)) customPeersNum.Text = "200"; break;
        default:
          if (IsZero(customPeersNum.Text)) customPeersNum.Text = "200"; break;
      }
    }

    private static void AddVersions(ComboBox combo, params string[] versions) {
      foreach (var v in versions) combo.Items.Add(v);
    }
    private static bool IsZero(string s) => s == "0" || string.IsNullOrEmpty(s);

    internal void cmbVersion_SelectedValueChanged(object sender, SelectionChangedEventArgs e) {
      if (!IsLoaded) return;  // skip during XAML init
      if (!getnew) { getnew = true; return; }
      if (chkNewValues.IsChecked == true) SetCustomValues();
    }

    // ── Open torrent ─────────────────────────────────────────────────────────

    internal void LoadTorrentFileInfo(string path) {
      try {
        currentTorrentFile   = new Torrent(path);
        torrentFile.Text     = path;
        trackerAddress.Text  = currentTorrentFile.Announce;
        shaHash.Text         = ToHexString(currentTorrentFile.InfoHash);
        txtTorrentSize.Text  = FormatFileSize(currentTorrentFile.totalLength);
      } catch (Exception ex) {
        AddLogLine(ex.ToString());
      }
    }

    private TorrentInfo GetCurrentTorrent() {
      var torrent = new TorrentInfo(0, 0);
      try {
        torrent.trackerUri = new Uri(trackerAddress.Text);
      } catch (Exception ex) {
        AddLogLine(ex.Message);
        return torrent;
      }
      torrent.tracker  = trackerAddress.Text;
      torrent.hash     = shaHash.Text;
      torrent.uploadRate   = _nextAnnounceUploadRate >= 0
        ? _nextAnnounceUploadRate
        : (long)(uploadRate.Text.ParseValidFloat(50) * 1024);
      _nextAnnounceUploadRate = -1;
      torrent.downloadRate = _nextAnnounceDownloadRate >= 0
        ? _nextAnnounceDownloadRate
        : (long)(downloadRate.Text.ParseValidFloat(10) * 1024);
      _nextAnnounceDownloadRate = -1;

      torrent.interval = interval.Text.ParseValidInt(torrent.interval);
      interval.Text    = torrent.interval.ToString();

      var finishedPercent = fileSize.Text.ParseDouble(0);
      if (finishedPercent < 0 || finishedPercent > 100) {
        AddLogLine("Finished value is invalid: " + fileSize.Text + ", assuming 0");
        finishedPercent = 0;
      }
      if (finishedPercent >= 100) { seedMode = true; finishedPercent = 100; }
      fileSize.Text = finishedPercent.ToString();

      if (finishedPercent == 0)        torrent.totalsize = (long)currentTorrentFile.totalLength;
      else if (finishedPercent == 100) torrent.totalsize = 0;
      else torrent.totalsize = (long)(currentTorrentFile.totalLength * (100 - finishedPercent) / 100);
      torrent.left = torrent.totalsize;
      torrent.filename = torrentFile.Text;

      torrent.port           = customPort.Text.GetValueDefault(torrent.port);
      customPort.Text        = torrent.port;
      torrent.key            = customKey.Text.GetValueDefault(currentClient.Key);
      torrent.numberOfPeers  = customPeersNum.Text.GetValueDefault(torrent.numberOfPeers);
      currentClient.Key      = customKey.Text.GetValueDefault(currentClient.Key);
      torrent.peerID         = customPeerID.Text.GetValueDefault(currentClient.PeerID);
      currentClient.PeerID   = customPeerID.Text.GetValueDefault(currentClient.PeerID);

      AddLogLine("TORRENT INFO:");
      AddLogLine("Torrent name: "    + currentTorrentFile.Name);
      AddLogLine("Tracker address: " + torrent.tracker);
      AddLogLine("Hash code: "       + torrent.hash);
      AddLogLine("Upload rate: "     + torrent.uploadRate / 1024);
      AddLogLine("Download rate: "   + torrent.downloadRate / 1024);
      AddLogLine("Update interval: " + torrent.interval);
      AddLogLine("Size: "            + (long)currentTorrentFile.totalLength / 1024);
      AddLogLine("Left: "            + torrent.totalsize / 1024);
      AddLogLine("Finished: "        + finishedPercent);
      AddLogLine("Filename: "        + torrent.filename);
      AddLogLine("Number of peers: " + torrent.numberOfPeers);
      AddLogLine("Port: "            + torrent.port);
      AddLogLine("Key: "             + torrent.key);
      AddLogLine("PeerID: "          + torrent.peerID + "\n\n");
      return torrent;
    }

    // ── Browse button ────────────────────────────────────────────────────────

    internal void browseButton_Click(object sender, RoutedEventArgs e) {
      var dlg = new OpenFileDialog {
        InitialDirectory = DefaultDirectory,
        Filter = "Torrent files|*.torrent|All files|*.*",
        Title  = "Open torrent file"
      };
      if (dlg.ShowDialog() == true) {
        LoadTorrentFileInfo(dlg.FileName);
        DefaultDirectory = Path.GetDirectoryName(dlg.FileName) ?? DefaultDirectory;
      }
    }

    // ── Buttons ──────────────────────────────────────────────────────────────

    internal void StartButton_Click(object sender, RoutedEventArgs e) {
      if (!StartButton.IsEnabled) return;
      Seeders = -1; Leechers = -1;
      if (string.IsNullOrEmpty(trackerAddress.Text) ||
          string.IsNullOrEmpty(shaHash.Text) ||
          string.IsNullOrEmpty(txtTorrentSize.Text)) {
        MessageBox.Show("Please select a valid torrent file!", "RatioMaster " + version + " – ERROR");
        return;
      }
      if (GetComboText(cmbStopAfter) == "After time:") {
        if (!int.TryParse(txtStopValue.Text, out var res) || (res < 1000 && res != 0)) {
          MessageBox.Show(
            "Please enter a valid number for Remaining Work\n- 0 = never stop\n- positive number > 1000",
            "RatioMaster " + version + " – ERROR");
          return;
        }
      }
      UpdateScrapStats("", "", "");
      TotalRunningTimeCounter = 0;
      timerValue.Text = "updating...";
      updateProcessStarted = true;
      seedMode = false;
      requestScrap = checkRequestScrap.IsChecked == true;

      SetRunningState(true);

      currentClient  = TorrentClientFactory.GetClient(GetClientNameStr());
      currentTorrent = GetCurrentTorrent();
      currentProxy   = GetCurrentProxy();
      AddClientInfo();
      OpenTcpListener();

      var t = new Thread(StartProcess) { Name = "startProcess() Thread" };
      t.Start();
      serverUpdateTimer.Start();
      remWork = 0;
      if (GetComboText(cmbStopAfter) == "After time:") RemaningWork.Start();
      RequestScrapeFromTracker(currentTorrent);
    }

    private string GetClientNameStr() {
      return GetComboText(cmbClient) + " " + GetComboText(cmbVersion);
    }

    private void SetRunningState(bool running) {
      StartButton.IsEnabled        = !running;
      StopButton.IsEnabled         =  running;
      manualUpdateButton.IsEnabled =  running;
      btnDefault.IsEnabled         = !running;
      interval.IsReadOnly          =  running;
      fileSize.IsReadOnly          =  running;
      cmbClient.IsEnabled          = !running;
      cmbVersion.IsEnabled         = !running;
      trackerAddress.IsReadOnly    =  running;
      browseButton.IsEnabled       = !running;
      txtStopValue.IsEnabled       = !running;
      cmbStopAfter.IsEnabled       = !running;
      customPeersNum.IsEnabled     = !running;
      customPort.IsEnabled         = !running;
      // Sync titlebar buttons on the parent MainWindow
      if (Window.GetWindow(this) is MainWindow mw)
        mw.UpdateTitlebarButtons(running);
    }

    private void StopTimerAndCounters() {
      if (!Dispatcher.CheckAccess()) {
        Dispatcher.Invoke(StopTimerAndCounters);
        return;
      }
      Seeders = -1; Leechers = -1;
      TotalRunningTimeCounter = 0;
      lblTotalTime.Text = "00:00";
      if (StartButton.IsEnabled) return;
      SetRunningState(false);
      serverUpdateTimer.Stop();
      CloseTcpListener();
      temporaryIntervalCounter = 0;
      timerValue.Text = "stopped";
      currentTorrent.numberOfPeers = "0";
      updateProcessStarted = false;
      RemaningWork.Stop();
      remWork = 0;
    }

    internal void StopButton_Click(object sender, RoutedEventArgs e) {
      if (!StopButton.IsEnabled) return;
      StopTimerAndCounters();
      var t = new Thread(StopProcess) { Name = "stopProcess() Thread" };
      t.Start();
    }

    internal void manualUpdateButton_Click(object sender, RoutedEventArgs e) {
      if (!manualUpdateButton.IsEnabled) return;
      if (updateProcessStarted) {
        OpenTcpListener();
        temporaryIntervalCounter = currentTorrent.interval;
      }
    }

    internal void btnDefault_Click(object sender, RoutedEventArgs e) {
      getnew = false;
      // Find and select uTorrent
      for (int i = 0; i < cmbClient.Items.Count; i++) {
        var txt = (cmbClient.Items[i] as ComboBoxItem)?.Content?.ToString()
                  ?? cmbClient.Items[i]?.ToString();
        if (txt == DefaultClient) { cmbClient.SelectedIndex = i; break; }
      }
      chkNewValues.IsChecked = true;
      SetCustomValues();
      customPort.Text = "";
      customPeersNum.Text = "";

      comboProxyType.SelectedIndex = 0;
      textProxyHost.Text = textProxyPass.Text = textProxyPort.Text = textProxyUser.Text = "";

      checkRequestScrap.IsChecked = true;
      checkTCPListen.IsChecked    = true;

      var t = new TorrentInfo(0, 0);
      uploadRate.Text   = (t.uploadRate / 1024).ToString();
      downloadRate.Text = (t.downloadRate / 1024).ToString();
      fileSize.Text     = "100";
      interval.Text     = t.interval.ToString();

      chkRandUP.IsChecked   = true;
      chkRandDown.IsChecked = true;
      txtRandUpMin.Text   = "1";  txtRandUpMax.Text   = "10";
      txtRandDownMin.Text = "1";  txtRandDownMax.Text = "10";

      checkRandomDownload.IsChecked = false;
      checkRandomUpload.IsChecked   = false;
      RandomUploadFrom.Text   = "10";  RandomUploadTo.Text   = "50";
      RandomDownloadFrom.Text = "10";  RandomDownloadTo.Text = "100";

      txtStopValue.Text = "0";
    }

    // ── Tracker communication ────────────────────────────────────────────────

    private bool haveInitialPeers;

    private bool SendEventToTracker(TorrentInfo torrentInfo, string eventType) {
      scrapStatsUpdated = false;
      currentTorrent = torrentInfo;
      bool ignoreFailure = Dispatcher.Invoke(() => checkIgnoreFailureReason.IsChecked == true);
      var urlString = GetUrlString(torrentInfo, eventType);
      try {
        var uri = new Uri(urlString);
        var resp = MakeWebRequestEx(uri);
        if (resp?.Dict != null) {
          var dict = resp.Dict;
          var failure = BEncode.String(dict["failure reason"]);
          if (failure.Length > 0) {
            AddLogLine("Tracker Error: " + failure);
            if (!ignoreFailure) {
              StopButton_Click(null, null);
              AddLogLine("Stopped because of tracker error!!!");
              return false;
            }
          } else {
            foreach (string key in dict.Keys) {
              // skip known binary fields (peer lists) — would display garbled chars
              if (key == "failure reason" || key == "peers" || key == "peers6") continue;
              var val = BEncode.String(dict[key]);
              // skip values that look like raw binary (more than 20% non-printable chars)
              int nonPrint = 0;
              foreach (char c in val) if (c < 32 && c != '\n' && c != '\r' && c != '\t') nonPrint++;
              if (val.Length > 0 && nonPrint * 5 > val.Length) continue;
              AddLogLine(key + ": " + val);
            }

            if (dict.Contains("interval"))
              UpdateInterval(BEncode.String(dict["interval"]));

            if (dict.Contains("complete") && dict.Contains("incomplete")) {
              UpdateScrapStats(BEncode.String(dict["complete"]), BEncode.String(dict["incomplete"]), "");
              decimal leechers = BEncode.String(dict["incomplete"]).ParseValidInt(0);
              if (leechers == 0) {
                AddLogLine("Min number of leechers reached... setting upload speed to 0");
                UpdateTextBox(uploadRate, "0");
                Dispatcher.Invoke(() => chkRandUP.IsChecked = false);
              }
            }

            if (dict.Contains("peers")) {
              haveInitialPeers = true;
              string text4;
              if (dict["peers"] is ValueString) {
                text4 = BEncode.String(dict["peers"]);
                var enc = Encoding.GetEncoding(0x6faf);
                var buf = enc.GetBytes(text4);
                var rdr = new BinaryReader(new MemoryStream(enc.GetBytes(text4)));
                var list1 = new PeerList();
                for (int n = 0; n < buf.Length; n += 6)
                  list1.Add(new Peer(rdr.ReadBytes(4), rdr.ReadInt16()));
                rdr.Close();
                AddLogLine("peers: " + list1);
              } else if (dict["peers"] is ValueList) {
                var list2 = (ValueList)dict["peers"];
                var list3 = new PeerList();
                foreach (var obj in list2) {
                  if (obj is ValueDictionary d2)
                    list3.Add(new Peer(BEncode.String(d2["ip"]),
                                       BEncode.String(d2["port"]),
                                       BEncode.String(d2["peer id"])));
                }
                AddLogLine("peers: " + list3);
              } else {
                AddLogLine("peers(x): " + BEncode.String(dict["peers"]));
              }
            }
          }
          return false;
        }
        AddLogLine("No connection in sendEventToTracker() !!!");
        return false;
      } catch (Exception ex) {
        AddLogLine("Error in sendEventToTracker(): " + ex.Message);
        return false;
      }
    }

    internal void UpdateInterval(string param) {
      if (!Dispatcher.CheckAccess()) {
        Dispatcher.Invoke(() => UpdateInterval(param));
        return;
      }
      if (!updateProcessStarted) return;
      if (!int.TryParse(param, out var temp)) return;
      if (temp > 3600) temp = 3600;
      if (temp < 60)   temp = 60;
      currentTorrent.interval = temp;
      AddLogLine("Updating Interval: " + temp);
      interval.IsReadOnly = false;
      interval.Text = temp.ToString();
      interval.IsReadOnly = true;
    }

    private static long RoundByDenominator(long value, long denom) => denom * (value / denom);

    private string GetUrlString(TorrentInfo info, string eventType) {
      string uploaded;
      if (info.uploaded > 0) {
        info.uploaded = RoundByDenominator(info.uploaded, 0x4000);
        uploaded = info.uploaded.ToString();
      } else { uploaded = "0"; }
      string downloaded;
      if (info.downloaded > 0) {
        info.downloaded = RoundByDenominator(info.downloaded, 0x10);
        downloaded = info.downloaded.ToString();
      } else { downloaded = "0"; }
      if (info.left > 0) info.left = info.totalsize - info.downloaded;
      var left  = info.left.ToString();
      var url   = info.tracker;
      url += url.Contains("?") ? "&" : "?";
      if (eventType.Contains("started"))  url = url.Replace("&natmapped=1&localip={localip}", "");
      if (!eventType.Contains("stopped")) url = url.Replace("&trackerid=48", "");
      url += currentClient.Query;
      url = url.Replace("{infohash}",  HashUrlEncode(info.hash, currentClient.HashUpperCase));
      url = url.Replace("{peerid}",    info.peerID);
      url = url.Replace("{port}",      info.port);
      url = url.Replace("{uploaded}",  uploaded);
      url = url.Replace("{downloaded}",downloaded);
      url = url.Replace("{left}",      left);
      url = url.Replace("{event}",     eventType);
      if (info.numberOfPeers == "0" && !eventType.ToLower().Contains("stopped"))
        info.numberOfPeers = "200";
      url = url.Replace("{numwant}",   info.numberOfPeers);
      url = url.Replace("{key}",       info.key);
      url = url.Replace("{localip}",   Functions.GetIp());
      return url;
    }

    // ── Scrape ───────────────────────────────────────────────────────────────

    private void RequestScrapeFromTracker(TorrentInfo info) {
      Seeders = -1; Leechers = -1;
      bool doScrape = Dispatcher.Invoke(() => checkRequestScrap.IsChecked == true);
      if (!doScrape || scrapStatsUpdated) return;
      try {
        var text1 = GetScrapeUrlString(info);
        if (text1 == "") { AddLogLine("This tracker doesn't seem to support scrape"); }
        var resp = MakeWebRequestEx(new Uri(text1));
        if (resp?.Dict != null) {
          var fail = BEncode.String(resp.Dict["failure reason"]);
          if (fail.Length > 0) { AddLogLine("Tracker Error: " + fail); return; }
          AddLogLine("---------- Scrape Info -----------");
          var files = (ValueDictionary)resp.Dict["files"];
          var key   = Encoding.GetEncoding(0x4e4).GetString(currentTorrentFile.InfoHash);
          if (files[key] is ValueDictionary d2) {
            AddLogLine("complete: "   + BEncode.String(d2["complete"]));
            AddLogLine("downloaded: " + BEncode.String(d2["downloaded"]));
            AddLogLine("incomplete: " + BEncode.String(d2["incomplete"]));
            UpdateScrapStats(BEncode.String(d2["complete"]),
                             BEncode.String(d2["incomplete"]),
                             BEncode.String(d2["downloaded"]));
            decimal leechers = BEncode.String(d2["incomplete"]).ParseValidInt(-1);
            if (Leechers != -1 && leechers == 0) {
              AddLogLine("Min number of leechers reached... setting upload speed to 0");
              UpdateTextBox(uploadRate, "0");
              Dispatcher.Invoke(() => chkRandUP.IsChecked = false);
            }
          } else {
            AddLogLine("Scrape returned: '" + ((ValueString)files[key]).String + "'");
          }
        }
      } catch (Exception ex) {
        AddLogLine("Scrape Error: " + ex.Message);
      }
    }

    internal string GetScrapeUrlString(TorrentInfo info) {
      var url   = info.tracker;
      var index = url.LastIndexOf("/");
      if (url.Substring(index + 1, 8).ToLower() != "announce") return "";
      url  = url.Substring(0, index + 1) + "scrape" + url.Substring(index + 9);
      var hash = HashUrlEncode(info.hash, currentClient.HashUpperCase);
      url += url.Contains("?") ? "&" : "?";
      return url + "info_hash=" + hash;
    }

    // ── Update counters ──────────────────────────────────────────────────────

    private int Seeders = -1, Leechers = -1;
    internal int TotalRunningTimeCounter;

    private void UpdateCounters(TorrentInfo info) {
      if (!Dispatcher.CheckAccess()) {
        Dispatcher.Invoke(() => UpdateCounters(info));
        return;
      }
      try {
        uploadCount.Text = FormatFileSize((ulong)info.uploaded);
        var uploadedR = info.uploadRate + RandomSp(txtRandUpMin.Text, txtRandUpMax.Text, chkRandUP.IsChecked == true);
        if (uploadedR < 0) uploadedR = 0;
        info.uploaded += uploadedR;

        downloadCount.Text = FormatFileSize((ulong)info.downloaded);
        if (!seedMode && info.downloadRate > 0) {
          var downloadedR = info.downloadRate + RandomSp(txtRandDownMin.Text, txtRandDownMax.Text, chkRandDown.IsChecked == true);
          if (downloadedR < 0) downloadedR = 0;
          info.downloaded += downloadedR;
          info.left = info.totalsize - info.downloaded;
        }

        if (info.left <= 0) {
          info.downloaded = info.totalsize;
          info.left       = 0;
          info.downloadRate = 0;
          if (!seedMode) {
            currentTorrent = info;
            seedMode = true;
            temporaryIntervalCounter = 0;
            var t = new Thread(CompletedProcess) { Name = "completedProcess() Thread" };
            t.Start();
          }
        }

        info.interval = int.Parse(interval.Text);
        currentTorrent = info;

        if (info.totalsize == 0) {
          fileSize.Text = "100";
        } else {
          var pct = (currentTorrentFile.totalLength - (float)info.left) / currentTorrentFile.totalLength * 100.0;
          fileSize.Text = pct >= 100 ? "100" : SetPrecision(pct.ToString(), 2);
        }

        downloadCount.Text = FormatFileSize((ulong)info.downloaded);
        lblTorrentRatio.Text = info.downloaded / 1024 < 100
          ? "NaN"
          : SetPrecision((info.uploaded / (float)info.downloaded).ToString(), 2);
      } catch (Exception ex) {
        AddLogLine(ex.Message);
      }
    }

    private static string SetPrecision(string data, int prec) {
      var pow = (float)Math.Pow(10, prec);
      var v   = float.Parse(data) * pow;
      return ((int)v / pow).ToString();
    }

    internal void UpdateScrapStats(string seedStr, string leechStr, string finishedStr) {
      if (!Dispatcher.CheckAccess()) {
        Dispatcher.Invoke(() => UpdateScrapStats(seedStr, leechStr, finishedStr));
        return;
      }
      try { Seeders = int.Parse(seedStr); Leechers = int.Parse(leechStr); }
      catch { Seeders = -1; Leechers = -1; }
      seedLabel.Text  = "Seeds: " + seedStr;
      leechLabel.Text = "Peers: " + leechStr;
      scrapStatsUpdated = true;
    }

    internal void StopModule() {
      try {
        var stopWhen = GetComboText(cmbStopAfter);
        if (stopWhen == "When seeders <")
          if (Seeders > -1 && Seeders < int.Parse(txtStopValue.Text)) StopButton_Click(null, null);
        if (stopWhen == "When leechers <")
          if (Leechers > -1 && Leechers < int.Parse(txtStopValue.Text)) StopButton_Click(null, null);
        if (stopWhen == "When uploaded >")
          if (currentTorrent.uploaded > long.Parse(txtStopValue.Text) * 1024 * 1024) StopButton_Click(null, null);
        if (stopWhen == "When downloaded >")
          if (currentTorrent.downloaded > int.Parse(txtStopValue.Text) * 1024 * 1024) StopButton_Click(null, null);
        if (stopWhen == "When leechers/seeders <")
          if (Leechers / (double)Seeders < double.Parse(txtStopValue.Text)) StopButton_Click(null, null);
      } catch (Exception ex) {
        AddLogLine("Error in stopping module!!!: " + ex.Message);
      }
    }

    internal void serverUpdateTimer_Tick(object sender, EventArgs e) {
      if (!updateProcessStarted) return;
      if (haveInitialPeers) UpdateCounters(currentTorrent);
      var num1 = currentTorrent.interval - temporaryIntervalCounter;
      TotalRunningTimeCounter++;
      lblTotalTime.Text = ConvertToTime(TotalRunningTimeCounter);
      StopModule();
      if (num1 > 0) {
        temporaryIntervalCounter++;
        timerValue.Text = ConvertToTime(num1);
      } else {
        RandomiseSpeeds();
        OpenTcpListener();
        var t = new Thread(ContinueProcess);
        temporaryIntervalCounter = 0;
        timerValue.Text = "0";
        t.Name = "continueProcess() Thread";
        t.Start();
      }
    }

    internal void RandomiseSpeeds() {
      try {
        bool   randUp   = Dispatcher.Invoke(() => checkRandomUpload.IsChecked   == true);
        bool   randDown = Dispatcher.Invoke(() => checkRandomDownload.IsChecked == true);
        string upFrom   = Dispatcher.Invoke(() => RandomUploadFrom.Text);
        string upTo     = Dispatcher.Invoke(() => RandomUploadTo.Text);
        string downFrom = Dispatcher.Invoke(() => RandomDownloadFrom.Text);
        string downTo   = Dispatcher.Invoke(() => RandomDownloadTo.Text);
        if (randUp) {
          _nextAnnounceUploadRate = RandomSp(upFrom, upTo, true);
          AddLogLine("Next announce upload (random): " + _nextAnnounceUploadRate / 1024 + " kB/s");
        }
        if (randDown) {
          _nextAnnounceDownloadRate = RandomSp(downFrom, downTo, true);
          AddLogLine("Next announce download (random): " + _nextAnnounceDownloadRate / 1024 + " kB/s");
        }
      } catch (Exception ex) {
        AddLogLine("Failed to randomise upload/download speeds: " + ex.Message);
      }
    }

    internal int RandomSp(string min, string max, bool ret) {
      if (!ret) return rand.Next(10);
      var minn = int.Parse(min);
      var maxx = int.Parse(max);
      return rand.Next(Math.Min(minn, maxx), Math.Max(minn, maxx)) * 1024;
    }

    // ── Help functions ───────────────────────────────────────────────────────

    internal void UpdateTextBox(TextBox textbox, string text) {
      if (!Dispatcher.CheckAccess()) {
        Dispatcher.Invoke(() => textbox.Text = text);
      } else {
        textbox.Text = text;
      }
    }

    internal static string FormatFileSize(ulong sz) {
      if (sz >= 0x40000000) return string.Format("{0:########0.00} GB", (double)sz / 1073741824);
      if (sz >= 0x100000)   return string.Format("{0:####0.00} MB",     (double)sz / 1048576);
      if (sz >= 0x400)      return string.Format("{0:####0.00} KB",     (double)sz / 1024);
      return string.Format("{0} bytes", sz);
    }

    internal static string ToHexString(byte[] bytes) {
      var hex = "0123456789ABCDEF";
      var chars = new char[bytes.Length * 2];
      for (int i = 0; i < bytes.Length; i++) {
        chars[i*2]   = hex[bytes[i] >> 4];
        chars[i*2+1] = hex[bytes[i] & 0xF];
      }
      return new string(chars);
    }

    internal static string ConvertToTime(int seconds) {
      if (seconds < 3600)
        return (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
      return (seconds / 3600).ToString("00") + ":" + (seconds % 3600 / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
    }

    internal string HashUrlEncode(string decoded, bool upperCase) {
      var sb   = new StringBuilder();
      var gen  = new RandomStringGenerator();
      try {
        for (int i = 0; i < decoded.Length; i += 2)
          sb.Append((char)Convert.ToUInt16(decoded.Substring(i, 2), 16));
      } catch (Exception ex) { AddLogLine(ex.ToString()); }
      return gen.Generate(sb.ToString(), upperCase);
    }

    // ── Socket / Proxy ────────────────────────────────────────────────────────

    internal SocketEx CreateSocket() {
      SocketEx sock = null;
      try {
        sock = new SocketEx(currentProxy.ProxyType, currentProxy.ProxyServer, currentProxy.ProxyPort,
                            currentProxy.ProxyUser, currentProxy.ProxyPassword);
        sock.SetTimeout(0x30d40);
      } catch (Exception ex) { AddLogLine("createSocket error: " + ex.Message); }
      return sock;
    }

    private ProxyInfo GetCurrentProxy() {
      var enc  = Encoding.GetEncoding(0x4e4);
      var proxy = new ProxyInfo();
      switch (comboProxyType.SelectedIndex) {
        case 1: proxy.ProxyType = ProxyType.HttpConnect; break;
        case 2: proxy.ProxyType = ProxyType.Socks4;      break;
        case 3: proxy.ProxyType = ProxyType.Socks4a;     break;
        case 4: proxy.ProxyType = ProxyType.Socks5;      break;
        default: proxy.ProxyType = ProxyType.None;       break;
      }
      proxy.ProxyServer   = textProxyHost.Text;
      proxy.ProxyPort     = textProxyPort.Text.ParseValidInt(0);
      proxy.ProxyUser     = enc.GetBytes(textProxyUser.Text);
      proxy.ProxyPassword = enc.GetBytes(textProxyPass.Text);
      AddLogLine("PROXY INFO:");
      AddLogLine("proxyType = "     + proxy.ProxyType);
      AddLogLine("proxyServer = "   + proxy.ProxyServer);
      AddLogLine("proxyPort = "     + proxy.ProxyPort);
      AddLogLine("proxyUser = "     + Encoding.ASCII.GetString(proxy.ProxyUser) + "\n\n");
      return proxy;
    }

    private TrackerResponse MakeWebRequestEx(Uri reqUri, int redirectDepth = 0) {
      if (redirectDepth > 5) { AddLogLine("Error: too many redirects, aborting"); return null; }
      if (reqUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)) {
        // No TLS over a proxy tunnel yet: refuse rather than send the passkey in clear
        if (currentProxy.ProxyType != ProxyType.None) { AddLogLine("Error: HTTPS trackers are not supported through a proxy"); return null; }
        return MakeHttpsRequest(reqUri, redirectDepth);
      }

      var enc  = Encoding.GetEncoding(0x4e4);
      SocketEx sock = null;
      try {
        var host = reqUri.Host;
        var port = reqUri.Port;
        var path = reqUri.PathAndQuery;
        AddLogLine("Connecting to tracker (" + host + ") in port " + port);
        sock = CreateSocket();
        sock.PreAuthenticate = false;
        int attempts = 0; bool connected = false;
        while (attempts < 5 && !connected) {
          try { sock.Connect(host, port); connected = true; AddLogLine("Connected Successfully"); }
          catch (Exception ex) { AddLogLine("Exception: " + ex.Message); AddLogLine("Failed connection attempt: " + attempts); attempts++; }
        }
        var cmd = "GET " + path + " " + currentClient.HttpProtocol + "\r\n"
                  + currentClient.Headers.Replace("{host}", host) + "\r\n";
        AddLogLine("======== Sending Command to Tracker ========");
        AddLogLine(cmd);
        sock.Send(enc.GetBytes(cmd));
        try {
          var data = new byte[32 * 1024];
          var mem  = new MemoryStream();
          while (true) { var n = sock.Receive(data); if (n == 0) break; mem.Write(data, 0, n); }
          if (mem.Length == 0) { AddLogLine("Error: Tracker Response is empty"); return null; }
          var resp = new TrackerResponse(mem);
          if (resp.doRedirect) return MakeWebRequestEx(new Uri(resp.RedirectionURL), redirectDepth + 1);
          AddLogLine("======== Tracker Response ========");
          AddLogLine(resp.Headers);
          if (resp.Dict == null) { AddLogLine("*** Failed to decode tracker response:"); AddLogLine(resp.Body); }
          mem.Dispose(); return resp;
        } catch (Exception ex) { sock.Close(); AddLogLine(ex.Message); return null; }
      } catch (Exception ex) { sock?.Close(); AddLogLine("Exception:" + ex.Message); return null; }
    }

    private TrackerResponse MakeHttpsRequest(Uri reqUri, int redirectDepth = 0) {
      var enc = Encoding.GetEncoding(0x4e4);
      var host = reqUri.Host; var port = reqUri.Port; var path = reqUri.PathAndQuery;
      AddLogLine("Connecting to tracker via HTTPS (" + host + ") in port " + port);
      TcpClient tcp = null; SslStream ssl = null;
      try {
        tcp = new TcpClient();
        int attempts = 0; bool connected = false;
        while (attempts < 5 && !connected) {
          try { tcp.Connect(host, port); connected = true; AddLogLine("Connected Successfully"); }
          catch (Exception ex) { AddLogLine("Exception: " + ex.Message); attempts++; }
        }
        if (!connected) { AddLogLine("Could not connect after 5 attempts"); return null; }
        var netStream = tcp.GetStream(); netStream.ReadTimeout = 15000;
        ssl = new SslStream(netStream, false);
        ssl.AuthenticateAsClient(host);
        var headers = currentClient.Headers.Replace("{host}", host);
        if (!headers.Contains("Connection:")) headers += "Connection: close\r\n";
        var cmd = "GET " + path + " " + currentClient.HttpProtocol + "\r\n" + headers + "\r\n";
        AddLogLine("======== Sending Command to Tracker ========"); AddLogLine(cmd);
        var cmdBytes = enc.GetBytes(cmd); ssl.Write(cmdBytes, 0, cmdBytes.Length); ssl.Flush();
        var data = new byte[32 * 1024]; var mem = new MemoryStream();
        try { int n; while ((n = ssl.Read(data, 0, data.Length)) > 0) mem.Write(data, 0, n); }
        catch (IOException) { }
        if (mem.Length == 0) { AddLogLine("Error: Tracker Response is empty"); return null; }
        var resp = new TrackerResponse(mem);
        if (resp.doRedirect) return MakeWebRequestEx(new Uri(resp.RedirectionURL), redirectDepth + 1);
        AddLogLine("======== Tracker Response ========"); AddLogLine(resp.Headers);
        if (resp.Dict == null) { AddLogLine("*** Failed to decode:"); AddLogLine(resp.Body); }
        mem.Dispose(); return resp;
      } catch (Exception ex) { AddLogLine("HTTPS Exception: " + ex.Message); return null; }
      finally { ssl?.Close(); tcp?.Close(); }
    }

    // ── Remaining work timer ─────────────────────────────────────────────────

    internal void RemaningWork_Tick(object sender, EventArgs e) {
      if (txtStopValue.Text == "0") return;
      remWork++;
      var rw = int.Parse(txtStopValue.Text);
      txtRemTime.Text = ConvertToTime(rw - remWork);
      if (remWork >= rw) { txtRemTime.Text = "0"; RemaningWork.Stop(); StopButton_Click(null, null); }
    }

    // ── Processes ────────────────────────────────────────────────────────────

    internal void StopProcess()      => SendEventToTracker(currentTorrent, "&event=stopped");
    internal void CompletedProcess() { SendEventToTracker(currentTorrent, "&event=completed"); RequestScrapeFromTracker(currentTorrent); }
    internal void ContinueProcess()  { SendEventToTracker(currentTorrent, ""); RequestScrapeFromTracker(currentTorrent); }
    internal void StartProcess()     { if (SendEventToTracker(currentTorrent, "&event=started")) { updateProcessStarted = true; RequestScrapeFromTracker(currentTorrent); } }

    // ── Speed text changed ───────────────────────────────────────────────────

    internal void uploadRate_TextChanged(object sender, TextChangedEventArgs e) {
      if (!updateProcessStarted) return;
      currentTorrent.uploadRate = string.IsNullOrEmpty(uploadRate.Text) ? 0
        : uploadRate.Text.ParseValidInt64(new TorrentInfo(0, 0).uploadRate / 1024) * 1024;
    }

    internal void downloadRate_TextChanged(object sender, TextChangedEventArgs e) {
      if (!updateProcessStarted) return;
      currentTorrent.downloadRate = string.IsNullOrEmpty(downloadRate.Text) ? 0
        : downloadRate.Text.ParseValidInt64(new TorrentInfo(0, 0).downloadRate / 1024) * 1024;
    }

    // ── Stop-after combo ──────────────────────────────────────────────────────

    private void cmbStopAfter_SelectedIndexChanged(object sender, SelectionChangedEventArgs e) {
      if (txtStopValue == null || lblStopAfter == null) return;
      var sel = GetComboText(cmbStopAfter);
      if (sel == "Never") {
        lblStopAfter.Text = "";
        txtStopValue.Text = "";
        txtStopValue.Visibility = Visibility.Collapsed;
      } else {
        txtStopValue.Visibility = Visibility.Visible;
        switch (sel) {
          case "After time:":              lblStopAfter.Text = "seconds"; txtStopValue.Text = "3600"; break;
          case "When seeders <":           lblStopAfter.Text = "";        txtStopValue.Text = "10";   break;
          case "When leechers <":          lblStopAfter.Text = "";        txtStopValue.Text = "10";   break;
          case "When uploaded >":          lblStopAfter.Text = "Mb";      txtStopValue.Text = "1024"; break;
          case "When downloaded >":        lblStopAfter.Text = "Mb";      txtStopValue.Text = "1024"; break;
          case "When leechers/seeders <":  lblStopAfter.Text = "";        txtStopValue.Text = "1,000";break;
        }
      }
    }

    // ── Settings ─────────────────────────────────────────────────────────────

    internal void ReadSettings() {
      try {
        var reg = Registry.CurrentUser.OpenSubKey("Software\\RatioMaster", false);
        if (reg == null) { Registry.CurrentUser.CreateSubKey("Software\\RatioMaster"); return; }
        var v = (string)reg.GetValue("Version", "none");
        if (v == "none") { btnDefault_Click(null, null); return; }

        chkNewValues.IsChecked = ItoB((int)reg.GetValue("NewValues", 1));
        getnew = false;
        SetComboByText(cmbClient,    (string)reg.GetValue("Client",        DefaultClient));
        getnew = false;
        SetComboByText(cmbVersion,   (string)reg.GetValue("ClientVersion", DefaultClientVersion));
        uploadRate.Text   = (string)reg.GetValue("UploadRate",   uploadRate.Text);
        downloadRate.Text = (string)reg.GetValue("DownloadRate", downloadRate.Text);
        fileSize.Text     = (string)reg.GetValue("fileSize",     "100");
        interval.Text     = reg.GetValue("Interval", interval.Text).ToString();
        DefaultDirectory  = (string)reg.GetValue("Directory", DefaultDirectory);

        checkTCPListen.IsChecked         = ItoB((int)reg.GetValue("TCPlistener",  1));
        checkRequestScrap.IsChecked      = ItoB((int)reg.GetValue("ScrapeInfo",   1));
        chkRandUP.IsChecked              = ItoB((int)reg.GetValue("GetRandUp",    1));
        chkRandDown.IsChecked            = ItoB((int)reg.GetValue("GetRandDown",  1));
        txtRandUpMin.Text   = (string)reg.GetValue("MinRandUp",   txtRandUpMin.Text);
        txtRandUpMax.Text   = (string)reg.GetValue("MaxRandUp",   txtRandUpMax.Text);
        txtRandDownMin.Text = (string)reg.GetValue("MinRandDown", txtRandDownMin.Text);
        txtRandDownMax.Text = (string)reg.GetValue("MaxRandDown", txtRandDownMax.Text);

        if (chkNewValues.IsChecked == false) {
          customKey.Text    = (string)reg.GetValue("CustomKey",    customKey.Text);
          customPeerID.Text = (string)reg.GetValue("CustomPeerID", customPeerID.Text);
          lblGenStatus.Text = "Generation status: using last saved values";
        } else {
          SetCustomValues();
        }
        customPort.Text     = (string)reg.GetValue("CustomPort",   customPort.Text);
        customPeersNum.Text = (string)reg.GetValue("CustomPeers",  customPeersNum.Text);

        checkRandomUpload.IsChecked   = ItoB((int)reg.GetValue("GetRandUpNext",  0));
        checkRandomDownload.IsChecked = ItoB((int)reg.GetValue("GetRandDownNext",0));
        RandomUploadFrom.Text   = (string)reg.GetValue("MinRandUpNext",   RandomUploadFrom.Text);
        RandomUploadTo.Text     = (string)reg.GetValue("MaxRandUpNext",   RandomUploadTo.Text);
        RandomDownloadFrom.Text = (string)reg.GetValue("MinRandDownNext", RandomDownloadFrom.Text);
        RandomDownloadTo.Text   = (string)reg.GetValue("MaxRandDownNext", RandomDownloadTo.Text);

        SetComboByText(cmbStopAfter, (string)reg.GetValue("StopWhen", "Never"));
        txtStopValue.Text = (string)reg.GetValue("StopAfter", txtStopValue.Text);

        SetComboByText(comboProxyType, (string)reg.GetValue("ProxyType",    "None"));
        textProxyHost.Text = (string)reg.GetValue("ProxyAdress", textProxyHost.Text);
        textProxyUser.Text = (string)reg.GetValue("ProxyUser",   textProxyUser.Text);
        textProxyPass.Text = (string)reg.GetValue("ProxyPass",   textProxyPass.Text);
        textProxyPort.Text = (string)reg.GetValue("ProxyPort",   textProxyPort.Text);

        checkIgnoreFailureReason.IsChecked = ItoB((int)reg.GetValue("IgnoreFailureReason", 0));
      } catch (Exception ex) {
        AddLogLine("Error in ReadSettings(): " + ex.Message);
      }
    }

    // Set ComboBox selected item by matching text (handles ComboBoxItem or string items)
    private void SetComboByText(ComboBox combo, string text) {
      if (text == null) return;
      for (int i = 0; i < combo.Items.Count; i++) {
        var item = combo.Items[i];
        var itemText = (item as ComboBoxItem)?.Content?.ToString() ?? item?.ToString() ?? "";
        if (string.Equals(itemText, text, StringComparison.OrdinalIgnoreCase)) {
          combo.SelectedIndex = i;
          return;
        }
      }
      // If not found, try adding as string
      combo.Items.Add(text);
      combo.SelectedIndex = combo.Items.Count - 1;
    }

    internal static int BtoI(bool? b) => b == true ? 1 : 0;
    internal static bool ItoB(int v)  => v != 0;

    // ── Custom values ─────────────────────────────────────────────────────────

    private void chkNewValues_Changed(object sender, RoutedEventArgs e) {
      if (!IsLoaded) return;  // skip during XAML init
      if (chkNewValues.IsChecked == true) SetCustomValues();
    }

    internal void GetRandCustVal() {
      var name = GetClientNameStr();
      currentClient = TorrentClientFactory.GetClient(name);
      customKey.Text    = currentClient.Key;
      customPeerID.Text = currentClient.PeerID;
      currentTorrent.port = rand.Next(1025, 65535).ToString();
      customPort.Text = currentTorrent.port;
      currentTorrent.numberOfPeers = currentClient.DefNumWant.ToString();
      customPeersNum.Text = currentTorrent.numberOfPeers;
      lblGenStatus.Text = "Generation status: generated new values for " + name;
    }

    internal void SetCustomValues() {
      var name = GetClientNameStr();
      currentClient = TorrentClientFactory.GetClient(name);
      AddLogLine("Client changed: " + name);
      if (!currentClient.Parse) {
        GetRandCustVal();
      } else {
        var searchStr   = currentClient.SearchString;
        var maxOffset   = currentClient.MaxOffset;
        var startOffset = currentClient.StartOffset;
        var process     = currentClient.ProcessName;
        var pversion    = GetComboText(cmbVersion);
        if (Getdata(process, pversion, searchStr, startOffset, maxOffset)) {
          customKey.Text    = currentClient.Key;
          customPeerID.Text = currentClient.PeerID;
          customPort.Text   = currentTorrent.port;
          customPeersNum.Text = currentTorrent.numberOfPeers;
          lblGenStatus.Text = "Generation status: " + name + " found! Parsed all values!";
        } else {
          GetRandCustVal();
        }
      }
    }

    internal bool Getdata(string client, string pversion, string searchStr, long startOffset, long maxOffset) {
      try {
        uint bufSize = 0x10000;
        var clientLower = client.ToLower();
        var enc = Encoding.ASCII;
        var proc = FindProcessByName(clientLower);
        if (proc == null) return false;
        var offset = startOffset;
        var pReader = new ProcessMemoryReader { ReadProcess = proc };
        bool found = false;
        pReader.OpenProcess();
        while (offset < maxOffset) {
          int bytesRead;
          var buf = pReader.ReadProcessMemory((IntPtr)offset, bufSize, out bytesRead);
          long pos = GetStringOffset(buf, enc, searchStr);
          if (pos >= 0) {
            found = true;
            var text = enc.GetString(buf);
            var m = new Regex("&peer_id=(.+?)(&| )", RegexOptions.Compiled).Match(text);
            if (m.Success) { currentClient.PeerID = m.Groups[1].ToString(); AddLogLine("====> PeerID = " + currentClient.PeerID); }
            m = new Regex("&key=(.+?)(&| )", RegexOptions.Compiled).Match(text);
            if (m.Success) { currentClient.Key = m.Groups[1].ToString(); AddLogLine("====> Key = " + currentClient.Key); }
            m = new Regex("&port=(.+?)(&| )", RegexOptions.Compiled).Match(text);
            if (m.Success) { currentTorrent.port = m.Groups[1].ToString(); AddLogLine("====> Port = " + currentTorrent.port); }
            m = new Regex("&numwant=(.+?)(&| )", RegexOptions.Compiled).Match(text);
            if (m.Success) {
              currentTorrent.numberOfPeers = m.Groups[1].ToString();
              if (!int.TryParse(currentTorrent.numberOfPeers, out _))
                currentTorrent.numberOfPeers = currentClient.DefNumWant.ToString();
              AddLogLine("====> NumWant = " + currentTorrent.numberOfPeers);
            }
            break;
          }
          offset += (int)bufSize;
        }
        pReader.CloseHandle();
        if (found) { AddLogLine("Search finished successfully!"); return true; }
        AddLogLine("Search failed. Make sure that torrent client {" + GetClientNameStr() + "} is running.");
        return false;
      } catch (Exception ex) { AddLogLine("Error when parsing: " + ex.Message); return false; }
    }

    private Process FindProcessByName(string name) {
      AddLogLine("Looking for " + name + " process...");
      var procs = Process.GetProcessesByName(name);
      if (procs.Length == 0) { AddLogLine("No " + name + " process found."); return null; }
      AddLogLine(name + " process found!");
      return procs[0];
    }

    private static int GetStringOffset(byte[] mem, Encoding enc, string s) => enc.GetString(mem).IndexOf(s);

    public override string ToString() => "RatioMaster";
  }
}
