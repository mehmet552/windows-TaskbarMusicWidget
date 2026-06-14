using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace TaskbarMusicWidget
{
    public partial class MainWindow : Window
    {
        // ── Win32 API ──────────────────────────────────────────────────────────
        [DllImport("user32.dll")]
        static extern IntPtr FindWindow(string cls, string? wnd);

        [DllImport("user32.dll")]
        static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string? wnd);

        [DllImport("user32.dll")]
        static extern IntPtr SetParent(IntPtr child, IntPtr parent);

        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr insert, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        static extern void keybd_event(byte vk, byte scan, uint flags, uint extra);

        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        static extern IntPtr GetShellWindow();

        [DllImport("user32.dll")]
        static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_SHOWWINDOW  = 0x0040;
        private const uint SWP_NOACTIVATE  = 0x0010;
        private const uint SWP_NOSIZE      = 0x0001;
        private const uint SWP_NOMOVE      = 0x0002;
        private const uint SWP_HIDEWINDOW  = 0x0080;
        private const int  GWL_EXSTYLE     = -20;
        private const int  WS_EX_TOOLWINDOW = 0x00000080;
        private const int  WS_EX_NOACTIVATE = 0x08000000;
        private const uint MONITOR_DEFAULTTONEAREST = 2;
        private const byte VK_MEDIA_NEXT   = 0xB0;
        private const byte VK_MEDIA_PREV   = 0xB1;
        private const byte VK_MEDIA_PLAY   = 0xB3;
        private const int  SW_RESTORE      = 9;

        // ── SMTC ───────────────────────────────────────────────────────────────
        private GlobalSystemMediaTransportControlsSessionManager? _mgr;
        private GlobalSystemMediaTransportControlsSession?        _session;
        private bool _isPlaying = false;
        private bool _scrolling = false;
        private Storyboard? _scrollSb;
        private Storyboard? _visualizerSb;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Closed += (_, _) => 
            {
                _scrollSb?.Stop();
                _visualizerSb?.Stop(this);
                AudioManager.Cleanup();
            };
            SystemEvents.UserPreferenceChanged += (_, e) =>
            {
                if (e.Category == UserPreferenceCategory.General) ApplyTheme();
            };
            ApplyTheme();
            BuildScrollStoryboard();
            BuildVisualizerStoryboard();
        }

        // ── Loaded & SourceInitialized ─────────────────────────────────────────
        private DispatcherTimer _topmostTimer;
        private DispatcherTimer _timelineTimer;
        private DispatcherTimer _timelineHideTimer;
        private bool _isUpdatingVolume = false;
        private bool _isDraggingTimeline = false;

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // WindowStyle degisiklikleri pencere gosterilmeden HEMEN SONRA yapilmali (Loaded'dan once)
            // Aksi takdirde AllowsTransparency=True olan pencereler gorunmez olur.
            var hWnd = new WindowInteropHelper(this).Handle;
            int exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
            SetWindowLong(hWnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            PositionOnTaskbar();
            
            // Gorev cubugu bazen widget'in ustune cikabilir, bunu onlemek icin periyodik olarak uste al
            _topmostTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _topmostTimer.Tick += (s, ev) => PositionOnTaskbar();
            _topmostTimer.Start();

            // Ses seviyesi senkronizasyonu (Event tabanli)
            AudioManager.Initialize();
            AudioManager.VolumeChanged += (vol) =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    if (Math.Abs(vol - SldVolume.Value) > 1.0)
                    {
                        _isUpdatingVolume = true;
                        SldVolume.Value = vol;
                        _isUpdatingVolume = false;
                    }
                });
            };

            _isUpdatingVolume = true;
            SldVolume.Value = AudioManager.GetMasterVolume();
            _isUpdatingVolume = false;

            _timelineTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _timelineTimer.Tick += (s, ev) => UpdateTimelineUI();
            _timelineTimer.Start();

            _timelineHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _timelineHideTimer.Tick += (s, ev) => 
            {
                TimelinePopup.IsOpen = false;
                _timelineHideTimer.Stop();
            };

            _ = InitSmtc();
        }

        private void UpdateTimelineUI()
        {
            if (!TimelinePopup.IsOpen || _isDraggingTimeline || _session == null) return;
            
            try
            {
                var props = _session.GetTimelineProperties();
                if (props != null && props.EndTime > TimeSpan.Zero)
                {
                    TxtDuration.Text = props.EndTime.ToString(@"m\:ss");
                    TxtPosition.Text = props.Position.ToString(@"m\:ss");
                    SldTimeline.Value = (props.Position.TotalSeconds / props.EndTime.TotalSeconds) * 100.0;
                }
            }
            catch { }
        }

        // ── Windows 11: Gorev cubugu uzerine overlay ──────────────────────────
        private void PositionOnTaskbar()
        {
            var hWnd = new WindowInteropHelper(this).Handle;

            if (IsForegroundFullScreen())
            {
                if (this.Visibility != Visibility.Hidden)
                    this.Visibility = Visibility.Hidden;
                return;
            }
            else
            {
                if (this.Visibility != Visibility.Visible)
                    this.Visibility = Visibility.Visible;
            }

            // Gorev cubugu pozisyonunu bul
            var taskbar = FindWindow("Shell_TrayWnd", null);

            // DPI faktorunu hesapla
            var source = PresentationSource.FromVisual(this);
            double dpiScale = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.25;

            if (taskbar != IntPtr.Zero && GetWindowRect(taskbar, out RECT tbRect))
            {
                int tbHeight = tbRect.Bottom - tbRect.Top; 
                int widgetH = (int)(this.Height * dpiScale);
                int widgetW = (int)(this.Width * dpiScale);

                int targetX = tbRect.Left + 8;
                int targetY = tbRect.Top + (tbHeight - widgetH) / 2;

                SetWindowPos(hWnd, HWND_TOPMOST, targetX, targetY, widgetW, widgetH,
                             SWP_SHOWWINDOW | SWP_NOACTIVATE);
            }
            else
            {
                this.Left = 4;
                this.Top = SystemParameters.PrimaryScreenHeight - this.Height - 4;
                this.Topmost = true;
            }
        }

        private bool IsForegroundFullScreen()
        {
            IntPtr fgWindow = GetForegroundWindow();
            if (fgWindow == IntPtr.Zero) return false;

            IntPtr desktopWindow = GetDesktopWindow();
            IntPtr shellWindow = GetShellWindow();
            if (fgWindow == desktopWindow || fgWindow == shellWindow) return false;

            GetWindowRect(fgWindow, out RECT appBounds);
            IntPtr hMonitor = MonitorFromWindow(fgWindow, MONITOR_DEFAULTTONEAREST);
            
            MONITORINFO mi = new MONITORINFO();
            mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            if (GetMonitorInfo(hMonitor, ref mi))
            {
                return appBounds.Left <= mi.rcMonitor.Left &&
                       appBounds.Top <= mi.rcMonitor.Top &&
                       appBounds.Right >= mi.rcMonitor.Right &&
                       appBounds.Bottom >= mi.rcMonitor.Bottom;
            }
            return false;
        }

        // ── SMTC (System Media Transport Controls) ─────────────────────────────
        private async Task InitSmtc()
        {
            try
            {
                _mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                _mgr.CurrentSessionChanged += (s, _) =>
                    Dispatcher.Invoke(() => HookSession(s.GetCurrentSession()));
                HookSession(_mgr.GetCurrentSession());
            }
            catch { ShowIdle(); }
        }

        private void HookSession(GlobalSystemMediaTransportControlsSession? s)
        {
            if (_session != null)
            {
                _session.MediaPropertiesChanged  -= OnMediaChanged;
                _session.PlaybackInfoChanged     -= OnPlaybackChanged;
            }
            _session = s;
            if (_session != null)
            {
                _session.MediaPropertiesChanged  += OnMediaChanged;
                _session.PlaybackInfoChanged     += OnPlaybackChanged;
                _ = RefreshMedia();
                RefreshPlayback();
            }
            else ShowIdle();
        }

        private void OnMediaChanged(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e)
            => Dispatcher.Invoke(() => _ = RefreshMedia());

        private void OnPlaybackChanged(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e)
            => Dispatcher.Invoke(RefreshPlayback);

        private async Task RefreshMedia()
        {
            if (_session == null) { Dispatcher.Invoke(ShowIdle); return; }
            try
            {
                var props = await _session.TryGetMediaPropertiesAsync();
                if (props == null) { Dispatcher.Invoke(ShowIdle); return; }

                var title  = string.IsNullOrWhiteSpace(props.Title)  ? "Bilinmiyor" : props.Title;
                var artist = string.IsNullOrWhiteSpace(props.Artist) ? ""           : props.Artist;

                Dispatcher.Invoke(() => 
                {
                    TxtTitle.Text  = title;
                    TxtArtist.Text = artist;
                });

                // Album kapagi
                if (props.Thumbnail != null)
                {
                    try
                    {
                        var stream = await props.Thumbnail.OpenReadAsync();
                        var bmp = new BitmapImage();
                        using var ms = new MemoryStream();
                        await stream.AsStreamForRead().CopyToAsync(ms);
                        ms.Position = 0;
                        Dispatcher.Invoke(() => 
                        {
                            bmp.BeginInit();
                            bmp.StreamSource = ms;
                            bmp.CacheOption  = BitmapCacheOption.OnLoad;
                            bmp.EndInit();
                            bmp.Freeze();
                            AlbumArt.Source = bmp;
                            NoArtIcon.Visibility = Visibility.Collapsed;
                        });
                    }
                    catch { Dispatcher.Invoke(() => { AlbumArt.Source = null; NoArtIcon.Visibility = Visibility.Visible; }); }
                }
                else
                {
                    Dispatcher.Invoke(() => { AlbumArt.Source = null; NoArtIcon.Visibility = Visibility.Visible; });
                }

                // Kayan animasyon
                Dispatcher.Invoke(() => 
                {
                    TxtTitle.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    double w = TxtTitle.DesiredSize.Width;
                    bool needScroll = w > 105;
                    if (needScroll && !_scrolling)       { _scrolling = true;  _scrollSb?.Begin(this, true); }
                    else if (!needScroll && _scrolling)  { _scrolling = false; _scrollSb?.Stop(this); TxtTitleTransform.X = 0; }
                });
            }
            catch { Dispatcher.Invoke(ShowIdle); }
        }

        private void RefreshPlayback()
        {
            if (_session == null) return;
            try
            {
                var info = _session.GetPlaybackInfo();
                _isPlaying = info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                Dispatcher.Invoke(() => 
                {
                    BtnPlay.Content = _isPlaying ? "\u23F8" : "\u25B6";
                    UpdateVisualizerState();
                });
            }
            catch { }
        }

        private void ShowIdle()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(ShowIdle);
                return;
            }

            try 
            {
                TxtTitle.Text  = "Muzik calmiyor";
                TxtArtist.Text = "";
                AlbumArt.Source = null;
                NoArtIcon.Visibility = Visibility.Visible;
                BtnPlay.Content = "\u25B6";
                _isPlaying = false;
                UpdateVisualizerState();
                _scrollSb?.Stop(this);
                _scrolling = false;
                TxtTitleTransform.X = 0;
            }
            catch { }
        }

        // ── Storyboard (kayan baslik) ──────────────────────────────────────────
        private void BuildScrollStoryboard()
        {
            _scrollSb = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
            var anim = new DoubleAnimationUsingKeyFrames
            {
                RepeatBehavior = RepeatBehavior.Forever
            };
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(0,     KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0))));
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(0,     KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2))));
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(-140,  KeyTime.FromTimeSpan(TimeSpan.FromSeconds(6))));
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(-140,  KeyTime.FromTimeSpan(TimeSpan.FromSeconds(8))));
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(0,     KeyTime.FromTimeSpan(TimeSpan.FromSeconds(10))));

            Storyboard.SetTargetName(anim, "TxtTitleTransform");
            Storyboard.SetTargetProperty(anim, new PropertyPath(TranslateTransform.XProperty));
            _scrollSb.Children.Add(anim);
        }

        private void BuildVisualizerStoryboard()
        {
            _visualizerSb = new Storyboard();
            
            var anim1 = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
            anim1.KeyFrames.Add(new LinearDoubleKeyFrame(2, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(0))));
            anim1.KeyFrames.Add(new LinearDoubleKeyFrame(10, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(200))));
            anim1.KeyFrames.Add(new LinearDoubleKeyFrame(4, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(400))));
            anim1.KeyFrames.Add(new LinearDoubleKeyFrame(12, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(600))));
            anim1.KeyFrames.Add(new LinearDoubleKeyFrame(2, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(800))));

            var anim2 = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
            anim2.KeyFrames.Add(new LinearDoubleKeyFrame(2, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(0))));
            anim2.KeyFrames.Add(new LinearDoubleKeyFrame(14, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(250))));
            anim2.KeyFrames.Add(new LinearDoubleKeyFrame(6, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(500))));
            anim2.KeyFrames.Add(new LinearDoubleKeyFrame(12, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(750))));
            anim2.KeyFrames.Add(new LinearDoubleKeyFrame(2, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1000))));

            var anim3 = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
            anim3.KeyFrames.Add(new LinearDoubleKeyFrame(2, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(0))));
            anim3.KeyFrames.Add(new LinearDoubleKeyFrame(8, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(300))));
            anim3.KeyFrames.Add(new LinearDoubleKeyFrame(14, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(600))));
            anim3.KeyFrames.Add(new LinearDoubleKeyFrame(4, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(900))));
            anim3.KeyFrames.Add(new LinearDoubleKeyFrame(2, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1200))));

            Storyboard.SetTargetName(anim1, "Bar1");
            Storyboard.SetTargetProperty(anim1, new PropertyPath(FrameworkElement.HeightProperty));
            
            Storyboard.SetTargetName(anim2, "Bar2");
            Storyboard.SetTargetProperty(anim2, new PropertyPath(FrameworkElement.HeightProperty));

            Storyboard.SetTargetName(anim3, "Bar3");
            Storyboard.SetTargetProperty(anim3, new PropertyPath(FrameworkElement.HeightProperty));

            _visualizerSb.Children.Add(anim1);
            _visualizerSb.Children.Add(anim2);
            _visualizerSb.Children.Add(anim3);
        }

        private void UpdateVisualizerState()
        {
            if (_isPlaying)
            {
                _visualizerSb?.Begin(this, true);
            }
            else
            {
                _visualizerSb?.Stop(this);
                Bar1.Height = 2;
                Bar2.Height = 2;
                Bar3.Height = 2;
            }
        }

        // ── Butonlar ──────────────────────────────────────────────────────────
        private void BtnPrev_Click(object sender, RoutedEventArgs e)
        {
            keybd_event(VK_MEDIA_PREV, 0, 0, 0);
            keybd_event(VK_MEDIA_PREV, 0, 2, 0);
        }

        private void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            keybd_event(VK_MEDIA_PLAY, 0, 0, 0);
            keybd_event(VK_MEDIA_PLAY, 0, 2, 0);
        }

        private void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            keybd_event(VK_MEDIA_NEXT, 0, 0, 0);
            keybd_event(VK_MEDIA_NEXT, 0, 2, 0);
        }

        private void AlbumArt_Click(object sender, MouseButtonEventArgs e)
        {
            if (_session != null)
            {
                TimelinePopup.IsOpen = true;
                UpdateTimelineUI();
                _timelineHideTimer.Start();
            }
        }

        private void AlbumArtContainer_MouseEnter(object sender, MouseEventArgs e)
        {
            _timelineHideTimer.Stop();
        }

        private void AlbumArtContainer_MouseLeave(object sender, MouseEventArgs e)
        {
            if (TimelinePopup.IsOpen)
                _timelineHideTimer.Start();
        }

        private void TimelinePopup_MouseEnter(object sender, MouseEventArgs e)
        {
            _timelineHideTimer.Stop();
        }

        private void TimelinePopup_MouseLeave(object sender, MouseEventArgs e)
        {
            if (TimelinePopup.IsOpen)
                _timelineHideTimer.Start();
        }

        private void SldTimeline_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _isDraggingTimeline = true;
        }

        private async void SldTimeline_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_session != null)
            {
                var props = _session.GetTimelineProperties();
                if (props != null && props.EndTime > TimeSpan.Zero)
                {
                    double percent = SldTimeline.Value / 100.0;
                    long targetTicks = (long)(props.EndTime.Ticks * percent);
                    await _session.TryChangePlaybackPositionAsync(targetTicks);
                }
            }
            _isDraggingTimeline = false;
        }

        private void SldTimeline_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isDraggingTimeline && _session != null)
            {
                var props = _session.GetTimelineProperties();
                if (props != null && props.EndTime > TimeSpan.Zero)
                {
                    double percent = e.NewValue / 100.0;
                    TimeSpan pos = TimeSpan.FromTicks((long)(props.EndTime.Ticks * percent));
                    TxtPosition.Text = pos.ToString(@"m\:ss");
                }
            }
        }

        private void SldVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtVolumeVal != null)
            {
                TxtVolumeVal.Text = ((int)e.NewValue).ToString();
            }

            if (!_isUpdatingVolume)
            {
                AudioManager.SetMasterVolume((float)e.NewValue);
            }
        }

        private void Volume_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            float currentVol = AudioManager.GetMasterVolume();
            float step = 2f;
            if (e.Delta > 0)
                currentVol = Math.Min(100f, currentVol + step);
            else
                currentVol = Math.Max(0f, currentVol - step);

            AudioManager.SetMasterVolume(currentVol);
            
            _isUpdatingVolume = true;
            SldVolume.Value = currentVol;
            _isUpdatingVolume = false;
        }

        private void Volume_MouseEnter(object sender, MouseEventArgs e)
        {
            VolPopup.IsOpen = true;
        }

        private void Volume_MouseLeave(object sender, MouseEventArgs e)
        {
            Dispatcher.InvokeAsync(async () => 
            {
                await Task.Delay(150);
                if (!VolPopup.IsMouseOver && !VolContainer.IsMouseOver)
                    VolPopup.IsOpen = false;
            });
        }

        private void VolPopup_MouseLeave(object sender, MouseEventArgs e)
        {
            Dispatcher.InvokeAsync(async () => 
            {
                await Task.Delay(150);
                if (!VolPopup.IsMouseOver && !VolContainer.IsMouseOver)
                    VolPopup.IsOpen = false;
            });
        }

        // ── Tema ──────────────────────────────────────────────────────────────
        private void ApplyTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                bool isDark = true;
                if (key?.GetValue("AppsUseLightTheme") is int v) isDark = v == 0;

                if (isDark)
                {
                    MainBorder.Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
                    TxtTitle.Foreground   = Brushes.White;
                    TxtArtist.Foreground  = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
                    Bar1.Fill = Bar2.Fill = Bar3.Fill = Brushes.White;
                }
                else
                {
                    MainBorder.Background = new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2));
                    TxtTitle.Foreground   = Brushes.Black;
                    TxtArtist.Foreground  = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
                    Bar1.Fill = Bar2.Fill = Bar3.Fill = Brushes.Black;
                }
            }
            catch { }
        }
    }
}