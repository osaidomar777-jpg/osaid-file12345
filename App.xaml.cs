using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using OsaidFileFetcher.ViewModels;

namespace OsaidFileFetcher
{
    /// <summary>
    /// نقطة تشغيل التطبيق + شبكة أمان لأي خطأ غير متوقع
    /// (يُسجَّل الخطأ في ملف نصي بدل أن يُغلق التطبيق فجأة)
    /// </summary>
    public partial class App : Application
    {
        private static readonly object LogLock = new object();

        protected override void OnStartup(StartupEventArgs e)
        {
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            DispatcherUnhandledException += OnDispatcherUnhandledException;

            base.OnStartup(e);

            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var splash = new Views.SplashWindow();
            splash.Finished += (s, args) => ShowMainWindow();
            splash.Show();
        }

        private void ShowMainWindow()
        {
            var main = new MainWindow();
            MainWindow = main;
            main.Closed += (s, args) => Shutdown();
            main.Show();
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log(e.Exception, "Dispatcher");

            // بدلاً من نافذة منبثقة توقّف العمل: إشعار لطيف داخل الواجهة مع تسجيل الخطأ.
            ShowInAppNotice(e.Exception);

            // عدم إغلاق التطبيق بسبب خطأ في عنصر واجهة واحد
            e.Handled = true;
        }

        /// <summary>إظهار تنبيه غير مقاطع داخل الواجهة بدل MessageBox.</summary>
        private static void ShowInAppNotice(Exception exception)
        {
            try
            {
                MainViewModel viewModel = Current == null
                    ? null
                    : (Current.MainWindow as Window)?.DataContext as MainViewModel;

                if (viewModel == null) return;

                string message = exception == null ? string.Empty : exception.Message;
                if (message.Length > 110) message = message.Substring(0, 110) + "…";

                viewModel.ShowToast(
                    string.IsNullOrWhiteSpace(message)
                        ? "حدث تنبيه غير متوقع وتم تسجيله. يمكنك متابعة العمل."
                        : "تم تسجيل تنبيه غير متوقع: " + message,
                    ToastKind.Error);
            }
            catch
            {
                // التنبيه نفسه يجب ألا يسبب خطأً آخر
            }
        }

        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Log(e.ExceptionObject as Exception, "AppDomain");
        }

        public static string GetLogPath()
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Osaid File",
                "Logs");

            Directory.CreateDirectory(folder);
            return Path.Combine(folder, "error.log");
        }

        /// <summary>تسجيل خطأ من أي مكان في التطبيق (يُستخدم في سجل الدعم الفني).</summary>
        public static void LogError(Exception exception, string source)
        {
            Log(exception, source);
        }

        private static void Log(Exception exception, string source)
        {
            if (exception == null) return;

            try
            {
                lock (LogLock)
                {
                    var builder = new StringBuilder();
                    builder.AppendLine("========================================================");
                    builder.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  [" + source + "]");
                    builder.AppendLine(exception.ToString());
                    builder.AppendLine();

                    File.AppendAllText(GetLogPath(), builder.ToString(), Encoding.UTF8);
                }
            }
            catch
            {
                // لا شيء — تسجيل الأخطاء لا يجب أن يسبب خطأً آخر
            }
        }
    }
}
