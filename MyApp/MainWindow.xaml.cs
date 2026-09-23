using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace MyApp
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private async void BtnGenerate_Click(object sender, RoutedEventArgs e)
        {
            // 1. Validate Input
            var quotes = txtQuotes.Text
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(q => q.Trim())
                .Where(q => !string.IsNullOrEmpty(q))
                .ToList();

            if (!quotes.Any())
            {
                MessageBox.Show("Please enter at least one quote.", "Input Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 2. Ask user where to save the final ZIP file
            var saveDialog = new SaveFileDialog
            {
                Filter = "ZIP Archive (*.zip)|*.zip",
                Title = "Save Generated Shorts ZIP",
                FileName = $"AutoShorts_{DateTime.Now:yyyyMMdd_HHmmss}.zip"
            };

            if (saveDialog.ShowDialog() != true) return;

            string zipDestination = saveDialog.FileName;
            string tempDir = Path.Combine(Path.GetTempPath(), "AutoShorts_Temp_" + Guid.NewGuid().ToString("N"));

            try
            {
                // 3. Lock UI and show progress
                SetUiBusy(true);
                Directory.CreateDirectory(tempDir);

                // 4. Process each quote asynchronously to keep UI responsive
                for (int i = 0; i < quotes.Count; i++)
                {
                    string quote = quotes[i];
                    int index = i + 1;
                    
                    UpdateStatus($"Generating Short {index} of {quotes.Count}: \"{TruncateText(quote, 40)}...\"");
                    UpdateProgress((double)i / quotes.Count * 100);

                    // Offload heavy video generation to a background thread
                    string outputFilePath = Path.Combine(tempDir, $"{index}.mp4");
                    await Task.Run(() => GenerateVideoEngine(quote, index, outputFilePath));
                }

                // 5. Zip all generated videos
                UpdateStatus("Packaging videos into ZIP archive...");
                UpdateProgress(95);
                
                // Run ZIP creation on background thread as it can be heavy for large files
                await Task.Run(() => ZipFile.CreateFromDirectory(tempDir, zipDestination));

                UpdateProgress(100);
                UpdateStatus($"Success! {quotes.Count} shorts saved to: {zipDestination}");
                
                MessageBox.Show($"Successfully generated and zipped {quotes.Count} YouTube Shorts!", "Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                // Robust error handling to prevent crashes
                MessageBox.Show($"An error occurred:\n\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatus("Failed. Please check the error message and try again.");
            }
            finally
            {
                // 6. Cleanup temporary files
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { /* Ignore cleanup errors */ }
                }
                SetUiBusy(false);
            }
        }

        /// <summary>
        /// ADVANCED PLACEHOLDER: This is where the actual video rendering happens.
        /// In a production environment, you would use a library like FFmpeg (via Xabe.FFmpeg or FFmpeg.AutoGen) 
        /// or an AI Video API here to create a 1080x1920 (9:16) MP4 with a colorful background 
        /// and perfectly wrapped, centered text.
        /// </summary>
        private void GenerateVideoEngine(string quoteText, int index, string outputPath)
        {
            // SIMULATION: Simulating heavy video rendering work (2 seconds per video)
            Task.Delay(2000).Wait(); 

            // In reality, you would write the MP4 file to 'outputPath' here.
            // For this template, we create a dummy file so the ZIP logic works perfectly.
            File.WriteAllText(outputPath, $"[Dummy MP4 Data for Quote {index}: {quoteText}]");
        }

        #region UI Helper Methods

        private void SetUiBusy(bool isBusy)
        {
            btnGenerate.IsEnabled = !isBusy;
            txtQuotes.IsReadOnly = isBusy;
            progressBar.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
            if (!isBusy) progressBar.Value = 0;
        }

        private void UpdateStatus(string message)
        {
            // Dispatcher is used because we might call this from background threads
            Dispatcher.Invoke(() => txtStatus.Text = message);
        }

        private void UpdateProgress(double percentage)
        {
            Dispatcher.Invoke(() => progressBar.Value = percentage);
        }

        private string TruncateText(string text, int maxLength)
        {
            return text.Length <= maxLength ? text : text.Substring(0, maxLength);
        }

        #endregion
    }
}
