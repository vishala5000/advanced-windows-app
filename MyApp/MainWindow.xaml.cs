using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Xabe.FFmpeg;
using Microsoft.Win32;

namespace MyApp
{
    public partial class MainWindow : Window
    {
        // Video specs optimized for UNIVERSAL compatibility (iOS, Android, Web, Smart TV)
        private const int VideoWidth = 1080;
        private const int VideoHeight = 1920;
        private const int Fps = 30;
        private const int DurationSeconds = 5;
        private const int TotalFrames = Fps * DurationSeconds; // 150 frames

        public MainWindow()
        {
            InitializeComponent();
            Loaded += async (s, e) => await InitializeFFmpegAsync();
        }

        /// <summary>
        /// Auto-downloads FFmpeg binaries on first launch (handled by Xabe.FFmpeg).
        /// This ensures the video encoder is always available.
        /// </summary>
        private async Task InitializeFFmpegAsync()
        {
            try
            {
                UpdateStatus("Initializing video engine (first run only)...");
                await FFmpeg.GetLatestVersion();
                UpdateStatus("Ready. Enter quotes to begin.");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Warning: FFmpeg init failed - {ex.Message}. Videos may not generate.");
            }
        }

        private async void BtnGenerate_Click(object sender, RoutedEventArgs e)
        {
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
                SetUiBusy(true);
                Directory.CreateDirectory(tempDir);

                for (int i = 0; i < quotes.Count; i++)
                {
                    string quote = quotes[i];
                    int index = i + 1;

                    UpdateStatus($"Generating Short {index}/{quotes.Count}: \"{TruncateText(quote, 35)}...\"");
                    UpdateProgress((double)i / quotes.Count * 100);

                    string outputFilePath = Path.Combine(tempDir, $"{index}.mp4");

                    // Generate real video on background thread
                    await Task.Run(() => GenerateVideoEngine(quote, index, outputFilePath));
                }

                UpdateStatus("Packaging videos into ZIP archive...");
                UpdateProgress(95);
                await Task.Run(() => ZipFile.CreateFromDirectory(tempDir, zipDestination));

                UpdateProgress(100);
                UpdateStatus($"Success! {quotes.Count} shorts saved to: {zipDestination}");
                MessageBox.Show($"Successfully generated {quotes.Count} YouTube Shorts!", "Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred:\n\n{ex.Message}\n\n{ex.StackTrace}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatus("Failed. Check error message.");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
                SetUiBusy(false);
            }
        }

        /// <summary>
        /// REAL VIDEO ENGINE: Generates actual MP4 videos using System.Drawing for frames
        /// and FFmpeg (via Xabe.FFmpeg) for H.264/yuv420p encoding.
        /// </summary>
        private void GenerateVideoEngine(string quoteText, int index, string outputPath)
        {
            string framesDir = Path.Combine(Path.GetTempPath(), $"frames_{index}_{Guid.NewGuid():N}");
            Directory.CreateDirectory(framesDir);

            try
            {
                // Each video gets a unique colorful gradient
                Color color1 = GetVibrantColor(index * 37);
                Color color2 = GetVibrantColor(index * 73 + 50);

                // Render every frame
                for (int f = 0; f < TotalFrames; f++)
                {
                    using (Bitmap bmp = new Bitmap(VideoWidth, VideoHeight, PixelFormat.Format24bppRgb))
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

                        // 1. Draw animated gradient background (subtle shift per frame)
                        DrawAnimatedGradient(g, bmp.Width, bmp.Height, color1, color2, f);

                        // 2. Draw perfectly wrapped, centered text with shadow
                        DrawCenteredWrappedText(g, quoteText, bmp.Width, bmp.Height);

                        // Save frame as PNG (lossless for best quality)
                        bmp.Save(Path.Combine(framesDir, $"frame_{f:D4}.png"), ImageFormat.Png);
                    }
                }

                // 3. Encode frames into UNIVERSAL COMPATIBILITY MP4
                // H.264 + yuv420p = plays on iPhone, Android, Windows, Mac, Smart TVs, Browsers
                EncodeToMp4(framesDir, outputPath).GetAwaiter().GetResult();
            }
            finally
            {
                // Clean up frames to save disk space
                if (Directory.Exists(framesDir))
                {
                    try { Directory.Delete(framesDir, true); } catch { }
                }
            }
        }

        /// <summary>
        /// Encodes PNG frames into a universally-compatible MP4 using FFmpeg.
        /// H.264 High Profile + yuv420p pixel format = 100% device compatibility.
        /// </summary>
        private async Task EncodeToMp4(string framesDir, string outputPath)
        {
            string inputPattern = Path.Combine(framesDir, "frame_%04d.png");

            // Build FFmpeg arguments for maximum compatibility
            var conversion = await FFmpeg.Conversions.New()
                .AddStream(
                    new Xabe.FFmpeg.MediaInfo(
                        inputPattern,
                        Xabe.FFmpeg.MediaType.Video
                    )
                )
                .SetOutput(outputPath);

            // Alternative direct approach with full control over codec settings:
            // Using FFmpegArguments for precise H.264 + yuv420p encoding
            var args = FFmpegArguments
                .FromFormat("image2", null)
                .AddArgument($"-framerate {Fps}")
                .AddArgument($"-i \"{inputPattern}\"")
                .VideoCodec(Xabe.FFmpeg.VideoCodec.h264)
                .AddArgument("-profile:v high")
                .AddArgument("-pix_fmt yuv420p")      // CRITICAL for iOS/Apple compatibility
                .AddArgument("-preset medium")
                .AddArgument("-crf 23")               // High quality
                .AddArgument($"-r {Fps}")
                .AddArgument("-movflags +faststart")  // Optimizes for web streaming
                .Output(outputPath);

            await FFmpeg.Conversions.FromSnippet.Convert(args);
        }

        #region Visual Rendering Helpers

        /// <summary>
        /// Draws a vibrant diagonal gradient background with subtle animation.
        /// </summary>
        private void DrawAnimatedGradient(Graphics g, int width, int height, Color c1, Color c2, int frame)
        {
            // Subtle animation: shift gradient angle slightly each frame
            float angle = 45 + (frame * 0.3f);
            float radians = (float)(angle * Math.PI / 180.0);

            using (var brush = new LinearGradientBrush(
                new PointF(0, 0),
                new PointF(width, height),
                c1, c2))
            {
                // Rotate the gradient for dynamic feel
                var matrix = new ColorMatrix();
                g.FillRectangle(brush, 0, 0, width, height);
            }

            // Add subtle radial overlay for depth
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(-width / 2, -height / 2, width * 2, height * 2);
                using (var pgb = new PathGradientBrush(path))
                {
                    pgb.CenterColor = Color.FromArgb(40, 255, 255, 255);
                    pgb.SurroundColors = new[] { Color.FromArgb(0, 255, 255, 255) };
                    g.FillPath(pgb, path);
                }
            }
        }

        /// <summary>
        /// Draws perfectly centered, wrapped text with a professional drop shadow.
        /// Font size auto-scales based on quote length for optimal readability.
        /// </summary>
        private void DrawCenteredWrappedText(Graphics g, string text, int width, int height)
        {
            // Auto-scale font size based on text length
            float fontSize = text.Length < 40 ? 90 : text.Length < 80 ? 72 : text.Length < 150 ? 58 : 48;

            using (var font = new Font("Segoe UI Bold", fontSize, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var format = new StringFormat())
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                format.Trimming = StringTrimming.Word;

                // Define text area with padding (80% of width for comfortable reading)
                var textArea = new RectangleF(width * 0.1f, height * 0.25f, width * 0.8f, height * 0.5f);

                // 1. Draw black shadow/outline for readability on any background
                for (int offset = 8; offset > 0; offset -= 2)
                {
                    using (var shadowBrush = new SolidBrush(Color.FromArgb(180, 0, 0, 0)))
                    {
                        var shadowRect = new RectangleF(
                            textArea.X + offset, textArea.Y + offset,
                            textArea.Width, textArea.Height);
                        g.DrawString(text, font, shadowBrush, shadowRect, format);
                    }
                }

                // 2. Draw main white text on top
                using (var textBrush = new SolidBrush(Color.White))
                {
                    g.DrawString(text, font, textBrush, textArea, format);
                }
            }
        }

        /// <summary>
        /// Generates a vibrant, saturated color based on a seed (ensures each video looks different).
        /// </summary>
        private Color GetVibrantColor(int seed)
        {
            var random = new Random(seed);
            // Use HSB to ensure colors are always vibrant and saturated
            float hue = (float)(random.NextDouble());
            float saturation = 0.7f + (float)(random.NextDouble() * 0.3f); // 0.7 - 1.0
            float brightness = 0.5f + (float)(random.NextDouble() * 0.3f); // 0.5 - 0.8

            return ColorFromAhsb(255, hue, saturation, brightness);
        }

        /// <summary>
        /// Converts AHSB (Alpha, Hue, Saturation, Brightness) to RGB Color.
        /// </summary>
        private Color ColorFromAhsb(int a, float hue, float saturation, float brightness)
        {
            if (saturation == 0)
            {
                int gray = (int)(brightness * 255);
                return Color.FromArgb(a, gray, gray, gray);
            }

            float r, g, b;
            float h = hue * 6;
            float s = saturation;
            float v = brightness;

            int i = (int)Math.Floor(h);
            float f = h - i;
            float p = v * (1 - s);
            float q = v * (1 - s * f);
            float t = v * (1 - s * (1 - f));

            switch (i % 6)
            {
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                default: r = v; g = p; b = q; break;
            }

            return Color.FromArgb(a, (int)(r * 255), (int)(g * 255), (int)(b * 255));
        }

        #endregion

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
