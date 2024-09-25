﻿using CommunityToolkit.Mvvm.ComponentModel;
using HitomiScrollViewerLib.Entities;
using HitomiScrollViewerLib.Models;
using HitomiScrollViewerLib.Views;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using static HitomiScrollViewerLib.Constants;

namespace HitomiScrollViewerLib.ViewModels.ViewPageVMs {
    public partial class GalleryTabViewItemVM : ObservableObject {
        public Gallery Gallery { get; init; }

        public GalleryViewSettings GalleryViewSettings { get; } = new();

        private Size _currentTabViewSize;
        public Size CurrentTabViewSize {
            get => _currentTabViewSize;
            set {
                _currentTabViewSize = value;
                UpdateImageCollectionPanelVMs();
            }
        }
        private const int SIZE_CHANGE_WAIT_TIME = 200;
        private DateTime _lastSizeChangedTime;

        private List<ImageCollectionPanelVM> _imageCollectionPanelVMs;
        public List<ImageCollectionPanelVM> ImageCollectionPanelVMs {
            get => _imageCollectionPanelVMs;
            set {
                MainWindow.MainDispatcherQueue.TryEnqueue(() => {
                    SetProperty(ref _imageCollectionPanelVMs, value);
                });
            }
        }
        private int _flipViewSelectedIndex;
        public int FlipViewSelectedIndex {
            get => _flipViewSelectedIndex;
            set {
                MainWindow.MainDispatcherQueue.TryEnqueue(() => {
                    SetProperty(ref _flipViewSelectedIndex, value);
                });
            }
        }

        private CancellationTokenSource _autoScrollCts;

        private bool _isAutoScrolling = false;
        public bool IsAutoScrolling {
            get => _isAutoScrolling;
            set {
                MainWindow.MainDispatcherQueue.TryEnqueue(() => {
                    if (SetProperty(ref _isAutoScrolling, value)) {
                        if (value) {
                            RequestShowActionIcon?.Invoke(GLYPH_PLAY, null);
                            _autoScrollCts = new();
                            Task.Run(StartAutoScrolling, _autoScrollCts.Token);
                        } else {
                            RequestShowActionIcon?.Invoke(GLYPH_PAUSE, null);
                            _autoScrollCts.Cancel();
                        }
                    }
                });
            }
        }

        private async void StartAutoScrolling() {
            while (IsAutoScrolling) {
                await Task.Delay((int)(GalleryViewSettings.AutoScrollInterval * 1000));
                if (!GalleryViewSettings.IsLoopEnabled && FlipViewSelectedIndex == ImageCollectionPanelVMs.Count - 1) {
                    IsAutoScrolling = false;
                    return;
                } else {
                    FlipViewSelectedIndex = (FlipViewSelectedIndex + 1) % ImageCollectionPanelVMs.Count;
                }
            }
        }

        public event Action<string, string> RequestShowActionIcon;

        public GalleryTabViewItemVM() {
            GalleryViewSettings.PropertyChanged += GalleryViewSettings_PropertyChanged;
        }

        private void GalleryViewSettings_PropertyChanged(object _0, PropertyChangedEventArgs e) {
            switch (e.PropertyName) {
                case nameof(GalleryViewSettings.IsLoopEnabled):
                    if (GalleryViewSettings.IsLoopEnabled) {
                        RequestShowActionIcon?.Invoke(GLYPH_REPEAT_ALL, null);
                    } else {
                        RequestShowActionIcon?.Invoke(GLYPH_REPEAT_ALL, GLYPH_CANCEL);
                    }
                    break;
            }
        }

        public async void UpdateImageCollectionPanelVMs() {
            DateTime localRecordedTime = _lastSizeChangedTime = DateTime.Now;
            await Task.Delay(SIZE_CHANGE_WAIT_TIME);
            if (_lastSizeChangedTime != localRecordedTime) {
                return;
            }
            ImageInfo[] imageInfos = [.. Gallery.Files.OrderBy(f => f.Index)];
            int imagesPerPage = CommonSettings.Main.ImagesPerPage;
            if (imagesPerPage == 0) {
                // auto allocate images per page by aspect ratio
                List<ImageCollectionPanelVM> imageCollectionPanelVMs = [];
                double viewportAspectRatio = CurrentTabViewSize.Width / CurrentTabViewSize.Height;
                double remainingAspectRatio = viewportAspectRatio - ((double)imageInfos[0].Width / imageInfos[0].Height);
                Range currentRange = 0..1;
                int pageIndex = 0;
                for (int i = 1; i < imageInfos.Length; i++) {
                    double imgAspectRatio = (double)imageInfos[i].Width / imageInfos[i].Height;
                    if (imgAspectRatio >= remainingAspectRatio) {
                        imageCollectionPanelVMs.Add(new() { ImageInfos = imageInfos[currentRange], PageIndex = pageIndex++ });
                        remainingAspectRatio = viewportAspectRatio;
                        currentRange = i..(i + 1);
                    } else {
                        remainingAspectRatio -= imgAspectRatio;
                        currentRange = currentRange.Start..(i + 1);
                    }
                }
                // add last range
                imageCollectionPanelVMs.Add(new() { ImageInfos = imageInfos[currentRange], PageIndex = pageIndex++ });
                ImageCollectionPanelVMs = imageCollectionPanelVMs;
            } else {
                int vmsCount = (int)Math.Ceiling((double)imageInfos.Length / imagesPerPage);
                ImageCollectionPanelVM[] imageCollectionPanelVMs = new ImageCollectionPanelVM[vmsCount];
                for (int i = 0; i < vmsCount; i++) {
                    int startIndex = i * imagesPerPage;
                    int endIndex = Math.Min((i + 1) * imagesPerPage, imageInfos.Length);
                    imageCollectionPanelVMs[i] = new() { ImageInfos = imageInfos[startIndex..endIndex], PageIndex = i };
                }
                ImageCollectionPanelVMs = new(imageCollectionPanelVMs);
            }
        }
    }
}