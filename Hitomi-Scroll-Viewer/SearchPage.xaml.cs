﻿using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI;
using static Hitomi_Scroll_Viewer.ImageWatchingPage;
using static Hitomi_Scroll_Viewer.MainWindow;
using static Hitomi_Scroll_Viewer.Tag;

namespace Hitomi_Scroll_Viewer {
    public sealed partial class SearchPage : Page {
        public static readonly string BASE_DOMAIN = "https://hitomi.la/search.html?";
        public static readonly int[] GALLERY_ID_LENGTH_RANGE = new int[] { 6, 7 };
        public static readonly JsonSerializerOptions _serializerOptions = new() { IncludeFields = true, WriteIndented = true };

        public static readonly string BM_INFO_FILE_NAME = "BookmarkInfo.json";

        public static readonly string TAG_FILE_PATH = "Tag.json";
        private static Dictionary<string, Tag> _tag;
        
        public static readonly double THUMBNAIL_IMG_WIDTH = 350;
        public static readonly int THUMBNAIL_IMG_NUM = 3;
        public static readonly int MAX_BOOKMARK_PER_PAGE = 3;
        public static readonly int MAX_BOOKMARK_PAGE = 5;
        private static readonly List<Grid> _bookmarkGrids = new(MAX_BOOKMARK_PER_PAGE * MAX_BOOKMARK_PAGE);
        private static int _currBookmarkPage = 0;

        /* TODO
         * global tag list which gets applied for every URL generated
         * disable remove and rename button when the current selected tag list is global tag list
         * 
         * add default view mode view (view one page at a time) and auto page turning
         * 
         * confirm on save, remove, clear
         * 
         * Save confirm:
         * Save current tags on "Tag List Name"?
         * The tags on "Tag List Name" will be overwritten.
         * 
         * Remove confirm:
         * Remove tag list "Tag List Name"?
         * "Tag List Name" will be deleted and cannot be recovered.
         * 
         * 
         */
        private static readonly TagContainer[] _tagContainers = new TagContainer[2];

        private static readonly DataPackage _myDataPackage = new() {
            RequestedOperation = DataPackageOperation.Copy
        };

        private static MainWindow _mw;

        public SearchPage(MainWindow mainWindow) {
            InitializeComponent();
            InitLayout();
            _mw = mainWindow;

            // create tag file if it doesn't exist
            if (!File.Exists(TAG_FILE_PATH)) {
                Tag defaultTag = new();
                defaultTag.includeTags["tag"] = new string[] { "non-h_imageset" };
                File.WriteAllText(TAG_FILE_PATH, JsonSerializer.Serialize(defaultTag, _serializerOptions));
            }
            // read tag info from file
            _tag = (Dictionary<string, Tag>)JsonSerializer.Deserialize(File.ReadAllText(TAG_FILE_PATH), typeof(Dictionary<string, Tag>), _serializerOptions);
            // add tags to TagListComboBox
            if (_tag.Count > 0) {
                foreach (KeyValuePair<string, Tag> item in _tag) {
                    TagListComboBox.Items.Add(item.Key);
                }
                TagListComboBox.SelectedIndex = 0;
            }

            // create bookmarked galleries' info file if it doesn't exist
            if (!File.Exists(BM_INFO_FILE_NAME)) {
                File.WriteAllText(BM_INFO_FILE_NAME, JsonSerializer.Serialize(new List<Gallery>(), _serializerOptions));
            }
            // read bookmarked galleries' info from file
            BMGalleries =
                (List<Gallery>)JsonSerializer.Deserialize(
                File.ReadAllText(BM_INFO_FILE_NAME),
                typeof(List<Gallery>),_serializerOptions);

            // create image storing directory if it doesn't exist
            Directory.CreateDirectory(IMAGE_DIR);
        }

        private void InitLayout() {
            // RootGrid
            int ROOT_GRID_ROW_NUM = 3;
            int ROOT_GRID_COLUMN_NUM = 3;

            for (int i = 0; i < ROOT_GRID_ROW_NUM; i++) {
                RootGrid.RowDefinitions.Add(new RowDefinition());
            }
            for (int i = 0; i < ROOT_GRID_COLUMN_NUM; i++) {
                RootGrid.ColumnDefinitions.Add(new ColumnDefinition());
            }

            // TagContainerGrid
            int TAG_GRID_ROW_NUM = 12;
            int TAG_GRID_COLUMN_NUM = _tagContainers.Length;

            for (int i = 0; i < TAG_GRID_ROW_NUM; i++) {
                TagContainerGrid.RowDefinitions.Add(new RowDefinition());
            }
            for (int i = 0; i < TAG_GRID_COLUMN_NUM; i++) {
                TagContainerGrid.ColumnDefinitions.Add(new ColumnDefinition());
            }
            Grid.SetColumnSpan(TagContainerGrid, ROOT_GRID_COLUMN_NUM);

            // tag containers
            _tagContainers[0] = new(false, "Include", Colors.Green, TAG_GRID_ROW_NUM - 1);
            _tagContainers[1] = new(true, "Exclude", Colors.Red, TAG_GRID_ROW_NUM - 1);
            for (int i = 0; i < _tagContainers.Length; i++) {
                Grid.SetColumn(_tagContainers[i], i);
                TagContainerGrid.Children.Add(_tagContainers[i]);
            }

            // for space (margin) between TagContainerGrid and row below
            Grid marginGrid = new() {
                Height = 30
            };
            Grid.SetRow(marginGrid, TAG_GRID_ROW_NUM - 1);
            Grid.SetColumnSpan(marginGrid, TAG_GRID_COLUMN_NUM);
            TagContainerGrid.Children.Add(marginGrid);

            // HyperlinkGrid
            int HYPERLINK_GRID_ROW_NUM = 8;
            int HYPERLINK_GRID_COLUMN_NUM = 12;

            for (int i = 0; i < HYPERLINK_GRID_ROW_NUM; i++) {
                HyperlinkGrid.RowDefinitions.Add(new RowDefinition());
            }
            for (int i = 0; i < HYPERLINK_GRID_COLUMN_NUM; i++) {
                HyperlinkGrid.ColumnDefinitions.Add(new ColumnDefinition());
            }

            // HyperlinkGrid Children
            foreach (FrameworkElement elem in HyperlinkGrid.Children.Cast<FrameworkElement>()) {
                Grid.SetColumnSpan(elem, HYPERLINK_GRID_COLUMN_NUM - 2);
                Grid.SetColumn(elem, 1);
                elem.VerticalAlignment = VerticalAlignment.Top;
                elem.HorizontalAlignment = HorizontalAlignment.Stretch;
            }

            // GenerateHyperlinkBtn
            Grid.SetRowSpan(GenerateHyperlinkBtn, 1);

            // GeneratedHyperlinks
            Grid.SetRowSpan(GeneratedHyperlinks, HYPERLINK_GRID_ROW_NUM - 1);

            // LinkInputGrid
            int LINK_INPUT_GRID_COLUMN_NUM = 12;
            for (int i = 0; i < LINK_INPUT_GRID_COLUMN_NUM; i++) {
                LinkInputGrid.ColumnDefinitions.Add(new ColumnDefinition());
            }

            // LinkInputGrid Children
            foreach (FrameworkElement elem in LinkInputGrid.Children.Cast<FrameworkElement>()) {
                elem.HorizontalAlignment = HorizontalAlignment.Stretch;
            }

            // GalleryIDTextBox
            int GALLERY_ID_TEXTBOX_COLUMN = 1;
            Grid.SetColumn(GalleryIDTextBox, GALLERY_ID_TEXTBOX_COLUMN);
            int GALLERY_ID_TEXTBOX_COLUMN_SPAN = 2 * (LINK_INPUT_GRID_COLUMN_NUM - 3) / 3;
            Grid.SetColumnSpan(GalleryIDTextBox, GALLERY_ID_TEXTBOX_COLUMN_SPAN);

            // LoadImageBtn
            int LOAD_IMAGE_BTN_COLUMN = GALLERY_ID_TEXTBOX_COLUMN + GALLERY_ID_TEXTBOX_COLUMN_SPAN + 1;
            Grid.SetColumn(LoadImageBtn, LOAD_IMAGE_BTN_COLUMN);
            int LOAD_IMAGE_BTN_COLUMN_SPAN = 1 * (LINK_INPUT_GRID_COLUMN_NUM - 3) / 3;
            Grid.SetColumnSpan(LoadImageBtn, LOAD_IMAGE_BTN_COLUMN_SPAN);

            // TagControlGrid
            int TAG_CONTROL_GRID_ROW_NUM = 16;
            int TAG_CONTROL_GRID_COLUMN_NUM = 16;

            for (int i = 0; i < TAG_CONTROL_GRID_ROW_NUM; i++) {
                TagControlGrid.RowDefinitions.Add(new RowDefinition());
            }
            for (int i = 0; i < TAG_CONTROL_GRID_COLUMN_NUM; i++) {
                TagControlGrid.ColumnDefinitions.Add(new ColumnDefinition());
            }

            // Create Tag Control Buttons
            int currRow = 0;

            Button createTagBtn = CreateTagControlBtn("Create a New Tag List", Colors.Blue, currRow, 2);
            createTagBtn.Click += CreateTag;
            TagControlGrid.Children.Add(createTagBtn);
            currRow += Grid.GetRowSpan(createTagBtn) + 1;

            Button renameTagBtn = CreateTagControlBtn("Rename Current Tag List", Colors.Orange, currRow, 2);
            renameTagBtn.Click += RenameTag;
            TagControlGrid.Children.Add(renameTagBtn);
            currRow += Grid.GetRowSpan(renameTagBtn) + 1;

            Button saveTagBtn = CreateTagControlBtn("Save Current Tag List", Colors.Green, currRow, 4);
            saveTagBtn.Click += SaveTag;
            TagControlGrid.Children.Add(saveTagBtn);
            currRow += Grid.GetRowSpan(saveTagBtn) + 1;

            Button removeTagBtn = CreateTagControlBtn("Remove Current Tag List", Colors.Red, currRow, 2);
            removeTagBtn.Click += RemoveTag;
            TagControlGrid.Children.Add(removeTagBtn);
            currRow += Grid.GetRowSpan(removeTagBtn) + 1;

            Button ClearTagTextBoxesBtn = CreateTagControlBtn("Clear Current Tags", Colors.Black, currRow, 2);
            ClearTagTextBoxesBtn.Click += ConfirmClear;
            TagControlGrid.Children.Add(ClearTagTextBoxesBtn);


            // TagControlGrid Children
            foreach (FrameworkElement elem in TagControlGrid.Children.Cast<FrameworkElement>()) {
                elem.HorizontalAlignment = HorizontalAlignment.Stretch;
            }

            // BookmarkGrid
            for (int i = 0; i < MAX_BOOKMARK_PER_PAGE; i++) {
                BookmarkGrid.RowDefinitions.Add(new RowDefinition());
            }

            // BookmarkPageBtns
            for (int i = 0; i < MAX_BOOKMARK_PAGE; i++) {
                Button pageNumBtn = new() {
                    Content = new TextBlock() {
                        Text = (i + 1).ToString(),
                        FontSize = 24,
                        Margin = new Thickness(6, 0, 6, 0)
                    },
                    Margin = new Thickness(18, 0, 18, 0)
                };
                pageNumBtn.Click += ChangeBookmarkPage;
                BookmarkPageBtnsPanel.Children.Add(pageNumBtn);
            }
        }

        /*               
                <Button x:Name="CreateBtn"
                        Click="CreateTag"
                        Grid.Row="0" Grid.RowSpan="2"
                        Grid.Column="8" Grid.ColumnSpan="7" VerticalAlignment="Stretch"
                        BorderBrush="Blue"
                        >
                    <TextBlock Text="Create New Tag List" TextWrapping="WrapWholeWords"/>
                </Button>
         */
        private static Button CreateTagControlBtn(string text, Color borderColor, int row, int rowSpan) {
            Button btn = new() {
                Content = new TextBlock() {
                    Text = text,
                    TextWrapping = TextWrapping.WrapWholeWords
                },
                BorderBrush = new SolidColorBrush(borderColor),
                VerticalAlignment = VerticalAlignment.Stretch
            };
            Grid.SetRow(btn, row);
            Grid.SetRowSpan(btn, rowSpan);
            Grid.SetColumn(btn, 8);
            Grid.SetColumnSpan(btn, 7);
            return btn;
        }

        public async void Init() {
            for (int i = 0; i < BMGalleries.Count; i++) {
                await CreateBookmarkGrid(i);
            }
            FillBookmarkGrid();
        }

        private static string GetSearchAddress() {
            string param = "";
            for (int i = 0; i < CATEGORIES.Length; i++) {
                param += _tagContainers[0].GetTagParameters(i);
                param += _tagContainers[1].GetTagParameters(i);
            }
            return BASE_DOMAIN + param;
        }

        private static string GetHyperlinkDisplayText() {
            string linkText = "";
            string tagTexts;

            for (int i = 0; i < CATEGORIES.Length; i++) {
                tagTexts = "";
                tagTexts += _tagContainers[0].GetTagStrings(i);
                tagTexts += _tagContainers[1].GetTagStrings(i);

                // if tag textbox is not empty
                if (tagTexts.Length > 0) {
                    linkText += CATEGORIES[i] + ": " + tagTexts;
                    linkText += Environment.NewLine;
                }
            }
            return linkText;
        }

        private static Tag GetCurrTag() {
            Tag tag = new();
            for (int i = 0; i < CATEGORIES.Length; i++) {
                tag.includeTags = _tagContainers[0].GetTags();
                tag.excludeTags = _tagContainers[1].GetTags();
            }
            return tag;
        }

        private void LoadTagsInTextBox(object sender, SelectionChangedEventArgs _) {
            ComboBox tagList = sender as ComboBox;
            if (tagList.SelectedIndex == -1) {
                return;
            }
            string selectedTagString = tagList.SelectedItem as string;
            Tag selectedTag = _tag[selectedTagString];
            for (int i = 0; i < CATEGORIES.Length; i++) {
                _tagContainers[0].InsertTags(selectedTag.includeTags);
                _tagContainers[1].InsertTags(selectedTag.excludeTags);
            }
        }

        private void CreateTag(object sender, RoutedEventArgs e) {
            string tagName = TagNameTextBox.Text.Trim();
            TagNameTextBox.Text = "";
            if (tagName.Length == 0) {
                _mw.AlertUser("No Tag Name", "Please enter a tag name");
                return;
            }
            foreach (string item in TagListComboBox.Items.Cast<string>()) {
                if (item == tagName) {
                    _mw.AlertUser("Same Tag Name", "A tag list with the same name already exists");
                    return;
                }
            }
            _tag.Add(tagName, GetCurrTag());
            TagListComboBox.Items.Add(tagName);
            TagListComboBox.SelectedItem = tagName;

            SaveTagInfo();
        }

        private void RenameTag(object sender, RoutedEventArgs e) {
            if (TagListComboBox.SelectedIndex == -1) {
                _mw.AlertUser("No Tags Selected", "There is no tag list selected currently.");
                return;
            }
            string tagName = TagNameTextBox.Text.Trim();
            TagNameTextBox.Text = "";
            if (tagName.Length == 0) {
                _mw.AlertUser("No Tag Name", "Please enter a tag name");
                return;
            }
            foreach (string item in TagListComboBox.Items.Cast<string>()) {
                if (item == tagName) {
                    _mw.AlertUser("Same Tag Name", "A tag list with the same name already exists");
                    return;
                }
            }
            string selectedItem = TagListComboBox.SelectedItem as string;
            _tag.Add(tagName, _tag[selectedItem]);
            _tag.Remove(selectedItem);
            TagListComboBox.Items.Add(tagName);
            TagListComboBox.SelectedItem = tagName;
            TagListComboBox.Items.Remove(selectedItem);

            SaveTagInfo();
        }

        private void SaveTag(object sender, RoutedEventArgs e) {
            if (TagListComboBox.SelectedIndex == -1) {
                _mw.AlertUser("No Tags Selected", "There is no tag list selected currently.");
                return;
            }
            string selectedString = TagListComboBox.SelectedItem as string;
            _tag[selectedString] = GetCurrTag();
            SaveTagConfirmationTextBlock.Text = '"' + selectedString + '"' + " was saved successfully.";

            SaveTagInfo();
        }

        private void RemoveTag(object sender, RoutedEventArgs e) {
            if (TagListComboBox.SelectedIndex == -1) {
                _mw.AlertUser("No Tags Selected", "There is no tag list selected currently.");
                return;
            }
            string selectedItem = TagListComboBox.SelectedItem as string;
            _tag.Remove(selectedItem);
            TagListComboBox.Items.Remove(selectedItem);
            TagListComboBox.SelectedIndex = 0;

            SaveTagInfo();
        }

        public async void ConfirmClear(object sender, RoutedEventArgs e) {
            ContentDialog dialog = new() {
                Title = "Clear all tags in text box?",
                IsPrimaryButtonEnabled = true,
                PrimaryButtonText = "Yes",
                CloseButtonText = "Cancel",
                XamlRoot = XamlRoot
            };
            dialog.PrimaryButtonClick += ClearTagTextboxes;

            // TODO call ClearTagTextboxes() on button click
            await dialog.ShowAsync();
        }

        private static void ClearTagTextboxes(ContentDialog cd, ContentDialogButtonClickEventArgs e) {
            _tagContainers[0].Clear();
            _tagContainers[1].Clear();
        }

        public static void SaveTagInfo() {
            File.WriteAllText(TAG_FILE_PATH, JsonSerializer.Serialize(_tag, _serializerOptions));
        }

        public static void SaveBookmarkInfo() {
            File.WriteAllText(BM_INFO_FILE_NAME, JsonSerializer.Serialize(BMGalleries, _serializerOptions));
        }

        private void GenerateHyperlink(object sender, RoutedEventArgs e) {
            string address = GetSearchAddress();
            // copy link to clipboard
            _myDataPackage.SetText(address);
            Clipboard.SetContent(_myDataPackage);

            Grid gd = new();
            for (int i = 0; i < 12; i++) {
                gd.ColumnDefinitions.Add(new ColumnDefinition());
            }

            HyperlinkButton hb = new() {
                Content = new TextBlock() {
                    Text = GetHyperlinkDisplayText(),
                    TextWrapping = TextWrapping.WrapWholeWords,
                    FontSize = 10,
                },
                NavigateUri = new Uri(address),
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };

            Grid.SetColumn(hb, 0);
            Grid.SetColumnSpan(hb, 10);
            gd.Children.Add(hb);
            
            Button btn = new() {
                Content = new TextBlock() {
                    Text = "Remove",
                    TextWrapping = TextWrapping.WrapWholeWords,
                },
                FontSize = 12,
            };
            btn.Click += RemoveHyperlink;
            Grid.SetColumn(btn, 10);
            Grid.SetColumnSpan(btn, 2);
            gd.Children.Add(btn);
            
            GeneratedHyperlinks.Children.Add(gd);

        }

        private void RemoveHyperlink(object sender, RoutedEventArgs e) {
            Button btn = sender as Button;
            Grid parent = btn.Parent as Grid;
            GeneratedHyperlinks.Children.Remove(parent);
        }

        private async void HandleGalleryIDSubmitKeyDown(object sender, KeyRoutedEventArgs e) {
            if (e.Key == Windows.System.VirtualKey.Enter) {
                await LoadGalleryFromId();
            }
        }

        private async void HandleLoadImageBtnClick(object sender, RoutedEventArgs e) {
            await LoadGalleryFromId();
        }

        private void HandleBookmarkClick(object sender, RoutedEventArgs e) {
            Grid bmGrid = (sender as HyperlinkButton).Parent as Grid;
            int idx = BookmarkGrid.Children.IndexOf(bmGrid) + _currBookmarkPage * MAX_BOOKMARK_PER_PAGE;

            // if gallery is already loaded
            if (gallery != null) {
                if (BMGalleries[idx].id == gallery.id) {
                    _mw.SwitchPage();
                    return;
                }
            }

            LoadGalleryFromBookmark(idx);

        }

        private async Task LoadGalleryFromId() {
            string id = ExtractGalleryId();
            if (string.IsNullOrEmpty(id)) {
                _mw.AlertUser("Invalid ID or URL", "Please enter a valid ID or URL");
                return;
            }
            GalleryIDTextBox.Text = "";

            // if gallery is already loaded
            if (gallery != null) {
                if (id == gallery.id) {
                    _mw.SwitchPage();
                    return;
                }
            }

            // if gallery is already bookmarked
            for (int i = 0; i < BMGalleries.Count; i++) {
                if (BMGalleries[i].id == id) {
                    LoadGalleryFromBookmark(i);
                    return;
                }
            }
            await _mw.iwp.LoadImagesFromWeb(id);
        }

        private string ExtractGalleryId() {
            string regex = @"\d{"+ GALLERY_ID_LENGTH_RANGE[0] + "," + GALLERY_ID_LENGTH_RANGE[1] + "}";
            MatchCollection matches = Regex.Matches(GalleryIDTextBox.Text, regex);
            if (matches.Count == 0) {
                return "";
            }
            return matches[^1].Value;
        }

        private static async void LoadGalleryFromBookmark(int idx) {
            await _mw.iwp.LoadGalleryFromLocalDir(idx);
        }

        private async Task CreateBookmarkGrid(int idx) {
            int rowSpan = 6;
            int columnSpan = 13;

            Grid gr = new();
            for (int i = 0; i < rowSpan; i++) {
                gr.RowDefinitions.Add(new RowDefinition());
            }
            for (int i = 0; i < columnSpan; i++) {
                gr.ColumnDefinitions.Add(new ColumnDefinition());
            }
            gr.BorderBrush = new SolidColorBrush(Colors.Black);
            gr.BorderThickness = new Thickness(1);

            HyperlinkButton hb = new() {
                Content = new TextBlock() {
                    Text = BMGalleries[idx].title + "\n" + BMGalleries[idx].id,
                    TextWrapping = TextWrapping.WrapWholeWords,
                    FontSize = 24,
                },
            };
            hb.Click += HandleBookmarkClick;

            Grid.SetRow(hb, 0);
            Grid.SetRowSpan(hb, 1);
            Grid.SetColumn(hb, 0);
            Grid.SetColumnSpan(hb, columnSpan - 1);
            gr.Children.Add(hb);

            try {
                string path = IMAGE_DIR + @"\" + BMGalleries[idx].id;
                int imgIdx;
                for (int i = 0; i < THUMBNAIL_IMG_NUM; i++) {
                    imgIdx = i * BMGalleries[idx].files.Count / THUMBNAIL_IMG_NUM;
                    Image img = new() {
                        Source = await GetBitmapImage(await File.ReadAllBytesAsync(path + @"\" + imgIdx.ToString())),
                        Width = THUMBNAIL_IMG_WIDTH,
                        Height = THUMBNAIL_IMG_WIDTH * BMGalleries[idx].files[i].height / BMGalleries[idx].files[i].width,
                    };

                    Grid.SetRow(img, 1);
                    Grid.SetRowSpan(img, rowSpan - 1);
                    Grid.SetColumn(img, i * (columnSpan - 1) / THUMBNAIL_IMG_NUM);
                    Grid.SetColumnSpan(img, (columnSpan - 1) / THUMBNAIL_IMG_NUM);
                    gr.Children.Add(img);

                }
            }
            catch (DirectoryNotFoundException) {
                Debug.WriteLine("Image directory for " + BMGalleries[idx].title + " (" + BMGalleries[idx].id + ") not found");
                (hb.Content as TextBlock).Text = BMGalleries[idx].title + "\n" + BMGalleries[idx].id + "\n" + "Image directory not found";
                hb.IsEnabled = false;
            }

            Button btn = new() {
                Content = new TextBlock() {
                    Text = "Remove",
                    TextWrapping = TextWrapping.WrapWholeWords,
                },
                FontSize = 18,
            };
            btn.Click += RemoveBookmark;
            Grid.SetRow(btn, 0);
            Grid.SetRowSpan(btn, rowSpan);
            Grid.SetColumn(btn, columnSpan - 1);
            Grid.SetColumnSpan(btn, 1);
            gr.Children.Add(btn);

            _bookmarkGrids.Add(gr);
        }

        private void ShowBookmarkOnGrid(int idx) {
            Grid.SetRow(_bookmarkGrids[idx], idx % MAX_BOOKMARK_PER_PAGE);
            BookmarkGrid.Children.Add(_bookmarkGrids[idx]);
        }

        public async void AddBookmark(object _, RoutedEventArgs e) {
            _mw.iwp.ChangeBookmarkBtnState(LoadingState.Bookmarked);

            BMGalleries.Add(gallery);

            await CreateBookmarkGrid(BMGalleries.Count - 1);

            if (BMGalleries.Count - 1 >= _currBookmarkPage * MAX_BOOKMARK_PER_PAGE && 
                BMGalleries.Count - 1 < (_currBookmarkPage + 1) * MAX_BOOKMARK_PER_PAGE) {
                ShowBookmarkOnGrid(BMGalleries.Count - 1);
            }

            SaveBookmarkInfo();
        }

        private void RemoveBookmark(object sender, RoutedEventArgs e) {
            Button btn = sender as Button;
            Grid bmGrid = btn.Parent as Grid;
            int targetIdx = _bookmarkGrids.IndexOf(bmGrid);

            // remove gallery files
            DeleteGallery(BMGalleries[targetIdx].id);

            // if the removing gallery is the current viewing gallery
            if (gallery != null) {
                if (BMGalleries[targetIdx].id == gallery.id) {
                    _mw.iwp.ChangeBookmarkBtnState(LoadingState.Loaded);
                }
            }

            BMGalleries.RemoveAt(targetIdx);
            _bookmarkGrids.RemoveAt(targetIdx);

            int targetIdxInGrid = targetIdx % MAX_BOOKMARK_PER_PAGE;
            BookmarkGrid.Children.RemoveAt(targetIdxInGrid);

            // number of bookmark grids to re-allocate to new row
            int reallocatingGridNum = MAX_BOOKMARK_PER_PAGE - targetIdxInGrid;
            if (_bookmarkGrids.Count - targetIdx < MAX_BOOKMARK_PER_PAGE) {
                reallocatingGridNum = _bookmarkGrids.Count - targetIdx;
            }

            // reallocate rows of each grid by decrementing the row position by 1
            for (int i = 0; i < reallocatingGridNum; i++) {
                Grid.SetRow(_bookmarkGrids[targetIdx + i], targetIdxInGrid + i);
            }
            
            // if the last bookmark grid is from next page
            if ((targetIdx + reallocatingGridNum)/MAX_BOOKMARK_PER_PAGE == _currBookmarkPage + 1) {
                BookmarkGrid.Children.Add(_bookmarkGrids[targetIdx + reallocatingGridNum - 1]);
            }

            SaveBookmarkInfo();
        }

        private void FillBookmarkGrid() {
            int startingIdx = _currBookmarkPage * MAX_BOOKMARK_PER_PAGE;
            int bookmarkCount = BMGalleries.Count;
            if (startingIdx >= bookmarkCount) {
                return;
            }
            int endingIdx = (_currBookmarkPage + 1) * MAX_BOOKMARK_PER_PAGE;
            if (endingIdx > bookmarkCount) {
                endingIdx = bookmarkCount;
            }
            for (int i = startingIdx; i < endingIdx; i++) {
                ShowBookmarkOnGrid(i);
            }
        }

        private void ChangeBookmarkPage(object sender, RoutedEventArgs e) {
            int targetPageIdx = BookmarkPageBtnsPanel.Children.IndexOf(sender as Button);
            if (_currBookmarkPage == targetPageIdx) {
                return;
            }
            BookmarkGrid.Children.Clear();
            _currBookmarkPage = targetPageIdx;
            FillBookmarkGrid();
        }
    }
}
