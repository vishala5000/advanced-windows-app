using System;
using System.Windows;

namespace MyApp
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void ExecuteAction_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string input = InputBox.Text;
                if (string.IsNullOrWhiteSpace(input))
                {
                    MessageBox.Show("Please enter some text first.", "Validation Error", 
                                    MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // TODO: ChatGPT will replace this with your actual advanced logic
                StatusText.Text = "Processing...";
                
                // Simulate work
                System.Threading.Thread.Sleep(500); 
                
                StatusText.Text = $"Success! Processed: {input}";
                StatusText.Foreground = System.Windows.Media.Brushes.Green;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred: {ex.Message}", "Error", 
                                MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Failed";
                StatusText.Foreground = System.Windows.Media.Brushes.Red;
            }
        }
    }
}
