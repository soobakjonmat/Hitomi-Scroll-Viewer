﻿using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using static Hitomi_Scroll_Viewer.MainWindow;

namespace Hitomi_Scroll_Viewer {
    public sealed partial class ImageWatchingPage : Page {
        private static MainWindow _mw;

        private static bool _isAutoScrolling = false;
        private static bool _isLooping = true;
        private static double _scrollSpeed;
        private static double _pageTurnDelay;
        private static double _imageScale = 1;

        private static int _currPage = 0;

        private enum ViewMode {
            Default,
            Scroll
        }
        private static ViewMode _viewMode = ViewMode.Default;

        private readonly HttpClient _httpClient;

        private static readonly string GALLERY_INFO_DOMAIN = "https://ltn.hitomi.la/galleries/";
        private static readonly string GALLERY_INFO_EXCLUDE_STRING = "var galleryinfo = ";
        private static readonly string SERVER_TIME_ADDRESS = "https://ltn.hitomi.la/gg.js";
        private static readonly string REFERER = "https://hitomi.la/";
        private static readonly string[] POSSIBLE_IMAGE_SUBDOMAINS = { "https://aa.", "https://ba." };
        private static readonly JsonSerializerOptions serializerOptions = new() { IncludeFields = true };

        private static CancellationTokenSource _cts = new();
        private static CancellationToken _ct = _cts.Token;

        public enum GalleryState {
            Bookmarked,
            Bookmarking,
            BookmarkFull,
            Loaded,
            Loading,
            Empty
        }
        public static GalleryState galleryState = GalleryState.Empty; 
        private static bool _isInAction = false;

        private static int _loadRequestCounter = 0;

        public ImageWatchingPage(MainWindow mainWindow) {
            InitializeComponent();

            ImageContainer.Margin = new Thickness(0,TopCommandBar.ActualHeight,0,0);

            DisableAction();

            _mw = mainWindow;

            // handle mouse movement on commandbar
            void handlePointerEnter(object commandBar, PointerRoutedEventArgs args) {
                ((CommandBar)commandBar).IsOpen = true;
            }
            TopCommandBar.PointerEntered += handlePointerEnter;

            void handlePointerMove(object cb, PointerRoutedEventArgs args) {
                CommandBar commandBar = (CommandBar)cb;
                Point pos = args.GetCurrentPoint(MainGrid).Position;
                double center = MainGrid.ActualWidth / 2;
                double cbHalfWidth = commandBar.ActualWidth / 2;
                // commandBar.ActualHeight is the height at its ClosedDisplayMode
                // * 3 is the height of the commandbar when it is open and ClosedDisplayMode="Minimal"
                if (pos.Y > commandBar.ActualHeight * 3 || pos.X < center - cbHalfWidth || pos.X > center + cbHalfWidth) {
                    commandBar.IsOpen = false;
                }
            }
            TopCommandBar.PointerMoved += handlePointerMove;

            SocketsHttpHandler shh = new() {
                MaxConnectionsPerServer = 30,
            };
            _httpClient = new(shh);
        }

        // For testing
        // https://hitomi.la/doujinshi/radiata-%E6%97%A5%E6%9C%AC%E8%AA%9E-2472850.html#1
        // 9 images

        public void Init(SearchPage sp) {
            BookmarkBtn.Click += sp.AddBookmark;
        }

        private void HandleGoBackBtnClick(object _, RoutedEventArgs e) {
            _mw.SwitchPage();
        }

        private void HandleAutoScrollBtnClick(object _, RoutedEventArgs e) {
            SetAutoScroll(!_isAutoScrolling);
        }

        public void SetAutoScroll(bool newValue) {
            _isAutoScrolling = newValue;
            stopwatch.Reset();
            AutoScrollBtn.IsChecked = newValue;
            if (newValue) {
                AutoScrollBtn.Icon = new SymbolIcon(Symbol.Pause);
                AutoScrollBtn.Label = "Stop";
                Task.Run(ScrollAutomatically);
            } else {
                AutoScrollBtn.Icon = new SymbolIcon(Symbol.Play);
                AutoScrollBtn.Label = "Start Auto Page Turning / Scrolling";
            }
        }

        private void HandleLoopBtnClick(object _, RoutedEventArgs e) {
            _isLooping = !_isLooping;
        }

        private void SetLoop(bool newValue) {
            _isLooping = newValue;
            LoopBtn.IsChecked = newValue;
        }

        private async Task InsertSingleImage() {
            ImageContainer.Children.Clear();
            Image image = new() {
                Source = await GetBitmapImage(await File.ReadAllBytesAsync(IMAGE_DIR + @"\" + gallery.id + @"\" + _currPage)),
                Height = gallery.files[_currPage].height * _imageScale,
            };
            ImageContainer.Children.Add(image);
        }

        private async Task InsertImages() {
            ImageContainer.Children.Clear();
            Image[] images = new Image[gallery.files.Count];

            Task<BitmapImage>[] tasks = new Task<BitmapImage>[images.Length];

            for (int i = 0; i < images.Length; i++) {
                _ct.ThrowIfCancellationRequested();
                tasks[i] = GetBitmapImage(await File.ReadAllBytesAsync(IMAGE_DIR + @"\" + gallery.id + @"\" + i.ToString()));
                images[i] = new() {
                    Source = await tasks[i],
                    Height = gallery.files[i].height * _imageScale
                };
            }

            for (int i = 0; i < images.Length; i++) {
                _ct.ThrowIfCancellationRequested();
                ImageContainer.Children.Add(images[i]);
            }
        }

        private async void HandleViewModeBtnClick(object _, RoutedEventArgs e) {
            if (!await RequestActionPermit()) {
                return;
            }
            DisableAction();
            try {
                switch (_viewMode) {
                    case ViewMode.Default:
                        _viewMode = ViewMode.Scroll;
                        await InsertImages();
                        bool allAdded = false;
                        // wait for all images to be added
                        while (!allAdded) {
                            await Task.Delay(10);
                            allAdded = true;
                            for (int i = 0; i < ImageContainer.Children.Count; i++) {
                                if (((Image)ImageContainer.Children[i]).ActualHeight == 0) {
                                    allAdded = false;
                                    break;
                                }
                            }
                        }
                        DispatcherQueue.TryEnqueue(() => MainScrollViewer.ScrollToVerticalOffset(GetScrollOffsetFromPage()));
                        break;
                    case ViewMode.Scroll:
                        _viewMode = ViewMode.Default;
                        GetPageFromScrollOffset();
                        await InsertSingleImage();
                        break;
                }
            }
            catch (OperationCanceledException) {}
            finally {
                EnableAction();
            }
        }
        // TODO make half offset of the pages be the division point for calculating page number
        private void GetPageFromScrollOffset() {
            double imageHeightSum = ImageContainer.Margin.Top;
            for (int i = 0; i < gallery.files.Count; i++) {
                imageHeightSum += ((Image)ImageContainer.Children[i]).Height;
                if (imageHeightSum > MainScrollViewer.VerticalOffset) {
                    _currPage = i;
                    break;
                }
            }
        }

        private double GetScrollOffsetFromPage() {
            double offset = 0;
            if (_currPage != 0) {
                offset = ImageContainer.Margin.Top;
            }
            for (int i = 0; i < gallery.files.Count; i++) {
                if (i >= _currPage) {
                    return offset;
                }
                offset += ((Image)ImageContainer.Children[i]).Height;
            }
            return offset;
        }

        private void SetScrollSpeed(object slider, RangeBaseValueChangedEventArgs e) {
            _scrollSpeed = (slider as Slider).Value;
            // delay = -1.9*(slider value) + 11 in seconds
            _pageTurnDelay = (-1.9 * _scrollSpeed + 11) * 1000;
        }

        private static void IncrementPage(int num) {
            _currPage = (_currPage + num + gallery.files.Count) % gallery.files.Count;
        }

        public async void HandleKeyDown(object _, KeyRoutedEventArgs e) {
            if (e.Key == Windows.System.VirtualKey.L) {
                SetLoop(!_isLooping);
            }
            if (gallery != null && !_isInAction) {
                _isInAction = true;
                if (_viewMode == ViewMode.Default) {
                    if (e.Key is Windows.System.VirtualKey.Right or Windows.System.VirtualKey.RightButton) {
                        IncrementPage(1);
                        await InsertSingleImage();
                    } else if (e.Key is Windows.System.VirtualKey.Left or Windows.System.VirtualKey.LeftButton) {
                        IncrementPage(-1);
                        await InsertSingleImage();
                    }
                }
                if (e.Key == Windows.System.VirtualKey.Space) {
                    SetAutoScroll(!_isAutoScrolling);
                }
                _isInAction = false;
            }
        }

        // for updating auto scrolling in sync with real time
        private static readonly Stopwatch stopwatch = new();

        private async void ScrollAutomatically() {
            while (_isAutoScrolling) {
                switch (_viewMode) {
                    case ViewMode.Default:
                        if (_currPage + 1 == gallery.files.Count && !_isLooping) {
                            DispatcherQueue.TryEnqueue(() => SetAutoScroll(false));
                            return;
                        }
                        await Task.Delay((int)_pageTurnDelay);
                        if (_isAutoScrolling) {
                            if (_currPage + 1 == gallery.files.Count && !_isLooping) {
                                DispatcherQueue.TryEnqueue(() => SetAutoScroll(false));
                                return;
                            }
                            IncrementPage(1);
                            DispatcherQueue.TryEnqueue(async () => await InsertSingleImage());
                        }
                        break;
                    case ViewMode.Scroll:
                        DispatcherQueue.TryEnqueue(() => {
                            if (MainScrollViewer.VerticalOffset != MainScrollViewer.ScrollableHeight) {
                                stopwatch.Stop();
                                MainScrollViewer.ScrollToVerticalOffset(MainScrollViewer.VerticalOffset + _scrollSpeed * stopwatch.ElapsedMilliseconds);
                                stopwatch.Restart();
                            }
                            else {
                                if (_isLooping) {
                                    MainScrollViewer.ScrollToVerticalOffset(0);
                                } else {
                                    SetAutoScroll(false);
                                    return;
                                }
                            }
                        });
                        break;
                }
            }
        }

        /**
         * <summary>
         * Change image size and re-position vertical offset to the page that the user was already at.
         * </summary>
         */
        private void ChangeImageSize(object slider, RangeBaseValueChangedEventArgs e) {
            _imageScale = ((Slider)slider).Value;
            if (ImageContainer != null && gallery != null) {
                switch (_viewMode) {
                    case ViewMode.Default:
                        Image image = (Image)ImageContainer.Children[0];
                        image.Height = gallery.files[_currPage].height * _imageScale;
                        break;
                    case ViewMode.Scroll:
                        GetPageFromScrollOffset();
                        for (int i = 0; i < ImageContainer.Children.Count; i++) {
                            ((Image)ImageContainer.Children[i]).Height = gallery.files[i].height * _imageScale;
                        }
                        // set vertical offset according to the new image scale
                        MainScrollViewer.ScrollToVerticalOffset(GetScrollOffsetFromPage());
                        break;
                }
            }
        }

        /**
         * <summary>
         * <see cref="RequestActionPermit"/> or <see cref="StartLoading"/> must be called before calling this method
         * to set <see cref="_isInAction"/> to <c>true</c>.
         * </summary>
         */
        private void DisableAction() {
            SetAutoScroll(false);
            ViewModeBtn.IsEnabled = false;
            ImageScaleSlider.IsEnabled = false;
            AutoScrollBtn.IsEnabled = false;
        }

        private void EnableAction() {
            ViewModeBtn.IsEnabled = true;
            ImageScaleSlider.IsEnabled = true;
            AutoScrollBtn.IsEnabled = true;
            _isInAction = false;
        }

        /**
         * <returns><c>true</c> if the load request is permitted, otherwise <c>false</c></returns>
         */
        private static async Task<bool> RequestActionPermit() {
            if (_isInAction) {
                int rank = Interlocked.Increment(ref _loadRequestCounter);
                if (rank == 1) {
                    // first load request so send cancel request
                    _cts.Cancel();
                }
                while (_isInAction) {
                    await Task.Delay(10);
                    // this request is not the latest request anymore
                    if (_loadRequestCounter != rank) {
                        return false;
                    }
                }
                // loading finished
                // this request is the latest request
                _isInAction = true;
                _loadRequestCounter = 0;
                _cts.Dispose();
                _cts = new();
                _ct = _cts.Token;
                return true;
            }
            _isInAction = true;
            return true;
        }

        private async Task<bool> StartLoading() {
            _mw.SwitchPage();
            if (!await RequestActionPermit()) {
                return false;
            }

            DisableAction();

            ChangeBookmarkBtnState(GalleryState.Loading);

            // check if we have a gallery already loaded
            if (gallery != null) {
                // if the loaded gallery is not bookmarked delete it from local directory
                if (!IsBookmarked()) {
                    DeleteGallery(gallery.id);
                }
            }
            return true;
        }

        private void FinishLoading(GalleryState state) {
            ChangeBookmarkBtnState(state);
            EnableAction();
        }

        public async Task LoadGalleryFromLocalDir(int bmIdx) {
            if (!await StartLoading()) {
                return;
            }
            gallery = bmGalleries[bmIdx];
            try {
                switch (_viewMode) {
                    case ViewMode.Default:
                        _ct.ThrowIfCancellationRequested();
                        _currPage = 0;
                        await InsertSingleImage();
                        break;
                    case ViewMode.Scroll:
                        _ct.ThrowIfCancellationRequested();
                        await InsertImages();
                        break;
                }
            }
            catch (OperationCanceledException) {
                FinishLoading(GalleryState.Loading);
            } finally {
                FinishLoading(GalleryState.Bookmarked);
            }
        }

        private async Task<string> GetGalleryInfo(string id) {
            string address = GALLERY_INFO_DOMAIN + id + ".js";
            HttpRequestMessage galleryInfoRequest = new() {
                Method = HttpMethod.Get,
                RequestUri = new Uri(address)
            };
            HttpResponseMessage response = await _httpClient.SendAsync(galleryInfoRequest);
            try {
                response.EnsureSuccessStatusCode();
            } catch (HttpRequestException ex) {
                _mw.AlertUser("An error has occurred while getting gallery info. Please try again.", ex.Message);
                return null;
            }
            string responseString = await response.Content.ReadAsStringAsync();
            return responseString[GALLERY_INFO_EXCLUDE_STRING.Length..];
        }

        private async Task<string> GetServerTime() {
            HttpRequestMessage serverTimeRequest = new() {
                Method = HttpMethod.Get,
                RequestUri = new Uri(SERVER_TIME_ADDRESS)
            };
            HttpResponseMessage response = await _httpClient.SendAsync(serverTimeRequest);
            try {
                response.EnsureSuccessStatusCode();
            }
            catch (HttpRequestException ex) {
                _mw.AlertUser("An error has occurred while getting server time. Please try again.", ex.Message);
                return null;
            }
            string responseString = await response.Content.ReadAsStringAsync();

            return Regex.Match(responseString, @"\'(.+?)/\'").Value[1..^2];
        }

        // TODO read about http request sockets, multithreading, concurrent requests, etc.
        // and implement them accordingly
        // search: c# concurrent http requests
        // to check if images are requested asynchrounously print out i for loop

        public async Task LoadImagesFromWeb(string id) {
            if (!await StartLoading()) {
                return;
            }
            try {
                _ct.ThrowIfCancellationRequested();

                string galleryInfo = await GetGalleryInfo(id);

                if (galleryInfo == null) {
                    FinishLoading(GalleryState.Empty);
                    return;
                }
                gallery = JsonSerializer.Deserialize<Gallery>(galleryInfo, serializerOptions);

                _ct.ThrowIfCancellationRequested();

                string[] imgHashArr = new string[gallery.files.Count];
                for (int i = 0; i < gallery.files.Count; i++) {
                    imgHashArr[i] = gallery.files[i].hash;
                }

                string serverTime = await GetServerTime();
                string[] imgAddresses;
                if (serverTime == null) {
                    FinishLoading(GalleryState.Empty);
                    return;
                }
                imgAddresses = GetImageAddresses(imgHashArr, serverTime);

                _ct.ThrowIfCancellationRequested();

                byte[][] imageBytes = new byte[imgAddresses.Length][];

                for (int i = 0; i < imgAddresses.Length; i++) {
                    _ct.ThrowIfCancellationRequested();
                    foreach (string subdomain in POSSIBLE_IMAGE_SUBDOMAINS) {
                        imageBytes[i] = await GetImageBytesFromWeb(subdomain + imgAddresses[i]);
                        if (imageBytes[i] != null) {
                            break;
                        }
                    }
                }

                // save gallery to local directory
                await SaveGallery(gallery.id, imageBytes);

                switch (_viewMode) {
                    case ViewMode.Default:
                        _ct.ThrowIfCancellationRequested();
                        _currPage = 0;
                        await InsertSingleImage();
                        break;
                    case ViewMode.Scroll:
                        _ct.ThrowIfCancellationRequested();
                        await InsertImages();
                        break;
                }
            }
            catch (OperationCanceledException) {
                FinishLoading(GalleryState.Loading);
                return;
            } finally {
                if (IsBookmarkFull()) {
                    FinishLoading(GalleryState.BookmarkFull);
                }
                else {
                    FinishLoading(GalleryState.Loaded);
                }
            }
        }

        private static string[] GetImageAddresses(string[] imgHashArr, string serverTime) {
            string[] result = new string[imgHashArr.Length];
            for (int i = 0; i < imgHashArr.Length; i++) {
                string hash = imgHashArr[i];
                string oneTwoCharInt = Convert.ToInt32(hash[^1..] + hash[^3..^1], 16).ToString();
                result[i] = $"hitomi.la/webp/{serverTime}/{oneTwoCharInt}/{hash}.webp";
            }
            return result;
        }

        /**
         * <returns>The image <c>byte[]</c> if the given address is valid, otherwise <c>null</c>.</returns>
         */
        public async Task<byte[]> GetImageBytesFromWeb(string address) {
            HttpRequestMessage request = new() {
                Method = HttpMethod.Get,
                RequestUri = new Uri(address),
                Headers = {
                    {"referer", REFERER }
                },
            };
            HttpResponseMessage response;
            try {
                response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();
            } catch (HttpRequestException e) {
                if (e.StatusCode != System.Net.HttpStatusCode.NotFound) {
                    Debug.WriteLine(e.Message);
                    Debug.WriteLine("Status Code: " + e.StatusCode);
                }
                return null;
            }
            return await response.Content.ReadAsByteArrayAsync();
        }

        public void ChangeBookmarkBtnState(GalleryState state) {
            galleryState = state;
            if (state == GalleryState.Loaded) {
                BookmarkBtn.IsEnabled = true;
            } else {
                BookmarkBtn.IsEnabled = false;
            }
            switch (state) {
                case GalleryState.Bookmarked:
                    BookmarkBtn.Label = "Bookmarked";
                    break;
                case GalleryState.Bookmarking:
                    BookmarkBtn.Label = "Bookmarking...";
                    break;
                case GalleryState.BookmarkFull:
                    BookmarkBtn.Label = "Bookmark is full";
                    break;
                case GalleryState.Loaded:
                    BookmarkBtn.Label = "Bookmark this gallery";
                    break;
                case GalleryState.Loading:
                    BookmarkBtn.Label = "Loading images...";
                    break;
                case GalleryState.Empty:
                    BookmarkBtn.Label = "";
                    break;
            }
        }
    }
}
