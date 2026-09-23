using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
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
        // OPTIMIZED SPECS: 24fps (still smooth) + BMP frames (10x faster than PNG)
        private const int VideoWidth = 1080;
        private const int VideoHeight = 1920;
        private const int Fps = 24; // 24fps is cinematic and 20% faster than 30fps
        private const int DurationSeconds = 12;
        private const int TotalFrames = Fps * DurationSeconds; // 288 frames (vs 360)

        private const int SafeMarginTop = 180;
        private const int SafeMarginBottom = 380;
        private const int SafeMarginRight = 180;
        private const int SafeMarginLeft = 80;

        private static readonly List<(Color c1, Color c2)> Palettes = new List<(Color, Color)>
        {
            (Color.FromArgb(255, 94, 53),  Color.FromArgb(255, 19, 97)),
            (Color.FromArgb(20, 30, 48),   Color.FromArgb(36, 59, 85)),
            (Color.FromArgb(131, 58, 180), Color.FromArgb(253, 29, 29)),
            (Color.FromArgb(1, 115, 117),  Color.FromArgb(7, 59, 76)),
            (Color.FromArgb(255, 154, 0),  Color.FromArgb(255, 0, 103)),
            (Color.FromArgb(64, 64, 64),   Color.FromArgb(20, 20, 20)),
            (Color.FromArgb(17, 153, 142), Color.FromArgb(56, 249, 196)),
            (Color.FromArgb(252, 0, 255),  Color.FromArgb(0, 219, 222))
        };

        public MainWindow()
        {
            InitializeComponent();
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

                // OPTIMIZATION: Generate videos in parallel (4 at a time)
                int maxParallel = Math.Min(4, Environment.ProcessorCount);
                var tasks = new List<Task>();
                int completed = 0;

                for (int i = 0; i < quotes.Count; i++)
                {
                    int index = i + 1;
                    string quote = quotes[i];
                    string outputFilePath = Path.Combine(tempDir, $"{index}.mp4");

                    tasks.Add(Task.Run(() =>
                    {
                        GenerateHighCtrVideo(quote, index, outputFilePath);
                        int current = System.Threading.Interlocked.Increment(ref completed);
                        Dispatcher.Invoke(() =>
                        {
                            UpdateStatus($"🎬 Generated {current}/{quotes.Count} shorts...");
                            UpdateProgress((double)current / quotes.Count * 100);
                        });
                    }));

                    // Limit parallel tasks
                    if (tasks.Count >= maxParallel)
                    {
                        await Task.WhenAny(tasks);
                        tasks.RemoveAll(t => t.IsCompleted);
                    }
                }

                await Task.WhenAll(tasks);

                UpdateStatus("📦 Packaging videos into ZIP archive...");
                await Task.Run(() => ZipFile.CreateFromDirectory(tempDir, zipDestination));

                UpdateProgress(100);
                UpdateStatus($"✅ Success! {quotes.Count} monetizable shorts saved.");
                MessageBox.Show($"Generated {quotes.Count} high-CTR YouTube Shorts!", "Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred:\n\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatus("❌ Failed. Check error message.");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    try { Directory.Delete(tempDir, true); } catch { }
                SetUiBusy(false);
            }
        }

        private void GenerateHighCtrVideo(string quoteText, int index, string outputPath)
        {
            string framesDir = Path.Combine(Path.GetTempPath(), $"frames_{index}_{Guid.NewGuid():N}");
            Directory.CreateDirectory(framesDir);

            try
            {
                var palette = Palettes[index % Palettes.Count];
                var particles = GenerateParticles(40);

                for (int f = 0; f < TotalFrames; f++)
                {
                    using (Bitmap bmp = new Bitmap(VideoWidth, VideoHeight, PixelFormat.Format24bppRgb))
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

                        DrawAnimatedGradient(g, bmp.Width, bmp.Height, palette.c1, palette.c2, f);
                        DrawParticles(g, particles, f, bmp.Width, bmp.Height);
                        DrawVignette(g, bmp.Width, bmp.Height);

                        float zoom = 1.0f + (0.05f * (f / (float)TotalFrames));
                        g.ScaleTransform(zoom, zoom, MatrixOrder.Append);
                        g.TranslateTransform(-VideoWidth * (zoom - 1) / 2, -VideoHeight * (zoom - 1) / 2, MatrixOrder.Append);

                        float textAlpha = CalculateTextAlpha(f);
                        float textScale = CalculateTextScale(f);

                        DrawAnimatedText(g, quoteText, bmp.Width, bmp.Height, f, textAlpha, textScale);

                        // OPTIMIZATION: Use BMP format (10x faster than PNG, no compression overhead)
                        bmp.Save(Path.Combine(framesDir, $"frame_{f:D4}.bmp"), ImageFormat.Bmp);
                    }
                }

                EncodeToMp4Premium(framesDir, outputPath);
            }
            finally
            {
                if (Directory.Exists(framesDir))
                    try { Directory.Delete(framesDir, true); } catch { }
            }
        }

        private void EncodeToMp4Premium(string framesDir, string outputPath)
        {
            string inputPattern = Path.Combine(framesDir, "frame_%04d.bmp");

            string args = $"-y -framerate {Fps} -i \"{inputPattern}\" " +
                          $"-c:v libx264 -profile:v high -level 4.1 " +
                          $"-pix_fmt yuv420p " +
                          $"-preset veryfast " + // OPTIMIZATION: veryfast preset (3x faster encoding)
                          $"-crf 20 " +          // Slightly lower quality for speed
                          $"-r {Fps} " +
                          $"-movflags +faststart " +
                          $"-vf \"scale=1080:1920\" " +
                          $"\"{outputPath}\"";

            var psi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (var process = Process.Start(psi))
            {
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    string error = process.StandardError.ReadToEnd();
                    throw new Exception($"FFmpeg encoding failed: {error}");
                }
            }
        }

        #region Animation Calculations

        private float CalculateTextAlpha(int frame)
        {
            float t = frame / (float)Fps;
            if (t < 1.5f) return t / 1.5f;
            if (t > 11.0f) return (12.0f - t) / 1.0f;
            return 1.0f;
        }

        private float CalculateTextScale(int frame)
        {
            float t = frame / (float)Fps;
            if (t < 1.5f)
            {
                float progress = t / 1.5f;
                float eased = 1 - (float)Math.Pow(1 - progress, 3);
                return 0.85f + (0.15f * eased);
            }
            return 1.0f;
        }

        #endregion

        #region Visual Rendering

        private void DrawAnimatedGradient(Graphics g, int width, int height, Color c1, Color c2, int frame)
        {
            float angle = 45 + (frame * 0.15f);
            double radians = angle * Math.PI / 180.0;
            float dx = (float)Math.Cos(radians) * width;
            float dy = (float)Math.Sin(radians) * height;

            using (var brush = new LinearGradientBrush(
                new PointF(width / 2 - dx / 2, height / 2 - dy / 2),
                new PointF(width / 2 + dx / 2, height / 2 + dy / 2),
                c1, c2))
            {
                g.FillRectangle(brush, 0, 0, width, height);
            }
        }

        private class Particle
        {
            public float X, Y, Radius, Speed, Opacity;
            public Color Color;
        }

        private List<Particle> GenerateParticles(int count)
        {
            var rng = new Random();
            var list = new List<Particle>();
            for (int i = 0; i < count; i++)
            {
                list.Add(new Particle
                {
                    X = (float)(rng.NextDouble() * VideoWidth),
                    Y = (float)(rng.NextDouble() * VideoHeight),
                    Radius = 20 + (float)(rng.NextDouble() * 80),
                    Speed = 0.3f + (float)(rng.NextDouble() * 1.2f),
                    Opacity = 0.15f + (float)(rng.NextDouble() * 0.35f),
                    Color = Color.White
                });
            }
            return list;
        }

        private void DrawParticles(Graphics g, List<Particle> particles, int frame, int width, int height)
        {
            foreach (var p in particles)
            {
                float y = (p.Y - frame * p.Speed) % (height + p.Radius * 2);
                if (y < -p.Radius) y += height + p.Radius * 2;

                float sway = (float)Math.Sin((frame + p.X) * 0.02) * 15;
                float x = p.X + sway;

                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(x - p.Radius, y - p.Radius, p.Radius * 2, p.Radius * 2);
                    using (var pgb = new PathGradientBrush(path))
                    {
                        pgb.CenterColor = Color.FromArgb((int)(p.Opacity * 255), p.Color);
                        pgb.SurroundColors = new[] { Color.FromArgb(0, p.Color) };
                        g.FillPath(pgb, path);
                    }
                }
            }
        }

        private void DrawVignette(Graphics g, int width, int height)
        {
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(-width / 3, -height / 3, width + width / 1.5f, height + height / 1.5f);
                using (var pgb = new PathGradientBrush(path))
                {
                    pgb.CenterColor = Color.FromArgb(0, 0, 0, 0);
                    pgb.SurroundColors = new[] { Color.FromArgb(140, 0, 0, 0) };
                    g.FillPath(pgb, path);
                }
            }
        }

        private void DrawAnimatedText(Graphics g, string text, int width, int height, int frame, float alpha, float scale)
        {
            if (alpha <= 0.01f) return;

            float baseFontSize = text.Length < 50 ? 96 : text.Length < 100 ? 78 : text.Length < 160 ? 62 : 52;
            float fontSize = baseFontSize * scale;

            using (var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var format = new StringFormat())
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                format.Trimming = StringTrimming.Word;
                format.FormatFlags = StringFormatFlags.LineLimit;

                var textArea = new RectangleF(
                    SafeMarginLeft,
                    SafeMarginTop,
                    width - SafeMarginLeft - SafeMarginRight,
                    height - SafeMarginTop - SafeMarginBottom);

                int alphaInt = (int)(alpha * 255);

                for (int glow = 20; glow > 0; glow -= 5)
                {
                    using (var glowBrush = new SolidBrush(Color.FromArgb((int)(alpha * 60), 255, 255, 255)))
                    {
                        var glowRect = new RectangleF(textArea.X - glow, textArea.Y - glow,
                                                      textArea.Width + glow * 2, textArea.Height + glow * 2);
                        g.DrawString(text, font, glowBrush, glowRect, format);
                    }
                }

                using (var strokeBrush = new SolidBrush(Color.FromArgb(alphaInt, 0, 0, 0)))
                {
                    int stroke = 10;
                    for (int dx = -stroke; dx <= stroke; dx += 2)
                        for (int dy = -stroke; dy <= stroke; dy += 2)
                        {
                            if (dx * dx + dy * dy > stroke * stroke) continue;
                            var r = new RectangleF(textArea.X + dx, textArea.Y + dy, textArea.Width, textArea.Height);
                            g.DrawString(text, font, strokeBrush, r, format);
                        }
                }

                using (var textBrush = new SolidBrush(Color.FromArgb(alphaInt, 255, 255, 255)))
                {
                    g.DrawString(text, font, textBrush, textArea, format);
                }
            }
        }

        #endregion

        #region UI Helpers

        private void SetUiBusy(bool isBusy)
        {
            btnGenerate.IsEnabled = !isBusy;
            txtQuotes.IsReadOnly = isBusy;
            progressBar.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
            if (!isBusy) progressBar.Value = 0;
        }

        private void UpdateStatus(string message) => Dispatcher.Invoke(() => txtStatus.Text = message);
        private void UpdateProgress(double percentage) => Dispatcher.Invoke(() => progressBar.Value = percentage);
        private string TruncateText(string text, int maxLength) =>
            text.Length <= maxLength ? text : text.Substring(0, maxLength);

        #endregion
    }
}
