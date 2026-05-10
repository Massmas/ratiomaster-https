using System.Windows;

namespace RatioMaster {
  public partial class InputDialog : Window {
    public string Result { get; private set; } = "";

    public InputDialog(string title, string prompt, string defaultValue = "") {
      InitializeComponent();
      Title = title;
      lblPrompt.Text = prompt;
      txtInput.Text = defaultValue;
      txtInput.SelectAll();
      txtInput.Focus();
    }

    private void btnOk_Click(object sender, RoutedEventArgs e) {
      Result = txtInput.Text;
      DialogResult = true;
    }

    private void btnCancel_Click(object sender, RoutedEventArgs e) {
      DialogResult = false;
    }
  }
}
