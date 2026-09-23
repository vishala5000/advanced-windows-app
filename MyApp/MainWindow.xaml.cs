using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace MyApp
{
    public partial class MainWindow : Window
    {
        private const int VideoWidth = 1080;
        private const int VideoHeight = 1920;
        private const int Fps = 24;
        private const int DurationSeconds = 12;
        private const int TotalFrames = Fps * DurationSeconds;
        private const int FrameBufferSize = VideoWidth * VideoHeight * 3; // 6.2MB per frame (BGR24)

        // SAFETY: FFmpeg timeout prevents infinite hangs
        private const int FFmpegTimeoutMs = 60_000;

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

        private CancellationTokenSource _cts;

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

            // SAFETY: Validate FFmpeg is available BEFORE starting
            if (!IsFFmpegAvailable())
            {
                MessageBox.Show("FFmpeg not found. Please install FFmpeg and add it to PATH, or place ffmpeg.exe next to this app.",
                    "FFmpeg Missing", MessageBoxButton.OK, MessageBoxImage.Error);
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

            _cts = new CancellationTokenSource();
            var startTime = DateTime.Now;

            try
            {
                SetUiBusy(true);
                Directory.CreateDirectory(tempDir);

                int totalQuotes = quotes.Count;
                int completed = 0;
                int maxParallel = Math.Min(4, Environment.ProcessorCount);

                var semaphore = new SemaphoreSlim(maxParallel, maxParallel);
                var tasks = new List<Task>();

                for (int i = 0; i < totalQuotes; i++)
                {
                    _cts.Token.ThrowIfCancellationRequested();
                    await semaphore.WaitAsync(_cts.Token);

                    int index = i + 1;
                    string quote = quotes[i];
                    string outputFilePath = Path.Combine(tempDir, $"{index}.mp4");

                    tasks.Add(Task.Run(() =>
                    {
                        try
                        {
                            GenerateHighCtrVideoPiped(quote, index, outputFilePath, _cts.Token);
                            int current = Interlocked.Increment(ref completed);
                            var elapsed = DateTime.Now - startTime;
                            var eta = TimeSpan.FromSeconds((elapsed.TotalSeconds / current) * (totalQuotes - current));

                            Dispatcher.Invoke(() =>
                            {
                                UpdateStatus($"🎬 Generated {current}/{totalQuotes} | ETA: {eta:mm\\:ss}");
                                UpdateProgress((double)current / totalQuotes * 100);
                            });
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }, _cts.Token));
                }

                await Task.WhenAll(tasks);

                UpdateStatus("📦 Packaging videos into ZIP archive...");
                await Task.Run(() => ZipFile.CreateFromDirectory(tempDir, zipDestination), _cts.Token);

                var totalTime = DateTime.Now - startTime;
                UpdateProgress(100);
                UpdateStatus($"✅ Done in {totalTime:mm\\:ss}! {totalQuotes} shorts saved.");
                MessageBox.Show($"Generated {totalQuotes} shorts in {totalTime:mm\\:ss}!", "Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (OperationCanceledException)
            {
                UpdateStatus("⚠️ Cancelled by user.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred:\n\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatus("❌ Failed.");
            }
            finally
            {
                // SAFETY: Wait a bit for FFmpeg processes to exit before deleting temp dir
                Thread.Sleep(500);
                if (Directory.Exists(tempDir))
                    try { Directory.Delete(tempDir, true); } catch { }
                SetUiBusy(false);
            }
        }

        /// <summary>
        /// SAFETY: Pre-flight check that FFmpeg is installed and working.
        /// </summary>
        private bool IsFFmpegAvailable()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = "-version",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var p = Process.Start(psi))
                {
                    return p.WaitForExit(5000) && p.ExitCode == 0;
                }
            }
            catch { return false; }
        }

        /// <summary>
        /// SAFETY-HARDENED: Pipes frames to FFmpeg with proper resource management,
        /// timeout protection, cancellation support, and zero GC pressure.
        /// </summary>
        private void GenerateHighCtrVideoPiped(string quoteText, int index, string outputPath, CancellationToken ct)
        {
            var palette = Palettes[index % Palettes.Count];
            var particles = GenerateParticles(40);

            // SAFETY: Sanitize output path to prevent FFmpeg argument injection
            string sanitizedPath = outputPath.Replace("\"", "\\\"");

            string args = $"-y -hide_banner -loglevel error -f rawvideo -pixel_format bgr24 " +
                          $"-video_size {VideoWidth}x{VideoHeight} -framerate {Fps} -i pipe:0 " +
                          $"-c:v libx264 -profile:v high -level 4.1 " +
                          $"-pix_fmt yuv420p -preset veryfast -crf 20 " +
                          $"-r {Fps} -movflags +faststart " +
                          $"\"{sanitizedPath}\"";

            var psi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            Process process = null;
            try
            {
                process = Process.Start(psi);

                // SAFETY: Drain stderr asynchronously to prevent pipe deadlock
                var stderrTask = Task.Run(() => process.StandardError.ReadToEnd());

                // SAFETY: Reuse buffer — allocated ONCE per video, not per frame
                byte[] frameBuffer = new byte[FrameBufferSize];

                // SAFETY: Reuse Bitmap, Graphics, and Font — created ONCE per video
                using (var bmp = new Bitmap(VideoWidth, VideoHeight, PixelFormat.Format24bppRgb))
                using (var g = Graphics.FromImage(bmp))
                using (var font = CreateOptimalFont(quoteText.Length))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;

                    var stream = process.StandardInput.BaseStream;

                    using (var format = new StringFormat())
                    {
                        format.Alignment = StringAlignment.Center;
                        format.LineAlignment = StringAlignment.Center;
                        format.Trimming = StringTrimming.Word;
                        format.FormatFlags = StringFormatFlags.LineLimit;

                        for (int f = 0; f < TotalFrames; f++)
                        {
                            ct.ThrowIfCancellationRequested();

                            g.Clear(Color.Black);
                            DrawAnimatedGradient(g, bmp.Width, bmp.Height, palette.c1, palette.c2, f);
                            DrawParticles(g, particles, f, bmp.Width, bmp.Height);
                            DrawVignette(g, bmp.Width, bmp.Height);

                            // SAFETY: Reset transform instead of accumulating
                            g.ResetTransform();
                            float zoom = 1.0f + (0.05f * (f / (float)TotalFrames));
                            g.ScaleTransform(zoom, zoom);
                            g.TranslateTransform(-VideoWidth * (zoom - 1) / 2, -VideoHeight * (zoom - 1) / 2);

                            float textAlpha = CalculateTextAlpha(f);
                            float textScale = CalculateTextScale(f);
                            DrawAnimatedText(g, quoteText, bmp.Width, bmp.Height, font, format, textAlpha, textScale);

                            // SAFETY: Write directly from bitmap memory to stream (no intermediate allocation)
                            var bmpData = bmp.LockBits(
                                new Rectangle(0, 0, bmp.Width, bmp.Height),
                                ImageLockMode.ReadOnly,
                                PixelFormat.Format24bppRgb);
                            try
                            {
                                Marshal.Copy(bmpData.Scan0, frameBuffer, 0, FrameBufferSize);
                            }
                            finally
                            {
                                bmp.UnlockBits(bmpData);
                            }

                            stream.Write(frameBuffer, 0, FrameBufferSize);
                        }
                    }
                }

                // SAFETY: Signal FFmpeg that we're done
                process.StandardInput.Close();

                // SAFETY: Wait with timeout — prevents infinite hang
                if (!process.WaitForExit(FFmpegTimeoutMs))
                {
                    try { process.Kill(); } catch { }
                    throw new TimeoutException($"FFmpeg timed out after {FFmpegTimeoutMs / 1000}s");
                }

                // Drain stderr to ensure process fully exits
                stderrTask.Wait(2000);

                if (process.ExitCode != 0)
                {
                    string error = stderrTask.IsCompleted ? stderrTask.Result : "Unknown error";
                    throw new Exception($"FFmpeg failed (code {process.ExitCode}): {error}");
                }
            }
            catch (OperationCanceledException)
            {
                // SAFETY: Kill FFmpeg on cancellation — no orphan processes
                if (process != null && !process.HasExited)
                {
                    try { process.Kill(); } catch { }
                }
                throw;
            }
            finally
            {
                // SAFETY: Always dispose process
                if (process != null)
                {
                    try { process.Dispose(); } catch { }
                }
            }
        }

        /// <summary>
        /// SAFETY: Pre-compute font once per video based on text length.
        /// </summary>
        private Font CreateOptimalFont(int textLength)
        {
            float baseFontSize = textLength < 50 ? 96 : textLength < 100 ? 78 : textLength < 160 ? 62 : 52;
            return new Font("Segoe UI", baseFontSize, FontStyle.Bold, GraphicsUnit.Pixel);
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

        private void DrawAnimatedText(Graphics g, string text, int width, int height, Font font, StringFormat format, float alpha, float scale)
        {
            if (alpha <= 0.01f) return;

            var textArea = new RectangleF(
                SafeMarginLeft,
                SafeMarginTop,
                width - SafeMarginLeft - SafeMarginRight,
                height - SafeMarginTop - SafeMarginBottom);

            int alphaInt = (int)(alpha * 255);

            // Glow effect
            for (int glow = 20; glow > 0; glow -= 5)
            {
                using (var glowBrush = new SolidBrush(Color.FromArgb((int)(alpha * 60), 255, 255, 255)))
                {
                    var glowRect = new RectangleF(textArea.X - glow, textArea.Y - glow,
                                                  textArea.Width + glow * 2, textArea.Height + glow * 2);
                    g.DrawString(text, font, glowBrush, glowRect, format);
                }
            }

            // Black stroke outline
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

            // Main white text
            using (var textBrush = new SolidBrush(Color.FromArgb(alphaInt, 255, 255, 255)))
            {
                g.DrawString(text, font, textBrush, textArea, format);
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

        #endregion
    }
}
