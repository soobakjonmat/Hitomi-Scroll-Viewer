﻿using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage.Streams;

namespace Hitomi_Scroll_Viewer {
    public sealed partial class MainWindow : Window {
        public static readonly string IMAGE_DIR = "images";

        public SearchPage sp;
        public ImageWatchingPage iwp;
        private readonly Page[] _appPages;
        private static int _currPageNum = 0;
        private static AppWindow _myAppWindow;

        public static Gallery gallery;
        public static List<Gallery> bmGalleries;

        public MainWindow() {
            InitializeComponent();

            Title = "Hitomi Scroll Viewer";

            // Maximise window
            IntPtr windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(windowHandle);
            _myAppWindow = AppWindow.GetFromWindowId(windowId);

            sp = new(this);
            iwp = new(this);
            _appPages = new Page[] { sp, iwp };

            sp.Init();
            iwp.Init();

            // Switch page on double click
            RootFrame.DoubleTapped += (object _, DoubleTappedRoutedEventArgs _) => {
                if (RootFrame.Content as Page != iwp) {
                    ImageWatchingPage.StopAutoScrolling();
                }
                SwitchPage();
            };

            // Maximise window on load
            RootFrame.Loaded += (object _, RoutedEventArgs _) => {
                (_myAppWindow.Presenter as OverlappedPresenter).Maximize();
            };

            // Handle window close
            Closed += (object _, WindowEventArgs _) => {
                ImageWatchingPage.StopAutoScrolling();
                if (gallery != null) {
                    if (!IsBookmarked()) {
                        DeleteGallery(gallery.id);
                    }
                }
            };

            RootFrame.Content = _appPages[_currPageNum];
        }

        public void SwitchPage() {
            _currPageNum = (_currPageNum + 1) % _appPages.Length;
            RootFrame.Content = _appPages[_currPageNum];
        }

        public Page GetPage() {
            return _appPages[_currPageNum];
        }

        public async void AlertUser(string title, string text) {
            ContentDialog dialog = new() {
                Title = title,
                Content = text,
                CloseButtonText = "Ok",
                XamlRoot = Content.XamlRoot
            };
            await dialog.ShowAsync();
        }

        public static async Task<BitmapImage> GetBitmapImage(byte[] imgData) {
            if (imgData == null) {
                return null;
            }
            BitmapImage img = new();
            InMemoryRandomAccessStream stream = new();

            DataWriter writer = new(stream);
            writer.WriteBytes(imgData);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
            stream.Seek(0);
            await img.SetSourceAsync(stream);

            writer.Dispose();
            stream.Dispose();
            return img;
        }

        public static bool IsBookmarked() {
            for (int i = 0; i < bmGalleries.Count; i++) {
                if (bmGalleries[i].id == gallery.id) {
                    return true;
                }
            }
            return false;
        }

        public static async Task SaveGallery(string id, byte[][] imageBytes) {
            string path = IMAGE_DIR + @"\" + id;
            Directory.CreateDirectory(path);

            for (int i = 0; i < imageBytes.Length; i++) {
                await File.WriteAllBytesAsync(path + @"\" + i.ToString(), imageBytes[i]);
            }
        }

        public static void DeleteGallery(string id) {
            try {
                Directory.Delete(IMAGE_DIR + @"\" + id, true);
            } catch (DirectoryNotFoundException) {}
        }

    }
}
