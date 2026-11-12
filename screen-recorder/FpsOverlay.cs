using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Kadr
{
    sealed class FpsOverlay : IDisposable
    {
        readonly OverlayWindow window = new OverlayWindow();
        readonly Timer refresh = new Timer { Interval = 500 };
        readonly FrameRates rates = new FrameRates();
        NamedPipeServerStream pipe;
        Process worker;
        uint target;
        public event Action<string> StatusChanged;

        public FpsOverlay()
        {
            refresh.Tick += (sender, args) => Refresh();
        }

        public async void Toggle(IntPtr foreground, IntPtr recorder)
        {
            if (pipe != null)
            {
                Stop();
                StatusChanged("FPS-оверлей выключен.");
                return;
            }
            uint processId;
            Native.GetWindowThreadProcessId(foreground, out processId);
            if (foreground == IntPtr.Zero || foreground == recorder || processId == Process.GetCurrentProcess().Id)
            {
                StatusChanged("Переключитесь на окно игры и нажмите Ctrl+Alt+F11.");
                return;
            }
            string name = "Kadr-" + Guid.NewGuid().ToString("N");
            var connection = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            pipe = connection;
            rates.Clear();
            target = processId;
            try
            {
                worker = Process.Start(new ProcessStartInfo(Application.ExecutablePath,
                    "--fps-worker " + name + " " + processId.ToString(CultureInfo.InvariantCulture))
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                });
                StatusChanged("Ожидание кадров игры…");
                refresh.Start();
                var capture = Task.Run(() => ReadFrames(connection));
                var connected = await Task.WhenAny(capture, Task.Delay(10000));
                if (pipe == connection && connected != capture && !connection.IsConnected)
                    throw new TimeoutException("Не удалось подключиться к сборщику кадров.");
                await capture;
                if (pipe == connection)
                {
                    Stop();
                    StatusChanged("Сбор FPS завершён. Если кадров не было, проверьте окно игры и разрешение Windows.");
                }
            }
            catch (Exception error)
            {
                if (pipe == connection)
                {
                    Stop();
                    var nativeError = error as System.ComponentModel.Win32Exception;
                    StatusChanged(nativeError != null && nativeError.NativeErrorCode == 1223
                        ? "Разрешение Windows для FPS отменено."
                        : "Не удалось включить FPS: " + error.Message);
                }
            }
        }

        void ReadFrames(NamedPipeServerStream connection)
        {
            connection.WaitForConnection();
            using (var reader = new StreamReader(connection))
            {
                string line;
                while ((line = reader.ReadLine()) != null) rates.Add(line);
            }
        }

        void Refresh()
        {
            var foreground = Native.GetForegroundWindow();
            uint process;
            Native.GetWindowThreadProcessId(foreground, out process);
            if (process != target)
            {
                window.Hide();
                return;
            }
            var area = Screen.FromHandle(foreground).WorkingArea;
            window.Location = new Point(area.Right - window.Width - 20, area.Top + 20);
            var rate = rates.Read();
            window.Caption.Text = rate.HasValue ? rate.Value.ToString("0", CultureInfo.InvariantCulture) + " FPS" : "— FPS";
            if (!window.Visible) window.Show();
        }

        void Stop()
        {
            refresh.Stop();
            window.Hide();
            var connection = pipe;
            pipe = null;
            if (connection != null) connection.Dispose();
            if (worker != null)
            {
                worker.Dispose();
                worker = null;
            }
        }

        public void Dispose()
        {
            Stop();
            refresh.Dispose();
            window.Dispose();
        }
    }

    sealed class OverlayWindow : Form
    {
        public readonly Label Caption;

        public OverlayWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            ClientSize = new Size(120, 40);
            BackColor = Color.FromArgb(22, 26, 22);
            Opacity = 0.9;
            Caption = new Label
            {
                Dock = DockStyle.Fill,
                Text = "— FPS",
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(216, 252, 120),
                Font = new Font("Segoe UI", 14, FontStyle.Bold)
            };
            Controls.Add(Caption);
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                var settings = base.CreateParams;
                settings.ExStyle |= 0x20 | 0x80 | 0x08000000; // Transparent, tool window, no activation
                return settings;
            }
        }
    }

    sealed class FrameRates
    {
        readonly Dictionary<string, List<double>> samples = new Dictionary<string, List<double>>();
        int swapChain = -1;
        int frameTime = -1;
        DateTime lastFrame;
        double? lastRate;

        public void Clear()
        {
            lock (samples)
            {
                samples.Clear();
                swapChain = frameTime = -1;
                lastFrame = DateTime.MinValue;
                lastRate = null;
            }
        }

        public void Add(string line)
        {
            var columns = line.Split(',');
            lock (samples)
            {
                if (columns[0] == "Application")
                {
                    swapChain = Array.IndexOf(columns, "SwapChainAddress");
                    frameTime = Array.IndexOf(columns, "MsBetweenPresents");
                    return;
                }
                if (swapChain < 0 || frameTime < 0 || columns.Length <= Math.Max(swapChain, frameTime)) return;
                double duration;
                if (!double.TryParse(columns[frameTime], NumberStyles.Float, CultureInfo.InvariantCulture, out duration)
                    || duration <= 0 || duration > 2000 || double.IsNaN(duration)) return;
                List<double> chain;
                if (!samples.TryGetValue(columns[swapChain], out chain))
                {
                    chain = new List<double>();
                    samples.Add(columns[swapChain], chain);
                }
                chain.Add(duration);
                lastFrame = DateTime.UtcNow;
            }
        }

        public double? Read()
        {
            lock (samples)
            {
                var chain = samples.Values.OrderByDescending(values => values.Count).FirstOrDefault();
                if (chain != null) lastRate = 1000 / chain.Average();
                samples.Clear();
                return DateTime.UtcNow - lastFrame < TimeSpan.FromSeconds(2) ? lastRate : null;
            }
        }
    }

    static class FrameWorker
    {
        public static void Run(string[] args)
        {
            uint processId;
            Guid identifier;
            if (args.Length != 3 || !args[1].StartsWith("Kadr-", StringComparison.Ordinal)
                || !Guid.TryParseExact(args[1].Substring(5), "N", out identifier)
                || !uint.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out processId)
                || processId == 0) return;

            string name = args[1];
            try
            {
                using (var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification))
                {
                    client.Connect(5000);
                    var command = "--process_id " + processId.ToString(CultureInfo.InvariantCulture)
                        + " --v1_metrics --output_stdout --terminate_on_proc_exit --session_name " + name;
                    using (var monitor = Start(command))
                    using (var writer = new StreamWriter(client) { AutoFlush = true })
                    {
                        try
                        {
                            var disconnected = client.ReadAsync(new byte[1], 0, 1);
                            var copying = Task.Run(() =>
                            {
                                string line;
                                while ((line = monitor.StandardOutput.ReadLine()) != null) writer.WriteLine(line);
                            });
                            var completed = Task.WhenAny(disconnected, copying).GetAwaiter().GetResult();
                            completed.GetAwaiter().GetResult();
                        }
                        finally
                        {
                            if (!monitor.HasExited)
                            {
                                using (var stop = Start("--terminate_existing_session --session_name " + name))
                                    stop.WaitForExit(2000);
                                if (!monitor.WaitForExit(2000)) monitor.Kill();
                            }
                        }
                    }
                }
            }
            catch (IOException) { }
            catch (TimeoutException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }

        static Process Start(string command)
        {
            return Process.Start(new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PresentMon.exe"), command)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            });
        }
    }
}
