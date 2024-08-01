﻿using Hitomi_Scroll_Viewer.MainWindowComponent.SearchPageComponent;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.ApplicationModel.Resources;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using static Hitomi_Scroll_Viewer.Resources;
using static Hitomi_Scroll_Viewer.Entities.TagFilter;
using static Hitomi_Scroll_Viewer.Utils;
using Hitomi_Scroll_Viewer.Entities;

namespace Hitomi_Scroll_Viewer.MainWindowComponent
{
    public sealed partial class SearchPage : Page {
        private static readonly ResourceMap _resourceMap = MainResourceMap.GetSubtree("SearchPage");

        private static readonly string BOOKMARK_NUM_PER_PAGE_SETTING_KEY = "BookmarkNumPerPage";
        private readonly ApplicationDataContainer _settings;

        private static readonly string SEARCH_ADDRESS = "https://hitomi.la/search.html?";
        private static readonly Range GALLERY_ID_LENGTH_RANGE = 6..7;

        private readonly IEnumerable<int> _bookmarkNumPerPageRange = Enumerable.Range(1, 8);
        internal static readonly List<BookmarkItem> BookmarkItems = [];
        private static readonly Dictionary<string, BookmarkItem> BookmarkDict = [];
        private static readonly object _bmLock = new();

        internal enum BookmarkSwapDirection {
            Up, Down
        }

        private static readonly DataPackage _myDataPackage = new() {
            RequestedOperation = DataPackageOperation.Copy
        };

        private readonly ObservableCollection<SearchLinkItem> _searchLinkItems = [];

        internal readonly ObservableCollection<DownloadItem> DownloadingItems = [];
        internal static readonly ConcurrentDictionary<string, byte> DownloadingGalleryIds = [];

        private string _currTagFilterName;

        private readonly ContentDialog _confirmDialog = new() {
            DefaultButton = ContentDialogButton.Primary,
            PrimaryButtonText = TEXT_YES,
            CloseButtonText = TEXT_CANCEL,
            Title = new TextBlock() {
                TextWrapping = TextWrapping.WrapWholeWords
            },
            Content = new TextBlock() {
                TextWrapping = TextWrapping.WrapWholeWords
            }
        };

        public SearchPage() {
            InitializeComponent();

            //_settings = ApplicationData.Current.LocalSettings;
            //BookmarkNumPerPageSelector.SelectedIndex = (int)(_settings.Values[BOOKMARK_NUM_PER_PAGE_SETTING_KEY] ?? 2);

            //if (File.Exists(TAG_FILTERS_FILE_PATH)) {
            //    TagFilterDict = (Dictionary<string, TagFilter>)JsonSerializer.Deserialize(
            //        File.ReadAllText(TAG_FILTERS_FILE_PATH),
            //        typeof(Dictionary<string, TagFilter>),
            //        DEFAULT_SERIALIZER_OPTIONS
            //    );
            //} else {
            //    TagFilterDict = new() {
            //        { EXAMPLE_TAG_FILTER_NAME_1, new() },
            //        { EXAMPLE_TAG_FILTER_NAME_2, new() },
            //        { EXAMPLE_TAG_FILTER_NAME_3, new() },
            //        { EXAMPLE_TAG_FILTER_NAME_4, new() },
            //    };
            //    TagFilterDict[EXAMPLE_TAG_FILTER_NAME_1].IncludeTagFilters["language"].Add("english");
            //    TagFilterDict[EXAMPLE_TAG_FILTER_NAME_1].IncludeTagFilters["tag"].Add("full_color");
            //    TagFilterDict[EXAMPLE_TAG_FILTER_NAME_1].ExcludeTagFilters["type"].Add("gamecg");

            //    TagFilterDict[EXAMPLE_TAG_FILTER_NAME_2].IncludeTagFilters["type"].Add("doujinshi");
            //    TagFilterDict[EXAMPLE_TAG_FILTER_NAME_2].IncludeTagFilters["series"].Add("naruto");
            //    TagFilterDict[EXAMPLE_TAG_FILTER_NAME_2].IncludeTagFilters["language"].Add("korean");

            //    TagFilterDict[EXAMPLE_TAG_FILTER_NAME_3].IncludeTagFilters["series"].Add("blue_archive");
            //    TagFilterDict[EXAMPLE_TAG_FILTER_NAME_3].IncludeTagFilters["female"].Add("sole_female");
            //    TagFilterDict[EXAMPLE_TAG_FILTER_NAME_3].ExcludeTagFilters["language"].Add("chinese");

            //    TagFilterDict[EXAMPLE_TAG_FILTER_NAME_4].ExcludeTagFilters["tag"].Add("non-h_imageset");

            //    WriteTagFilterDict();
            //}
            GalleryIDTextBox.TextChanged += (_, _) => { DownloadButton.IsEnabled = GalleryIDTextBox.Text.Length != 0; };


            void SetConfirmDialogXamlRoot(object _0, RoutedEventArgs _1) {
                _confirmDialog.XamlRoot = XamlRoot;
                Loaded -= SetConfirmDialogXamlRoot;
            };
            Loaded += SetConfirmDialogXamlRoot;
        }

        public async Task<ContentDialogResult> ShowConfirmDialogAsync(string title, string content) {
            (_confirmDialog.Title as TextBlock).Text = title;
            (_confirmDialog.Content as TextBlock).Text = content;
            ContentDialogResult result = await _confirmDialog.ShowAsync();
            return result;
        }

        private static readonly string NOTIFICATION_TAG_FILTER_NAME_EMPTY_TITLE = _resourceMap.GetValue("Notification_TagFilter_NameEmpty_Title").ValueAsString;
        private static readonly string NOTIFICATION_TAG_FILTER_NAME_EMPTY_CONTENT = _resourceMap.GetValue("Notification_TagFilter_NameEmpty_Content").ValueAsString;
        private static readonly string NOTIFICATION_TAG_FILTER_NAME_DUP_TITLE = _resourceMap.GetValue("Notification_TagFilter_NameDup_Title").ValueAsString;
        private static readonly string NOTIFICATION_TAG_FILTER_NAME_DUP_CONTENT = _resourceMap.GetValue("Notification_TagFilter_NameDup_Content").ValueAsString;
        private static readonly string NOTIFICATION_TAG_FILTER_OVERLAP_TITLE = _resourceMap.GetValue("Notification_TagFilter_Overlap_Title").ValueAsString;
        
        private static readonly string NOTIFICATION_TAG_FILTER_CREATE_1_TITLE = _resourceMap.GetValue("Notification_TagFilter_Create_1_Title").ValueAsString;
        private static readonly string NOTIFICATION_TAG_FILTER_CREATE_2_TITLE = _resourceMap.GetValue("Notification_TagFilter_Create_2_Title").ValueAsString;


        private static readonly string NOTIFICATION_TAG_FILTER_RENAME_1_TITLE = _resourceMap.GetValue("Notification_TagFilter_Rename_1_Title").ValueAsString;
        private static readonly string NOTIFICATION_TAG_FILTER_RENAME_2_TITLE = _resourceMap.GetValue("Notification_TagFilter_Rename_2_Title").ValueAsString;


        private static readonly string NOTIFICATION_TAG_FILTER_SAVE_1_TITLE = _resourceMap.GetValue("Notification_TagFilter_Save_1_Title").ValueAsString;
        private static readonly string NOTIFICATION_TAG_FILTER_SAVE_1_CONTENT = _resourceMap.GetValue("Notification_TagFilter_Save_1_Content").ValueAsString;
        private static readonly string NOTIFICATION_TAG_FILTER_SAVE_2_TITLE = _resourceMap.GetValue("Notification_TagFilter_Save_2_Title").ValueAsString;

        private static readonly string NOTIFICATION_TAG_FILTER_DELETE_1_TITLE = _resourceMap.GetValue("Notification_TagFilter_Delete_1_Title").ValueAsString;
        private static readonly string NOTIFICATION_TAG_FILTER_DELETE_2_TITLE = _resourceMap.GetValue("Notification_TagFilter_Delete_2_Title").ValueAsString;



        private static readonly string NOTIFICATION_SEARCH_LINK_CONTENT_EMPTY_TITLE = _resourceMap.GetValue("Notification_SeachLink_ContentEmpty_Title").ValueAsString;

        private void CreateHyperlinkBtn_Clicked(object _0, RoutedEventArgs _1) {
            string selectedTagFilterSetName = TagFilterSetControl.TagFilterSetEditor.GetSelectedTagFilterSetName();
            List<string> includeTagFilterSetNames = TagFilterSetControl.IncludeTagFilterSetSelector.GetCheckedTagFilterSetNames();
            List<string> excludeTagFilterSetNames = TagFilterSetControl.IncludeTagFilterSetSelector.GetCheckedTagFilterSetNames();
            if (selectedTagFilterSetName == null) {

            } else {

            }
            var selectedSearchFilterItems = _searchFilterItems.Where(item => (bool)item.IsChecked);
            TagFilter combinedTagFilter = selectedSearchFilterItems.Aggregate(
                new TagFilter(),
                (result, item) => {
                    TagFilter currTagFilter = TagFilterDict[item.TagFilterName];
                    foreach (string category in CATEGORIES) {
                        result.IncludeTagFilters[category] = result.IncludeTagFilters[category].Union(currTagFilter.IncludeTagFilters[category]).ToHashSet();
                        result.ExcludeTagFilters[category] = result.ExcludeTagFilters[category].Union(currTagFilter.ExcludeTagFilters[category]).ToHashSet();
                    }
                    return result;
                }
            );

            string overlappingTagFiltersText = combinedTagFilter.GetIncludeExcludeOverlap();
            if (overlappingTagFiltersText != "") {
                MainWindow.NotifyUser(NOTIFICATION_TAG_FILTER_OVERLAP_TITLE, overlappingTagFiltersText);
                return;
            }

            string searchParams = string.Join(
                ' ',
                CATEGORIES.Select(category =>
                    (
                        string.Join(' ', combinedTagFilter.IncludeTagFilters[category].Select(tag => category + ':' + tag.Replace(' ', '_')))
                            + ' ' +
                        string.Join(' ', combinedTagFilter.ExcludeTagFilters[category].Select(tag => '-' + category + ':' + tag.Replace(' ', '_')))
                    ).Trim()
                ).Where(searchParam => searchParam != "")
            );
            if (searchParams == "") {
                MainWindow.NotifyUser(NOTIFICATION_SEARCH_LINK_CONTENT_EMPTY_TITLE, "");
                return;
            }
            string searchLink = SEARCH_ADDRESS + searchParams;

            string displayText = string.Join(
                Environment.NewLine,
                CATEGORIES.Select(category => {
                    string displayTextPart =
                        (
                            string.Join(' ', combinedTagFilter.IncludeTagFilters[category].Select(tag => tag.Replace(' ', '_')))
                            + ' ' +
                            string.Join(' ', combinedTagFilter.ExcludeTagFilters[category].Select(tag => '-' + tag.Replace(' ', '_')))
                        ).Trim();
                    if (displayTextPart != "") {
                        return char.ToUpper(category[0]) + category[1..] + ": " + displayTextPart;
                    } else {
                        return "";
                    }
                }).Where(displayTagTexts => displayTagTexts != "")
            );
            
            _searchLinkItems.Add(new(
                searchLink,
                displayText,
                (_, arg) => _searchLinkItems.Remove((SearchLinkItem)arg.Parameter)
            ));
            // copy link to clipboard
            _myDataPackage.SetText(searchLink);
            Clipboard.SetContent(_myDataPackage);
        }

        private static readonly string NOTIFICATION_GALLERY_ID_TEXTBOX_CONTENT_EMPTY_TITLE = _resourceMap.GetValue("Notification_GalleryIDTextBox_ContentEmpty_Title").ValueAsString;
        private static readonly string NOTIFICATION_GALLERY_ID_TEXTBOX_CONTENT_INVALID_TITLE = _resourceMap.GetValue("Notification_GalleryIDTextBox_ContentInvalid_Title").ValueAsString;
        private static readonly string NOTIFICATION_GALLERY_ID_TEXTBOX_CONTENT_INVALID_CONTENT = _resourceMap.GetValue("Notification_GalleryIDTextBox_ContentInvalid_Content").ValueAsString;

        private void DownloadBtn_Clicked(object _0, RoutedEventArgs _1) {
            string idPattern = @"\d{" + GALLERY_ID_LENGTH_RANGE.Start + "," + GALLERY_ID_LENGTH_RANGE.End + "}";
            string[] urlOrIds = GalleryIDTextBox.Text.Split(NEW_LINE_SEPS, DEFAULT_STR_SPLIT_OPTIONS);
            if (urlOrIds.Length == 0) {
                MainWindow.NotifyUser(NOTIFICATION_GALLERY_ID_TEXTBOX_CONTENT_EMPTY_TITLE, "");
                return;
            }
            List<string> extractedIds = [];
            foreach (string urlOrId in urlOrIds) {
                MatchCollection matches = Regex.Matches(urlOrId, idPattern);
                if (matches.Count > 0) {
                    extractedIds.Add(matches.Last().Value);
                }
            }
            if (extractedIds.Count == 0) {
                MainWindow.NotifyUser(
                    NOTIFICATION_GALLERY_ID_TEXTBOX_CONTENT_INVALID_TITLE,
                    NOTIFICATION_GALLERY_ID_TEXTBOX_CONTENT_INVALID_CONTENT
                );
                return;
            }
            GalleryIDTextBox.Text = "";
            
            // only download if the gallery is not already downloading
            foreach (string extractedId in extractedIds) {
                TryDownload(extractedId);
            }
        }

        internal bool TryDownload(string id, BookmarkItem bookmarkItem = null) {
            if (DownloadingGalleryIds.TryAdd(id, 0)) {
                DownloadingItems.Add(new(id, bookmarkItem));
                return true;
            }
            return false;
        }

        //public BookmarkItem AddBookmark(Gallery gallery) {
        //    lock (_bmLock) {
        //        // return the BookmarkItem if it is already bookmarked
        //        if (BookmarkDict.TryGetValue(gallery.id, out BookmarkItem bmItem)) {
        //            return bmItem;
        //        }
        //        bmItem = new BookmarkItem(gallery, true);
        //        BookmarkItems.Add(bmItem);
        //        BookmarkDict.Add(gallery.id, bmItem);
        //        // new page is needed
        //        if (BookmarkItems.Count % (BookmarkNumPerPageSelector.SelectedIndex + 1) == 1) {
        //            BookmarkPageSelector.Items.Add(BookmarkPageSelector.Items.Count + 1);
        //        }
        //        WriteObjectToJson(BOOKMARKS_FILE_PATH, BookmarkItems.Select(bmItem => bmItem.gallery));
        //        if (BookmarkItems.Count == 1) {
        //            BookmarkPageSelector.SelectedIndex = 0;
        //        } else {
        //            UpdateBookmarkLayout();
        //        }
        //        return bmItem;
        //    }
        //}

        //public void RemoveBookmark(BookmarkItem bmItem) {
        //    lock (_bmLock) {
        //        string path = Path.Combine(IMAGE_DIR, bmItem.gallery.id);
        //        if (Directory.Exists(path)) Directory.Delete(path, true);
        //        BookmarkItems.Remove(bmItem);
        //        BookmarkDict.Remove(bmItem.gallery.id);
        //        WriteObjectToJson(BOOKMARKS_FILE_PATH, BookmarkItems.Select(bmItem => bmItem.gallery));

        //        bool pageChanged = false;
        //        // a page needs to be removed
        //        if (BookmarkItems.Count % (BookmarkNumPerPageSelector.SelectedIndex + 1) == 0) {
        //            // if current page is the last page
        //            if (BookmarkPageSelector.SelectedIndex == BookmarkPageSelector.Items.Count - 1) {
        //                pageChanged = true;
        //                BookmarkPageSelector.SelectedIndex = 0;
        //            }
        //            BookmarkPageSelector.Items.RemoveAt(BookmarkPageSelector.Items.Count - 1);
        //        }
        //        // don't call UpdateBookmarkLayout again if BookmarkPageSelector.SelectedIndex is set to 0 because UpdateBookmarkLayout is already called by SelectionChanged event
        //        if (!pageChanged) {
        //            UpdateBookmarkLayout();
        //        }
        //    }
        //}

        //internal void SwapBookmarks(BookmarkItem bmItem, BookmarkSwapDirection dir) {
        //    lock (_bmLock) {
        //        int idx = BookmarkItems.FindIndex(item => item.gallery.id == bmItem.gallery.id);
        //        switch (dir) {
        //            case BookmarkSwapDirection.Up: {
        //                if (idx == 0) {
        //                    return;
        //                }
        //                (BookmarkItems[idx], BookmarkItems[idx - 1]) = (BookmarkItems[idx - 1], BookmarkItems[idx]);
        //                break;
        //            }
        //            case BookmarkSwapDirection.Down: {
        //                if (idx == BookmarkItems.Count - 1) {
        //                    return;
        //                }
        //                (BookmarkItems[idx], BookmarkItems[idx + 1]) = (BookmarkItems[idx + 1], BookmarkItems[idx]);
        //                break;
        //            }
        //        }
        //        WriteObjectToJson(BOOKMARKS_FILE_PATH, BookmarkItems.Select(bmItem => bmItem.gallery));
        //        UpdateBookmarkLayout();
        //    }
        //}

        //private void UpdateBookmarkLayout() {
        //    BookmarkPanel.Children.Clear();
        //    int page = BookmarkPageSelector.SelectedIndex;
        //    if (page < 0) {
        //        return;
        //    }
        //    int bookmarkNumPerPage = BookmarkNumPerPageSelector.SelectedIndex + 1;
        //    for (int i = page * bookmarkNumPerPage; i < Math.Min((page + 1) * bookmarkNumPerPage, BookmarkItems.Count); i++) {
        //        BookmarkPanel.Children.Add(BookmarkItems[i]);
        //    }
        //}

        //private void BookmarkNumPerPageSelector_SelectionChanged(object _0, SelectionChangedEventArgs arg) {
        //    if (arg.AddedItems.Count == 0 || BookmarkItems.Count == 0) {
        //        return;
        //    }
        //    BookmarkPageSelector.Items.Clear();
        //    int numOfPages = (int)Math.Ceiling((double)BookmarkItems.Count / (BookmarkNumPerPageSelector.SelectedIndex + 1));
        //    for (int i = 0; i < numOfPages; i++) {
        //        BookmarkPageSelector.Items.Add(i + 1);
        //    }
        //    BookmarkPageSelector.SelectedIndex = 0;
        //}

        //internal void SaveSettings() {
        //    _settings.Values[BOOKMARK_NUM_PER_PAGE_SETTING_KEY] = BookmarkNumPerPageSelector.SelectedIndex;
        //}
    }
}
