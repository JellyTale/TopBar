using System.Windows;

namespace TopBar
{
    public partial class RenameDialog : Window
    {
        public string NewName { get; private set; } = string.Empty;

        public RenameDialog(string currentName)
        {
            InitializeComponent();
            NameBox.Text = currentName;
            NameBox.SelectAll();
            NameBox.Focus();
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            var trimmed = NameBox.Text.Trim();
            if (!string.IsNullOrEmpty(trimmed))
            {
                NewName = trimmed;
                DialogResult = true;
            }
        }
    }
}
