using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace RatioMaster {
  public partial class App : Application {

    public static bool _24h_format_enabled = false;
    public static bool IsDarkTheme = true;

    protected override void OnStartup(StartupEventArgs e) {
      // Register global error handlers first
      AppDomain.CurrentDomain.UnhandledException += (s, ex) => {
        var msg = (ex.ExceptionObject as Exception)?.ToString() ?? ex.ExceptionObject?.ToString();
        try { File.WriteAllText("RatioMaster_crash.log", msg); } catch { }
        MessageBox.Show("Fatal error:\n" + msg, "RatioMaster", MessageBoxButton.OK, MessageBoxImage.Error);
      };
      DispatcherUnhandledException += (s, ex) => {
        try { File.WriteAllText("RatioMaster_crash.log", ex.Exception.ToString()); } catch { }
        MessageBox.Show("Error:\n" + ex.Exception.ToString(), "RatioMaster", MessageBoxButton.OK, MessageBoxImage.Error);
        ex.Handled = true;
      };

      // Load default theme in code (avoid XAML-time failure)
      try {
        SetTheme(true); // dark by default
      } catch (Exception themeEx) {
        File.WriteAllText("RatioMaster_crash.log", "Theme load error:\n" + themeEx);
        MessageBox.Show("Theme load error:\n" + themeEx.Message, "RatioMaster", MessageBoxButton.OK, MessageBoxImage.Error);
      }

      base.OnStartup(e);
    }

    public static void SetTheme(bool dark) {
      IsDarkTheme = dark;
      var dict = new ResourceDictionary {
        Source = new System.Uri(
          dark ? "Themes/Dark.xaml" : "Themes/Light.xaml",
          System.UriKind.Relative)
      };
      Current.Resources.MergedDictionaries.Clear();
      Current.Resources.MergedDictionaries.Add(dict);
    }
  }
}
