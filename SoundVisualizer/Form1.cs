using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;

namespace SoundVisualizer
{
    public partial class Form1 : Form
    {
        private WasapiLoopbackCapture? capture;
        private BufferedWaveProvider? waveProvider;
        private System.Windows.Forms.Timer? renderTimer;
        private float[] fftBuffer = new float[8192];
        private NAudio.Dsp.Complex[] fftComplex = new NAudio.Dsp.Complex[4096];
        private float[] spectrumData = new float[64];
        private float[] peakData = new float[64];
        private int[] peakHoldTime = new int[64];
        private float[] waveformData = new float[2048];
        private int waveformIndex = 0;
        private const int BarCount = 64;
        private const int FftSize = 8192;
        private const int PeakHoldFrames = 20; // Hold peak for ~20 frames
        private const float PeakFallSpeed = 0.01f;
        private float maxAmplitude = 0.01f; // Track maximum amplitude for auto-scaling
        private MMDeviceEnumerator? deviceEnumerator;
        private bool isFullScreen = false;
        private FormWindowState previousWindowState;
        private FormBorderStyle previousBorderStyle;
        private Rectangle previousBounds;
        private float backgroundOffset = 0;
        private ColorPalette currentPalette = ColorPalette.Classic;

        public Form1()
        {
            InitializeComponent();
            InitializeVisualizer();
        }

        private enum ColorPalette
        {
            Classic,
            Ocean,
            Fire,
            Purple,
            Neon,
            Sunset,
            Matrix,
            Monochrome
        }

        private void InitializeVisualizer()
        {
            this.Text = "Audio Spectrum Analyzer";
            this.Size = new Size(1000, 600);
            this.BackColor = Color.Black;
            this.DoubleBuffered = true;
            this.KeyPreview = true;

            // Add keyboard and mouse handlers for fullscreen
            this.KeyDown += Form1_KeyDown;
            this.DoubleClick += (s, e) => ToggleFullScreen();

            // Load audio devices
            deviceEnumerator = new MMDeviceEnumerator();
            LoadAudioDevices();

            // Setup render timer
            renderTimer = new System.Windows.Forms.Timer
            {
                Interval = 16 // ~60 FPS
            };
            renderTimer.Tick += (s, e) => this.Invalidate();
            renderTimer.Start();

            this.FormClosing += (s, e) =>
            {
                renderTimer?.Stop();
                StopCapture();
                deviceEnumerator?.Dispose();
            };
        }

        private void LoadAudioDevices()
        {
            if (deviceEnumerator == null) return;

            deviceComboBox.Items.Clear();
            
            // Add loopback devices (system audio output)
            var devices = deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            foreach (var device in devices)
            {
                deviceComboBox.Items.Add(new AudioDeviceItem
                {
                    Device = device,
                    Name = device.FriendlyName
                });
            }

            if (deviceComboBox.Items.Count > 0)
            {
                deviceComboBox.SelectedIndex = 0;
            }

            // Load color palettes
            paletteComboBox.Items.AddRange(Enum.GetNames(typeof(ColorPalette)));
            paletteComboBox.SelectedIndex = 0;
        }

        private void paletteComboBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (paletteComboBox.SelectedItem is string paletteName)
            {
                currentPalette = (ColorPalette)Enum.Parse(typeof(ColorPalette), paletteName);
            }
        }

        private void deviceComboBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (deviceComboBox.SelectedItem is AudioDeviceItem item)
            {
                StartCapture(item.Device);
            }
        }

        private void StartCapture(MMDevice device)
        {
            StopCapture();

            try
            {
                capture = new WasapiLoopbackCapture(device);
                waveProvider = new BufferedWaveProvider(capture.WaveFormat)
                {
                    BufferLength = FftSize * 4,
                    DiscardOnBufferOverflow = true
                };

                capture.DataAvailable += OnDataAvailable;
                capture.StartRecording();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to start audio capture: {ex.Message}\n\nThe device may be in exclusive mode or unavailable.", 
                    "Audio Capture Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                StopCapture();
            }
        }

        private void StopCapture()
        {
            try
            {
                if (capture != null)
                {
                    capture.DataAvailable -= OnDataAvailable;
                    capture.StopRecording();
                    capture.Dispose();
                    capture = null;
                }
            }
            catch
            {
                // Ignore disposal errors
            }
            waveProvider = null;
            Array.Clear(spectrumData, 0, spectrumData.Length);
        }

        private void Form1_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F11 || e.KeyCode == Keys.Escape)
            {
                ToggleFullScreen();
                e.Handled = true;
            }
        }

        private void ToggleFullScreen()
        {
            if (!isFullScreen)
            {
                // Save current state
                previousWindowState = this.WindowState;
                previousBorderStyle = this.FormBorderStyle;
                previousBounds = this.Bounds;

                // Enter fullscreen
                this.WindowState = FormWindowState.Normal;
                this.FormBorderStyle = FormBorderStyle.None;
                this.Bounds = Screen.FromControl(this).Bounds;
                this.TopMost = true;
                deviceComboBox.Visible = false;
                paletteComboBox.Visible = false;
                isFullScreen = true;
            }
            else
            {
                // Exit fullscreen
                this.TopMost = false;
                this.FormBorderStyle = previousBorderStyle;
                this.WindowState = previousWindowState;
                this.Bounds = previousBounds;
                deviceComboBox.Visible = true;
                paletteComboBox.Visible = true;
                isFullScreen = false;
            }
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            try
            {
                waveProvider?.AddSamples(e.Buffer, 0, e.BytesRecorded);
                ProcessAudio();
            }
            catch
            {
                // Ignore processing errors
            }
        }

        private void ProcessAudio()
        {
            if (waveProvider == null || waveProvider.BufferedBytes < FftSize * 4) return;

            // Read audio data
            byte[] buffer = new byte[FftSize * 4];
            int bytesRead = waveProvider.Read(buffer, 0, buffer.Length);
            if (bytesRead < FftSize * 4) return;

            // Convert bytes to float samples
            for (int i = 0; i < FftSize / 2; i++)
            {
                int sampleIndex = i * 4;
                if (sampleIndex + 3 < buffer.Length)
                {
                    fftBuffer[i] = BitConverter.ToSingle(buffer, sampleIndex);
                    
                    // Store waveform data (subsample for display)
                    if (i % 2 == 0 && waveformIndex < waveformData.Length)
                    {
                        waveformData[waveformIndex] = fftBuffer[i];
                        waveformIndex++;
                        if (waveformIndex >= waveformData.Length)
                            waveformIndex = 0;
                    }
                }
            }

            // Apply Hamming window
            for (int i = 0; i < FftSize / 2; i++)
            {
                fftBuffer[i] *= (float)(0.54 - 0.46 * Math.Cos(2 * Math.PI * i / (FftSize / 2)));
            }

            // Prepare complex array for FFT
            for (int i = 0; i < FftSize / 2; i++)
            {
                fftComplex[i] = new NAudio.Dsp.Complex { X = fftBuffer[i], Y = 0 };
            }

            // Perform FFT
            FastFourierTransform.FFT(true, (int)Math.Log(FftSize / 2, 2), fftComplex);

            // Calculate spectrum data (logarithmic scale)
            for (int i = 0; i < BarCount; i++)
            {
                float sum = 0;
                int start = (int)Math.Pow(2, i * 10.0 / BarCount);
                int end = (int)Math.Pow(2, (i + 1) * 10.0 / BarCount);
                
                if (end > fftComplex.Length) end = fftComplex.Length;
                if (start >= end) start = Math.Max(0, end - 1);

                int count = 0;
                for (int j = start; j < end && j < fftComplex.Length; j++)
                {
                    float magnitude = (float)Math.Sqrt(fftComplex[j].X * fftComplex[j].X + fftComplex[j].Y * fftComplex[j].Y);
                    sum += magnitude;
                    count++;
                }

                if (count > 0)
                {
                    float average = sum / count;
                    // Smooth transition and amplify
                    spectrumData[i] = spectrumData[i] * 0.7f + average * 0.3f;
                    
                    // Update peak
                    if (spectrumData[i] > peakData[i])
                    {
                        peakData[i] = spectrumData[i];
                        peakHoldTime[i] = PeakHoldFrames;
                    }
                    else if (peakHoldTime[i] > 0)
                    {
                        peakHoldTime[i]--;
                    }
                    else
                    {
                        peakData[i] = Math.Max(0, peakData[i] - PeakFallSpeed);
                    }
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            // Draw animated gradient background
            DrawBackground(g);

            // Draw waveform
            DrawWaveform(g);

            int barWidth = this.ClientSize.Width / BarCount;
            int maxHeight = this.ClientSize.Height - 40;

            // Find current maximum value for auto-scaling
            float currentMax = 0;
            for (int i = 0; i < BarCount; i++)
            {
                if (spectrumData[i] > currentMax)
                    currentMax = spectrumData[i];
            }

            // Smoothly adjust maxAmplitude (with slow decay to allow full height usage)
            if (currentMax > maxAmplitude)
            {
                maxAmplitude = currentMax;
            }
            else
            {
                // Slow decay: let it drop down gradually to match current levels
                maxAmplitude = maxAmplitude * 0.995f + currentMax * 0.005f;
            }

            // Prevent division by zero
            if (maxAmplitude < 0.001f) maxAmplitude = 0.001f;

            for (int i = 0; i < BarCount; i++)
            {
                float value = spectrumData[i];
                // Normalize based on current maximum, using ~90% of available height
                float normalizedValue = Math.Min((value / maxAmplitude) * 0.9f, 1.0f);
                int barHeight = Math.Max(2, (int)(normalizedValue * maxHeight));

                int x = i * barWidth;
                int y = this.ClientSize.Height - barHeight - 20;

                // Get color based on selected palette
                Color barColor = GetPaletteColor(normalizedValue, i, BarCount);

                using (SolidBrush brush = new SolidBrush(barColor))
                {
                    g.FillRectangle(brush, x + 2, y, barWidth - 4, barHeight);
                }

                // Draw border
                if (barHeight > 2)
                {
                    using (Pen pen = new Pen(Color.FromArgb(128, 255, 255, 255), 1))
                    {
                        g.DrawRectangle(pen, x + 2, y, barWidth - 4, barHeight);
                    }
                }

                // Draw peak line
                float peakValue = peakData[i];
                float normalizedPeak = Math.Min((peakValue / maxAmplitude) * 0.9f, 1.0f);
                int peakHeight = (int)(normalizedPeak * maxHeight);
                if (peakHeight > 2)
                {
                    int peakY = this.ClientSize.Height - peakHeight - 20;
                    
                    // Peak line with fade effect based on hold time
                    float fadeAlpha = peakHoldTime[i] > 0 ? 255 : Math.Max(100, 255 * (peakData[i] / (value + 0.001f)));
                    fadeAlpha = Math.Min(255, Math.Max(0, fadeAlpha)); // Clamp to 0-255
                    Color peakColor = Color.FromArgb((int)fadeAlpha, Color.Cyan);
                    
                    using (Pen peakPen = new Pen(peakColor, 2))
                    {
                        g.DrawLine(peakPen, x + 2, peakY, x + barWidth - 2, peakY);
                    }
                }
            }

            // Draw title
            using (Font font = new Font("Arial", 14, FontStyle.Bold))
            using (SolidBrush brush = new SolidBrush(Color.Lime))
            {
                string deviceName = deviceComboBox.SelectedItem is AudioDeviceItem item ? item.Name : "No Device";
                string paletteName = currentPalette.ToString();
                int titleY = isFullScreen ? 10 : 45;
                g.DrawString($"Spectrum Analyzer - {deviceName} | Palette: {paletteName}", font, brush, 10, titleY);
            }

            // Show fullscreen hint
            if (!isFullScreen)
            {
                using (Font font = new Font("Arial", 9))
                using (SolidBrush brush = new SolidBrush(Color.Gray))
                {
                    g.DrawString("Press F11 or double-click for fullscreen", font, brush, 10, this.ClientSize.Height - 25);
                }
            }
        }

        private void DrawWaveform(Graphics g)
        {
            if (waveformData.Length == 0) return;

            int centerY = this.ClientSize.Height / 2;
            int waveHeight = Math.Min(300, this.ClientSize.Height / 2); // Increased from 150 to 300
            float xStep = (float)this.ClientSize.Width / waveformData.Length;

            using (Pen waveformPen = new Pen(Color.FromArgb(80, 0, 255, 255), 2))
            using (Pen waveformPen2 = new Pen(Color.FromArgb(40, 0, 200, 255), 3))
            {
                // Draw outer glow
                for (int i = 0; i < waveformData.Length - 1; i++)
                {
                    int x1 = (int)(i * xStep);
                    int x2 = (int)((i + 1) * xStep);
                    
                    int y1 = centerY + (int)(waveformData[i] * waveHeight * 3); // 3x amplification
                    int y2 = centerY + (int)(waveformData[i + 1] * waveHeight * 3);
                    
                    g.DrawLine(waveformPen2, x1, y1, x2, y2);
                }

                // Draw main waveform
                for (int i = 0; i < waveformData.Length - 1; i++)
                {
                    int x1 = (int)(i * xStep);
                    int x2 = (int)((i + 1) * xStep);
                    
                    int y1 = centerY + (int)(waveformData[i] * waveHeight * 3); // 3x amplification
                    int y2 = centerY + (int)(waveformData[i + 1] * waveHeight * 3);
                    
                    g.DrawLine(waveformPen, x1, y1, x2, y2);
                }
            }

            // Draw center line
            using (Pen centerLinePen = new Pen(Color.FromArgb(30, 255, 255, 255), 1))
            {
                g.DrawLine(centerLinePen, 0, centerY, this.ClientSize.Width, centerY);
            }
        }

        private void DrawBackground(Graphics g)
        {
            // Animated gradient background
            backgroundOffset += 0.5f;
            if (backgroundOffset > 360) backgroundOffset = 0;

            // Get background colors based on palette
            (Color bgColor1, Color bgColor2) = GetBackgroundColors();

            // Create gradient
            using (System.Drawing.Drawing2D.LinearGradientBrush brush = 
                new System.Drawing.Drawing2D.LinearGradientBrush(
                    this.ClientRectangle,
                    bgColor1,
                    bgColor2,
                    45f + (float)Math.Sin(backgroundOffset * Math.PI / 180) * 15))
            {
                g.FillRectangle(brush, this.ClientRectangle);
            }

            // Draw subtle grid pattern
            using (Pen gridPen = new Pen(Color.FromArgb(20, 255, 255, 255), 1))
            {
                int gridSize = 50;
                int offsetX = (int)(backgroundOffset % gridSize);
                int offsetY = (int)(backgroundOffset * 0.7f % gridSize);

                // Vertical lines
                for (int x = -gridSize + offsetX; x < this.ClientSize.Width; x += gridSize)
                {
                    g.DrawLine(gridPen, x, 0, x, this.ClientSize.Height);
                }

                // Horizontal lines
                for (int y = -gridSize + offsetY; y < this.ClientSize.Height; y += gridSize)
                {
                    g.DrawLine(gridPen, 0, y, this.ClientSize.Width, y);
                }
            }

            // Draw radial glow in center
            using (System.Drawing.Drawing2D.GraphicsPath path = new System.Drawing.Drawing2D.GraphicsPath())
            {
                int centerX = this.ClientSize.Width / 2;
                int centerY = this.ClientSize.Height / 2;
                int radius = Math.Min(this.ClientSize.Width, this.ClientSize.Height) / 2;

                path.AddEllipse(centerX - radius, centerY - radius, radius * 2, radius * 2);
                using (System.Drawing.Drawing2D.PathGradientBrush gradBrush = 
                    new System.Drawing.Drawing2D.PathGradientBrush(path))
                {
                    Color glowColor = GetCenterGlowColor();
                    gradBrush.CenterColor = glowColor;
                    gradBrush.SurroundColors = new[] { Color.FromArgb(0, 0, 0, 0) };
                    gradBrush.FocusScales = new System.Drawing.PointF(0.3f, 0.3f);
                    g.FillPath(gradBrush, path);
                }
            }
        }

        private Color GetPaletteColor(float normalizedValue, int barIndex, int totalBars)
        {
            switch (currentPalette)
            {
                case ColorPalette.Classic:
                    // Green to red gradient
                    int red = (int)(255 * normalizedValue);
                    int green = (int)(255 * (1 - normalizedValue));
                    return Color.FromArgb(red, green, 0);

                case ColorPalette.Ocean:
                    // Blue to cyan to white
                    int blue = (int)(255 * (1 - normalizedValue * 0.5f));
                    int oceanGreen = (int)(255 * normalizedValue);
                    int oceanRed = (int)(255 * Math.Max(0, normalizedValue - 0.7f) * 3.3f);
                    return Color.FromArgb(oceanRed, oceanGreen, blue);

                case ColorPalette.Fire:
                    // Dark red to orange to yellow to white
                    if (normalizedValue < 0.33f)
                    {
                        int fireRed = (int)(255 * (0.5f + normalizedValue * 1.5f));
                        return Color.FromArgb(fireRed, 0, 0);
                    }
                    else if (normalizedValue < 0.66f)
                    {
                        int fireGreen = (int)(255 * (normalizedValue - 0.33f) * 3);
                        return Color.FromArgb(255, fireGreen, 0);
                    }
                    else
                    {
                        int fireBlue = (int)(255 * (normalizedValue - 0.66f) * 3);
                        return Color.FromArgb(255, 255, fireBlue);
                    }

                case ColorPalette.Purple:
                    // Dark purple to bright purple to pink
                    int purpleRed = (int)(200 + 55 * normalizedValue);
                    int purpleGreen = (int)(50 * normalizedValue);
                    int purpleBlue = (int)(200 + 55 * (1 - normalizedValue * 0.5f));
                    return Color.FromArgb(purpleRed, purpleGreen, purpleBlue);

                case ColorPalette.Neon:
                    // Rainbow cycling through bars
                    float hue = ((float)barIndex / totalBars + normalizedValue * 0.3f) % 1.0f;
                    return ColorFromHSV(hue * 360, 1.0f, 0.5f + normalizedValue * 0.5f);

                case ColorPalette.Sunset:
                    // Orange to pink to purple
                    if (normalizedValue < 0.5f)
                    {
                        int sunsetGreen = (int)(100 + 155 * normalizedValue * 2);
                        return Color.FromArgb(255, sunsetGreen, 0);
                    }
                    else
                    {
                        int sunsetRed = 255;
                        int sunsetGreen = (int)(255 * (1 - (normalizedValue - 0.5f) * 2));
                        int sunsetBlue = (int)(200 * (normalizedValue - 0.5f) * 2);
                        return Color.FromArgb(sunsetRed, sunsetGreen, sunsetBlue);
                    }

                case ColorPalette.Matrix:
                    // Black to dark green to bright green
                    int matrixGreen = (int)(255 * normalizedValue);
                    int matrixRed = (int)(50 * normalizedValue);
                    return Color.FromArgb(matrixRed, matrixGreen, 0);

                case ColorPalette.Monochrome:
                    // Black to white
                    int mono = (int)(255 * normalizedValue);
                    return Color.FromArgb(mono, mono, mono);

                default:
                    return Color.Lime;
            }
        }

        private (Color, Color) GetBackgroundColors()
        {
            switch (currentPalette)
            {
                case ColorPalette.Classic:
                    return (Color.FromArgb(10, 10, 30), Color.FromArgb(20, 10, 40));
                case ColorPalette.Ocean:
                    return (Color.FromArgb(5, 10, 30), Color.FromArgb(10, 20, 50));
                case ColorPalette.Fire:
                    return (Color.FromArgb(20, 5, 5), Color.FromArgb(40, 10, 5));
                case ColorPalette.Purple:
                    return (Color.FromArgb(20, 5, 30), Color.FromArgb(30, 10, 50));
                case ColorPalette.Neon:
                    return (Color.FromArgb(10, 10, 10), Color.FromArgb(20, 20, 30));
                case ColorPalette.Sunset:
                    return (Color.FromArgb(30, 10, 10), Color.FromArgb(40, 15, 20));
                case ColorPalette.Matrix:
                    return (Color.FromArgb(0, 10, 0), Color.FromArgb(5, 20, 5));
                case ColorPalette.Monochrome:
                    return (Color.FromArgb(10, 10, 10), Color.FromArgb(20, 20, 20));
                default:
                    return (Color.FromArgb(10, 10, 30), Color.FromArgb(20, 10, 40));
            }
        }

        private Color GetCenterGlowColor()
        {
            switch (currentPalette)
            {
                case ColorPalette.Classic:
                    return Color.FromArgb(30, 50, 100, 150);
                case ColorPalette.Ocean:
                    return Color.FromArgb(30, 0, 150, 200);
                case ColorPalette.Fire:
                    return Color.FromArgb(30, 200, 50, 0);
                case ColorPalette.Purple:
                    return Color.FromArgb(30, 150, 50, 200);
                case ColorPalette.Neon:
                    return Color.FromArgb(30, 100, 100, 200);
                case ColorPalette.Sunset:
                    return Color.FromArgb(30, 200, 100, 50);
                case ColorPalette.Matrix:
                    return Color.FromArgb(30, 0, 150, 0);
                case ColorPalette.Monochrome:
                    return Color.FromArgb(30, 100, 100, 100);
                default:
                    return Color.FromArgb(30, 50, 100, 150);
            }
        }

        private Color ColorFromHSV(float hue, float saturation, float value)
        {
            int hi = Convert.ToInt32(Math.Floor(hue / 60)) % 6;
            float f = hue / 60 - (float)Math.Floor(hue / 60);

            value = value * 255;
            int v = Convert.ToInt32(value);
            int p = Convert.ToInt32(value * (1 - saturation));
            int q = Convert.ToInt32(value * (1 - f * saturation));
            int t = Convert.ToInt32(value * (1 - (1 - f) * saturation));

            switch (hi)
            {
                case 0: return Color.FromArgb(v, t, p);
                case 1: return Color.FromArgb(q, v, p);
                case 2: return Color.FromArgb(p, v, t);
                case 3: return Color.FromArgb(p, q, v);
                case 4: return Color.FromArgb(t, p, v);
                default: return Color.FromArgb(v, p, q);
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Don't call base to prevent flicker
        }

        private class AudioDeviceItem
        {
            public MMDevice Device { get; set; } = null!;
            public string Name { get; set; } = string.Empty;

            public override string ToString() => Name;
        }
    }
}
