using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Kadr
{
    static class Desktop
    {
        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--fps-worker")
            {
                FrameWorker.Run(args);
                return;
            }
            Native.SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath))
            using (var window = new RecorderWindow())
            {
                if (icon != null) window.Icon = icon;
                Application.Run(window);
            }
        }
    }

    sealed class RecorderWindow : Form
    {
        const string Page = "https://kadr.local/Screen%20Recorder.html";
        readonly WebView2 web = new WebView2 { Dock = DockStyle.Fill };
        readonly List<string> unavailableKeys = new List<string>();
        readonly FpsOverlay fps = new FpsOverlay();
        bool allowClose;
        bool checkingClose;
        bool changingRecording;

        public RecorderWindow()
        {
            Text = "Кадр";
            ClientSize = new Size(1140, 830);
            MinimumSize = new Size(580, 540);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(17, 18, 20);
            web.DefaultBackgroundColor = BackColor;
            Controls.Add(web);
            Shown += Initialize;
            FormClosing += CheckClose;
            FormClosed += (sender, args) => fps.Dispose();
            fps.StatusChanged += Report;
        }

        async void Initialize(object sender, EventArgs args)
        {
            try
            {
                string profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kadr", "WebView2");
                var environment = await CoreWebView2Environment.CreateAsync(null, profile);
                await web.EnsureCoreWebView2Async(environment);
                var core = web.CoreWebView2;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.AreBrowserAcceleratorKeysEnabled = false;
                core.SetVirtualHostNameToFolderMapping("kadr.local", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ui"),
                    CoreWebView2HostResourceAccessKind.Deny);
                core.NavigationStarting += (source, navigation) => navigation.Cancel = navigation.Uri != Page;
                core.NewWindowRequested += (source, window) => window.Handled = true;
                core.DownloadStarting += SaveDownload;
                core.NavigationCompleted += (source, navigation) =>
                {
                    if (!navigation.IsSuccess) Report("Не удалось загрузить интерфейс.");
                    else if (unavailableKeys.Count > 0)
                        Report("Заняты другими программами: " + string.Join(", ", unavailableKeys));
                };
                RegisterKey(9, Keys.F9);
                RegisterKey(10, Keys.F10);
                RegisterKey(11, Keys.F11);
                core.Navigate(Page);
            }
            catch (Exception error)
            {
                MessageBox.Show(this, "Не удалось запустить интерфейс. Проверьте установку Microsoft Edge WebView2.\n\n" + error.Message,
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                allowClose = true;
                Close();
            }
        }

        void RegisterKey(int id, Keys key)
        {
            const uint modifiers = 0x0001 | 0x0002 | 0x4000; // Alt, Ctrl, no repeat
            if (!Native.RegisterHotKey(Handle, id, modifiers, (uint)key))
                unavailableKeys.Add("Ctrl+Alt+" + key);
        }

        protected override void OnHandleDestroyed(EventArgs args)
        {
            for (int id = 9; id <= 11; id++) Native.UnregisterHotKey(Handle, id);
            base.OnHandleDestroyed(args);
        }

        void Report(string message)
        {
            if (!IsDisposed && web.CoreWebView2 != null) web.CoreWebView2.PostWebMessageAsString(message);
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0312)
            {
                switch (message.WParam.ToInt32())
                {
                    case 9: ToggleRecording(); return;
                    case 10: PauseRecording(); return;
                    case 11: fps.Toggle(Native.GetForegroundWindow(), Handle); return;
                }
            }
            base.WndProc(ref message);
        }

        async void ToggleRecording()
        {
            if (web.CoreWebView2 == null || changingRecording) return;
            changingRecording = true;
            try
            {
                if (await web.ExecuteScriptAsync("window.kadr?.isIdle()") != "true")
                {
                    await web.ExecuteScriptAsync("window.kadr?.hotkey('record')");
                    return;
                }
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                Activate();
                web.Focus();
                if (Native.GetForegroundWindow() != Handle)
                {
                    Report("Откройте «Кадр» и нажмите «Начать запись», чтобы выбрать источник.");
                    return;
                }
                // The physical hotkey initiates the standard WebView2 source picker.
                var result = await web.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
                    "{\"expression\":\"window.kadr.hotkey('record')\",\"userGesture\":true}");
                if (result.Contains("\"exceptionDetails\"")) Report("Нажмите «Начать запись», чтобы выбрать источник.");
            }
            catch (Exception error) { Report("Не удалось обработать клавишу записи: " + error.Message); }
            finally { changingRecording = false; }
        }

        async void PauseRecording()
        {
            if (web.CoreWebView2 == null) return;
            try { await web.ExecuteScriptAsync("window.kadr?.hotkey('pause')"); }
            catch (InvalidOperationException) { }
        }

        void SaveDownload(object sender, CoreWebView2DownloadStartingEventArgs args)
        {
            using (args.GetDeferral())
            {
                args.Handled = true;
                var operation = args.DownloadOperation;
                if (!operation.Uri.StartsWith("blob:https://kadr.local/", StringComparison.Ordinal))
                {
                    args.Cancel = true;
                    return;
                }
                using (var dialog = new SaveFileDialog())
                {
                    dialog.Title = "Сохранить запись";
                    dialog.FileName = Path.GetFileName(args.ResultFilePath);
                    dialog.Filter = dialog.FileName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
                        ? "Видео MP4|*.mp4" : "Видео WebM|*.webm";
                    dialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
                    if (dialog.ShowDialog(this) != DialogResult.OK)
                    {
                        args.Cancel = true;
                        return;
                    }
                    args.ResultFilePath = dialog.FileName;
                }
                operation.StateChanged += (source, state) =>
                {
                    if (operation.State == CoreWebView2DownloadState.Completed)
                    {
                        Report("saved:" + operation.Uri);
                        Report("Видео сохранено.");
                    }
                    else if (operation.State == CoreWebView2DownloadState.Interrupted)
                        Report("Не удалось сохранить видео. Попробуйте снова.");
                };
            }
        }

        async void CheckClose(object sender, FormClosingEventArgs args)
        {
            if (allowClose || web.CoreWebView2 == null || args.CloseReason == CloseReason.WindowsShutDown) return;
            args.Cancel = true;
            if (checkingClose) return;
            checkingClose = true;
            try
            {
                var state = await web.ExecuteScriptAsync("window.kadr?.closeState()");
                if (state == "2")
                {
                    MessageBox.Show(this, "Сначала завершите запись.", Text);
                    return;
                }
                if (state == "1" && MessageBox.Show(this, "В этой сессии есть несохранённые записи. Закрыть приложение?",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                allowClose = true;
                Close();
            }
            catch (InvalidOperationException) { }
            finally { checkingClose = false; }
        }
    }

    static class Native
    {
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll")]
        internal static extern bool UnregisterHotKey(IntPtr window, int id);
        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")]
        internal static extern bool SetProcessDPIAware();
    }
}
