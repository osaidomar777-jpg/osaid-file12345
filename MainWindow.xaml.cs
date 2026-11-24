using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Threading.Tasks;
using OsaidFileFetcher.ViewModels;
using OsaidFileFetcher.Views;

namespace OsaidFileFetcher
{
    /// <summary>
    /// نافذة التحكم الرئيسية: شريط عنوان مخصص + شريط وحدات علوي + قائمة جانبية كاملة
    /// + شريط إجراءات سريعة + إشعارات عائمة. كل الأقسام تعمل على نفس ربط USB/AFC.
    /// </summary>
    public partial class MainWindow : Window
    {
        private const string HomeTitle = "لوحة التحكم";
        private const string HomeSubtitle = "نظرة سريعة على الجهاز المتصل ونقل الملفات";

        private const string DeviceInfoTitle = "معلومات الجهاز";
        private const string DeviceInfoSubtitle = "المواصفات والبطارية والمساحة من الجهاز الموصول عبر USB";

        private const string AppsTitle = "التطبيقات";
        private const string AppsSubtitle = "تطبيقات الآيفون الموصول ومجلدات مشاركة الملفات";

        private const string PhotosTitle = "الصور";
        private const string PhotosSubtitle = "الألبومات والوسائط ونقلها إلى الآيفون أو إلى الكمبيوتر";

        private const string VideosTitle = "الفيديوهات";
        private const string VideosSubtitle = "فيديوهات الآيفون المعروضة عبر الربط المحلي مباشرة";

        private readonly MainViewModel _viewModel;

        // صفحات المشروع الأول المستعادة
        private HomePage _homePage;
        private DeviceInfoPage _deviceInfoPage;
        private AppsManagerPage _appsManagerPage;
        private EnhancedPhotosPage _enhancedPhotosPage;
        private DevicesPage _devicesPage;
        private FilesPage _filesPage;
        private DownloaderPage _downloaderPage;
        private TextPage _textPage;
        private TransferHistoryPage _historyPage;
        private SettingsPage _settingsPage;
        private readonly Dictionary<string, ModulePage> _modulePages = new Dictionary<string, ModulePage>();

        private System.Windows.Threading.DispatcherTimer _usbRefreshTimer;
        private HwndSource _windowSource;
        private const int WmDeviceChange = 0x0219;

        public MainWindow()
        {
            InitializeComponent();

            FitToWorkArea();

            try
            {
                _viewModel = new MainViewModel();
                DataContext = _viewModel;
            }
            catch (Exception ex)
            {
                // لا نافذة مقاطعة عند بدء التشغيل — نسجّل ونكمل بواجهة فارغة آمنة.
                App.LogError(ex, "Startup");
            }

            EnsurePages();
            Navigate(_homePage, HomeButton, HomeTitle, HomeSubtitle);

            if (_viewModel != null)
            {
                _viewModel.ShowDevicePageRequested += OnShowDevicePageRequested;
            }

            StateChanged += OnWindowStateChanged;
            PreviewKeyDown += OnPreviewKeyDown;
            Closed += OnWindowClosed;
        }

        /// <summary>تصغير النافذة تلقائياً إذا كانت الشاشة أصغر من الحجم الافتراضي.</summary>
        private void FitToWorkArea()
        {
            try
            {
                Rect workArea = SystemParameters.WorkArea;

                double maxWidth = Math.Max(900, workArea.Width - 24);
                double maxHeight = Math.Max(600, workArea.Height - 24);

                if (Width > maxWidth) Width = maxWidth;
                if (Height > maxHeight) Height = maxHeight;

                MinWidth = Math.Min(MinWidth, Width);
                MinHeight = Math.Min(MinHeight, Height);
            }
            catch
            {
                // نترك القيم الافتراضية إن تعذرت قراءة أبعاد الشاشة
            }
        }

        #region مراقبة USB

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            try
            {
                _windowSource = PresentationSource.FromVisual(this) as HwndSource;
                if (_windowSource != null)
                {
                    _windowSource.AddHook(WndProc);
                }
            }
            catch
            {
                // يبقى زر «فحص» (F5) متاحاً
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WmDeviceChange)
            {
                OnUsbDeviceChanged(null, EventArgs.Empty);
            }

            return IntPtr.Zero;
        }

        /// <summary>يُستدعى عند تغيّر أجهزة النظام (قد يأتي من خيط آخر).</summary>
        private void OnUsbDeviceChanged(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_viewModel == null) return;

                if (_usbRefreshTimer == null)
                {
                    _usbRefreshTimer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromSeconds(1.5)
                    };

                    _usbRefreshTimer.Tick += OnUsbRefreshTimerTick;
                }

                // ويندوز يرسل عدة أحداث لنفس الكابل
                _usbRefreshTimer.Stop();
                _usbRefreshTimer.Start();
            }));
        }

        private void OnUsbRefreshTimerTick(object sender, EventArgs e)
        {
            if (_usbRefreshTimer != null) _usbRefreshTimer.Stop();
            if (_viewModel != null && _viewModel.RefreshDevicesCommand.CanExecute(null))
            {
                _viewModel.RefreshDevicesCommand.Execute(null);
            }
        }

        private void OnWindowClosed(object sender, EventArgs e)
        {
            _viewModel?.StopDownloader();
            if (_usbRefreshTimer != null)
            {
                _usbRefreshTimer.Stop();
                _usbRefreshTimer.Tick -= OnUsbRefreshTimerTick;
                _usbRefreshTimer = null;
            }

            if (_windowSource != null)
            {
                try { _windowSource.RemoveHook(WndProc); }
                catch { }
                _windowSource = null;
            }

            PreviewKeyDown -= OnPreviewKeyDown;
            StateChanged -= OnWindowStateChanged;
        }

        #endregion

        #region التنقل

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.OriginalSource is TextBox || e.OriginalSource is RichTextBox) return;

            switch (e.Key)
            {
                case Key.D1:
                case Key.NumPad1:
                    ShowDeviceInfoPage();
                    e.Handled = true;
                    break;

                case Key.D2:
                case Key.NumPad2:
                    ShowAppsManagerPage();
                    e.Handled = true;
                    break;

                case Key.D3:
                case Key.NumPad3:
                    ShowPhotosPage();
                    e.Handled = true;
                    break;

                case Key.D4:
                case Key.NumPad4:
                    ShowDevicesPage();
                    e.Handled = true;
                    break;
            }
        }

        private void EnsurePages()
        {
            EnsurePage(ref _homePage);
            EnsurePage(ref _deviceInfoPage);
            EnsurePage(ref _appsManagerPage);
            EnsurePage(ref _enhancedPhotosPage);
            EnsurePage(ref _devicesPage);
            EnsurePage(ref _filesPage);
            EnsurePage(ref _textPage);
            EnsurePage(ref _historyPage);
            EnsurePage(ref _settingsPage);
        }

        private void EnsurePage<T>(ref T page) where T : FrameworkElement, new()
        {
            if (page != null) return;

            try
            {
                page = new T();
                if (_viewModel != null) page.DataContext = _viewModel;
            }
            catch (Exception ex)
            {
                App.LogError(ex, "Create Page " + typeof(T).Name);
            }
        }

        private void Navigate(FrameworkElement page, Button activeButton, string title, string subtitle,
                              Button secondaryButton = null)
        {
            EnsurePages();

            if (page == null)
            {
                App.LogError(new InvalidOperationException("محاولة التنقل إلى صفحة غير مهيأة."), "Navigation");
                return;
            }

            if (HeaderTitle == null || HeaderSubtitle == null || MainContent == null)
            {
                App.LogError(new InvalidOperationException("عناصر التنقل غير مهيأة في MainWindow."), "Navigation");
                return;
            }

            HeaderTitle.Text = title;
            HeaderSubtitle.Text = subtitle;

            SetActiveNavigation(activeButton, secondaryButton);

            if (ReferenceEquals(MainContent.Content, page)) return;

            MainContent.Content = page;

            try
            {
                var translate = new TranslateTransform(0, 14);
                page.RenderTransform = translate;

                page.BeginAnimation(
                    OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    });

                translate.BeginAnimation(
                    TranslateTransform.YProperty,
                    new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(260))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    });
            }
            catch (Exception ex)
            {
                App.LogError(ex, "Navigation Animation");
            }
        }

        private void SetActiveNavigation(Button activeButton, Button secondaryButton = null)
        {
            Button[] buttons =
            {
                HomeButton,
                TopDeviceButton,
                TopAppsButton,
                TopRingtonesButton,
                TopFlashButton,
                TopToolboxButton,
                SidebarDeviceInfoButton,
                SidebarAppsButton,
                SidebarPhotosButton,
                SidebarMusicButton,
                SidebarRingtonesButton,
                SidebarVideosButton,
                SidebarBooksButton,
                SidebarUDiskButton,
                SidebarDataButton,
                SidebarFilesButton,
                SidebarDownloaderButton,
                SidebarCommonToolsButton,
                SidebarTextButton,
                SidebarHistoryButton,
                SidebarSettingsButton
            };

            foreach (Button button in buttons)
            {
                if (button == null) continue;
                bool active = ReferenceEquals(button, activeButton) || ReferenceEquals(button, secondaryButton);
                button.Tag = active ? "True" : null;
            }
        }

        public void ShowHomePage()
        {
            EnsurePages();
            Navigate(_homePage, HomeButton, HomeTitle, HomeSubtitle);
        }

        public void ShowDeviceInfoPage()
        {
            EnsurePages();
            Navigate(_deviceInfoPage, SidebarDeviceInfoButton, DeviceInfoTitle, DeviceInfoSubtitle, TopDeviceButton);
        }

        public void ShowAppsManagerPage()
        {
            ShowAppsManagerPage(SidebarAppsButton);
        }

        public void ShowAppsManagerPage(Button activeButton)
        {
            EnsurePages();

            if (_viewModel != null && _viewModel.AppsManagerItems.Count == 0 &&
                _viewModel.RefreshAppsManagerCommand.CanExecute(null))
            {
                _viewModel.RefreshAppsManagerCommand.Execute(null);
            }

            Navigate(_appsManagerPage, activeButton ?? SidebarAppsButton, AppsTitle, AppsSubtitle);
        }

        public void ShowPhotosPage()
        {
            ShowPhotosPage(null, PhotosTitle, PhotosSubtitle, SidebarPhotosButton);
        }

        public void ShowPhotosPage(string albumKey, string title, string subtitle, Button activeButton)
        {
            ShowEnhancedPhotosPage(albumKey, title, subtitle, activeButton);
        }

        /// <summary>عرض معرض الصور والفيديو، أو ألبوم الفيديو عند تمرير «videos».</summary>
        public void ShowEnhancedPhotosPage(string albumKey = null)
        {
            ShowEnhancedPhotosPage(albumKey, PhotosTitle, PhotosSubtitle, SidebarPhotosButton);
        }

        private void ShowEnhancedPhotosPage(string albumKey, string title, string subtitle, Button activeButton)
        {
            EnsurePages();

            if (_viewModel != null)
            {
                _viewModel.SelectedSmartAlbumKey = string.IsNullOrWhiteSpace(albumKey) ? "all" : albumKey;
                Observe(_viewModel.RefreshEnhancedMediaAsync());
            }

            Navigate(_enhancedPhotosPage, activeButton ?? SidebarPhotosButton, title, subtitle);
        }

        public void ShowDevicesPage()
        {
            EnsurePages();
            Navigate(_devicesPage, TopDeviceButton, "الجهاز", "تصفح ملفات الآيفون والوسائط عبر AFC مباشرة",
                     SidebarDeviceInfoButton);
        }

        /// <summary>«عرض الملفات» في مدير التطبيقات: انتقال مباشر إلى مجلد التطبيق.</summary>
        private void OnShowDevicePageRequested(object sender, EventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(ShowDevicesPage));
                return;
            }

            ShowDevicesPage();
        }

        public void ShowFilesPage()
        {
            EnsurePages();
            Navigate(_filesPage, SidebarFilesButton, "الملفات", "إضافة الملفات المحلية ونسخها إلى مكتبة التطبيق", null);
        }

        public void ShowDownloaderPage()
        {
            EnsurePage(ref _downloaderPage);
            Navigate(_downloaderPage, SidebarDownloaderButton, "التنزيل الخاص بأسيد",
                     "تنزيل الفيديو والصوت من الروابط المدعومة");
        }

        private void SidebarDownloader_Click(object sender, RoutedEventArgs e)
        {
            ShowDownloaderPage();
        }

        public void ShowTextPage()
        {
            EnsurePages();
            Navigate(_textPage, SidebarTextButton, "النصوص والحافظة",
                     "كتابة النصوص وحفظها محلياً أو إرسالها إلى الآيفون", null);
        }

        public void ShowHistoryPage()
        {
            EnsurePages();
            Navigate(_historyPage, SidebarHistoryButton, "سجل النقل",
                     "عمليات النقل المحفوظة محلياً في قاعدة SQLite", null);
        }

        public void ShowSettingsPage()
        {
            EnsurePages();
            Navigate(_settingsPage, SidebarSettingsButton, "الإعدادات والتشخيص",
                     "حالة الخدمات والربط والمجلدات المحلية", null);
        }

        /// <summary>صفحة عامة لكل وحدة من وحدات المشروع الأول التي لا تحتوي على منطق خاص.</summary>
        public void ShowModulePage(string key, string title, string subtitle, string glyph, string description,
                                   Button activeButton = null)
        {
            EnsurePages();

            ModulePage page;
            if (_modulePages.TryGetValue(key, out page))
            {
                page.ModuleTitle = title;
                page.ModuleSubtitle = subtitle;
                page.ModuleGlyph = glyph;
                page.ModuleDescription = description;
            }
            else
            {
                try
                {
                    page = new ModulePage
                    {
                        ModuleTitle = title,
                        ModuleSubtitle = subtitle,
                        ModuleGlyph = glyph,
                        ModuleDescription = description
                    };
                    if (_viewModel != null) page.DataContext = _viewModel;
                    _modulePages[key] = page;
                }
                catch (Exception ex)
                {
                    App.LogError(ex, "Create ModulePage " + key);
                    return;
                }
            }

            Navigate(page, activeButton, title, subtitle);
        }

        private static void Observe(Task task)
        {
            if (task == null) return;

            task.ContinueWith(
                finished =>
                {
                    if (finished.IsFaulted && finished.Exception != null)
                    {
                        App.LogError(finished.Exception, "Background Load");
                    }
                },
                TaskContinuationOptions.OnlyOnFaulted);
        }

        #region أحداث التنقل

        private void Home_Click(object sender, RoutedEventArgs e)
        {
            ShowHomePage();
        }

        private void SidebarDeviceInfo_Click(object sender, RoutedEventArgs e)
        {
            ShowDeviceInfoPage();
        }

        private void SidebarApps_Click(object sender, RoutedEventArgs e)
        {
            ShowAppsManagerPage(SidebarAppsButton);
        }

        private void SidebarPhotos_Click(object sender, RoutedEventArgs e)
        {
            ShowPhotosPage();
        }

        private void SidebarMusic_Click(object sender, RoutedEventArgs e)
        {
            ShowModulePage("music", "الموسيقى", "مكتبة الموسيقى على الجهاز الموصول عبر الربط المحلي",
                           "\uE8D6", "تصفح ملفات الموسيقى ونقلها بين الآيفون والكمبيوتر باستخدام نفس اتصال AFC المباشر.");
        }

        private void SidebarRingtones_Click(object sender, RoutedEventArgs e)
        {
            ShowModulePage("ringtones", "النغمات", "النغمات والملفات الصوتية الخاصة بالجهاز",
                           "\uE8D6", "إدارة النغمات وملفات الصوت عبر مسارات الجهاز المتاحة، بلا تنزيلات أو اتصال إنترنت.");
        }

        private void SidebarVideos_Click(object sender, RoutedEventArgs e)
        {
            ShowPhotosPage("videos", VideosTitle, VideosSubtitle, SidebarVideosButton);
        }

        private void SidebarBooks_Click(object sender, RoutedEventArgs e)
        {
            ShowModulePage("books", "الكتب", "الكتب والمستندات المتاحة على الجهاز",
                           "\uE8A5", "عرض ونقل الكتب والمستندات عبر نفس ربط USB/AFC المستخدم في بقية الأقسام.");
        }

        private void SidebarUDisk_Click(object sender, RoutedEventArgs e)
        {
            ShowModulePage("udisk", "قرص UDisk", "مساحة التخزين المتاحة للملفات على الجهاز",
                           "\uE8B7", "مساحة تخزين إضافية للملفات يمكن الوصول إليها عبر الربط المحلي.");
        }

        private void SidebarData_Click(object sender, RoutedEventArgs e)
        {
            ShowModulePage("data", "البيانات", "بيانات التطبيقات والمجلدات المتاحة",
                           "\uE937", "استعراض مجلدات البيانات التي تكشفها خدمة AFC على الجهاز الموصول.");
        }

        private void SidebarFiles_Click(object sender, RoutedEventArgs e)
        {
            ShowFilesPage();
        }

        private void SidebarCommonTools_Click(object sender, RoutedEventArgs e)
        {
            ShowModulePage("common", "أدوات شائعة", "أدوات الفحص والتشخيص والنسخ الاحتياطي",
                           "\uE90C", "مجموعة الأدوات التي كانت تظهر في المشروع الأول، تعمل جميعها محلياً.");
        }

        private void SidebarText_Click(object sender, RoutedEventArgs e)
        {
            ShowTextPage();
        }

        private void SidebarHistory_Click(object sender, RoutedEventArgs e)
        {
            ShowHistoryPage();
        }

        private void SidebarSettings_Click(object sender, RoutedEventArgs e)
        {
            ShowSettingsPage();
        }

        private void TopModule_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            if (button == null) return;

            if (ReferenceEquals(button, TopDeviceButton))
            {
                ShowDevicesPage();
            }
            else if (ReferenceEquals(button, TopAppsButton))
            {
                ShowAppsManagerPage(TopAppsButton);
            }
            else if (ReferenceEquals(button, TopRingtonesButton))
            {
                ShowModulePage("rtwp", "النغمات والخلفيات", "النغمات والصور المستخدمة كخلفيات",
                               "\uE8D6", "إدارة النغمات والخلفيات من نفس واجهة الربط المحلي دون إنترنت.",
                               TopRingtonesButton);
            }
            else if (ReferenceEquals(button, TopFlashButton))
            {
                ShowModulePage("flash", "الفلاش الذكي", "أدوات الإضاءة والفلاش المرتبطة بالجهاز",
                               "\uE7B4", "وحدة الفلاش الذكي كما ظهرت في المشروع الأول، تعمل ضمن نفس الربط المحلي.",
                               TopFlashButton);
            }
            else if (ReferenceEquals(button, TopToolboxButton))
            {
                ShowModulePage("toolbox", "صندوق الأدوات", "كل الأدوات المساعدة في مكان واحد",
                               "\uE90C", "صندوق الأدوات الذي يجمع الفحص والتشخيص والنقل والتخصيص بلا إنترنت.",
                               TopToolboxButton);
            }
        }

        private void QuickAction_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            if (button == null) return;

            if (ReferenceEquals(button, QuickBackupButton))
            {
                ShowModulePage("backup", "النسخ الاحتياطي / الاستعادة",
                               "نسخ البيانات محلياً واستعادتها عبر نفس اتصال الجهاز",
                               "\uE7C4", "تجهيز نسخة محلية من البيانات المسموح بها عبر AFC، أو استعادتها من الملفات المحفوظة.",
                               SidebarSettingsButton);
            }
            else if (ReferenceEquals(button, QuickAirplayButton))
            {
                ShowModulePage("airplay", "العرض اللاسلكي", "خيارات عرض الشاشة والوسائط",
                               "\uE8D2", "معلومات وإرشادات العرض اللاسلكي؛ التنفيذ الفعلي يتم من إعدادات الجهاز نفسه.");
            }
            else if (ReferenceEquals(button, QuickMakeRingtoneButton))
            {
                ShowModulePage("make-ring", "إنشاء نغمة", "تحويل مقطع صوتي إلى نغمة",
                               "\uE8D6", "إعداد ملف صوتي محلي تمهيداً لنقله إلى مجلد النغمات عبر نافذة النقل الموحّدة.",
                               SidebarRingtonesButton);
            }
            else if (ReferenceEquals(button, QuickIconsButton))
            {
                ShowModulePage("icons", "إدارة الأيقونات", "تنظيم أيقونات التطبيقات والملفات",
                               "\uE80D", "إدارة الأيقونات داخل التطبيق، مع الإبقاء على ربط الجهاز كما هو.");
            }
            else if (ReferenceEquals(button, QuickStopUpdateButton))
            {
                ShowModulePage("stop-update", "إيقاف تحديث iOS", "معلومات عن تعطيل تحديثات النظام",
                               "\uE9D9", "لا يغيّر التطبيق إعدادات النظام؛ يعرض فقط الإرشادات المتاحة محلياً بلا إنترنت.");
            }
            else if (ReferenceEquals(button, QuickTransferButton))
            {
                ShowDevicesPage();
            }
            else if (ReferenceEquals(button, QuickCustomizeButton))
            {
                ShowSettingsPage();
            }
        }

        /// <summary>زر الإضافة الموحّد: يضيف ملفات فقط بلا أي تنقل إضافي.</summary>
        private void AddFiles_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null) return;
            _viewModel.AddFilesCommand.Execute(null);
        }

        #endregion

        #endregion

        #region شريط العنوان المخصص

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleMaximize();
                return;
            }

            if (e.ButtonState == MouseButtonState.Pressed)
            {
                try { DragMove(); }
                catch { /* تجاهل أخطاء السحب */ }
            }
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            ToggleMaximize();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ToggleMaximize()
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private void OnWindowStateChanged(object sender, EventArgs e)
        {
            bool isMaximized = WindowState == WindowState.Maximized;

            if (MaximizeGlyph != null)
            {
                MaximizeGlyph.Text = isMaximized ? "\uE923" : "\uE922";
            }

            if (MaximizeButton != null)
            {
                MaximizeButton.ToolTip = isMaximized ? "استعادة" : "تكبير";
            }
        }

        #endregion
    }
}
