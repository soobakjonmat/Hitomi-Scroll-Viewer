using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System.IO;
using Windows.UI.Text;
using static Hitomi_Scroll_Viewer.SearchPage;
using static Hitomi_Scroll_Viewer.Utils;

namespace Hitomi_Scroll_Viewer.SearchPageComponent {
    public sealed partial class BookmarkItem : Grid {
        public readonly Gallery gallery;

        private readonly HyperlinkButton[] _imageWrappers;
        private readonly Image[] _images;
        public BookmarkItem(Gallery newGallery, SearchPage sp) {
            InitializeComponent();

            gallery = newGallery;

            TitleTextBlock.Text = gallery.title;
            string artistsText = gallery.GetArtists();
            if (artistsText == null) {
                ArtistTextBlock.Text = "Artist: N/A";
                ArtistTextBlock.FontStyle = FontStyle.Italic;
            } else {
                ArtistTextBlock.Text = "Artist: " + artistsText;
            }
            IdTextBlock.Text = "ID: " + gallery.id;

            foreach (TextBlock textblock in new[] { TitleTextBlock, ArtistTextBlock, IdTextBlock }) {
                textblock.TextWrapping = TextWrapping.WrapWholeWords;
                textblock.FontSize = 24;
                textblock.IsTextSelectionEnabled = true;
            }

            // add thumbnail images
            _imageWrappers = new HyperlinkButton[THUMBNAIL_IMG_NUM];
            _images = new Image[THUMBNAIL_IMG_NUM];
            string imageDir = Path.Combine(IMAGE_DIR, gallery.id);
            bool dirExists = Directory.Exists(imageDir);
            for (int i = 0; i < THUMBNAIL_IMG_NUM; i++) {
                ImageContainer.ColumnDefinitions.Add(new());
                int idx = i * gallery.files.Length / THUMBNAIL_IMG_NUM;
                _imageWrappers[i] = new() {
                    Content = _images[i] = new(),
                    MaxHeight = THUMBNAIL_IMG_MAXHEIGHT,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                _imageWrappers[i].Click += HandleBookmarkClick;
                if (dirExists) {
                    string[] file = Directory.GetFiles(imageDir, idx.ToString() + ".*");
                    if (file.Length > 0) {
                        _images[i].Source = new BitmapImage(new(file[0]));
                    }
                }
                SetColumn(_imageWrappers[i], i);
                ImageContainer.Children.Add(_imageWrappers[i]);
            }

            RemoveBtn.Click += (_, _) => { sp.RemoveBookmark(this); };
        }

        private void HandleBookmarkClick(object _0, RoutedEventArgs _1) {
            LoadBookmark(gallery);
        }

        public void ReloadImages() {
            string imageDir = Path.Combine(IMAGE_DIR, gallery.id);
            for (int i = 0; i < THUMBNAIL_IMG_NUM; i++) {
                int idx = i * gallery.files.Length / THUMBNAIL_IMG_NUM;
                string[] file = Directory.GetFiles(imageDir, idx.ToString() + ".*");
                if (file.Length > 0) {
                    _images[i].Source = new BitmapImage(new(file[0]));
                }
            }
        }

        public void EnableBookmark(bool enable) {
            foreach (HyperlinkButton imageWrapper in _imageWrappers) {
                imageWrapper.IsEnabled = enable;
            }
        }
    }
}
