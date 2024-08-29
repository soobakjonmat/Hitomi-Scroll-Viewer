﻿using CommunityToolkit.Mvvm.ComponentModel;
using HitomiScrollViewerLib.DbContexts;
using HitomiScrollViewerLib.Entities;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Generic;
using System.Linq;

namespace HitomiScrollViewerLib.ViewModels.SearchPage {
    public partial class GalleryLanguageASBWrapperVM : ObservableObject {
        private const int MAX_SUGGESTION_NUM = 5;
        public GalleryLanguage SelectedGalleryLanguage { get; private set; }

        [ObservableProperty]
        private string _text;

        public IEnumerable<GalleryLanguage> ItemsSource { get; private set; }

        private IEnumerable<GalleryLanguage> GetSuggestions() {
            return
                HitomiContext.Main.GalleryLanguages
                .Where(gl => gl.DisplayName.StartsWith(Text))
                .Take(MAX_SUGGESTION_NUM);
        }

        public void AutoSuggestBox_GotFocus(object _0, RoutedEventArgs _1) {
            ItemsSource = GetSuggestions();
        }

        public void AutoSuggestBox_TextChanged(AutoSuggestBox _0, AutoSuggestBoxTextChangedEventArgs args) {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput) {
                ItemsSource = GetSuggestions();
            }
            if (Text.Length == 0) {
                SelectedGalleryLanguage = null;
            }
        }

        public void AutoSuggestBox_QuerySubmitted(AutoSuggestBox _0, AutoSuggestBoxQuerySubmittedEventArgs args) {
            SelectedGalleryLanguage = args.ChosenSuggestion != null ? args.ChosenSuggestion as GalleryLanguage : null;
        }
    }
}
