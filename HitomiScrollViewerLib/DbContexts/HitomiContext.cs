﻿using CommunityToolkit.WinUI.Collections;
using HitomiScrollViewerLib.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Windows.ApplicationModel.Resources;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Windows.ApplicationModel;
using static HitomiScrollViewerLib.Constants;
using static HitomiScrollViewerLib.SharedResources;

namespace HitomiScrollViewerLib.DbContexts {
    public class HitomiContext : DbContext {
        public DbSet<TagFilter> TagFilters { get; set; }
        public DbSet<Gallery> Galleries { get; set; }
        public DbSet<Tag> Tags { get; set; }
        public DbSet<GalleryLanguage> GalleryLanguages { get; set; }
        public DbSet<GalleryTypeEntity> GalleryTypes { get; set; }
        public DbSet<SortDirectionEntity> SortDirections { get; set; }
        public DbSet<GallerySortEntity> GallerySorts { get; set; }


        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) {
            // db file storage location = Windows.Storage.ApplicationData.Current.LocalFolder.Path
            optionsBuilder.UseSqlite($"Data Source={MAIN_DATABASE_PATH_V3}").EnableSensitiveDataLogging();
        }

        private static readonly string[] ALPHABETS_WITH_123 =
            ["123", .. Enumerable.Range('a', 26).Select(intValue => Convert.ToChar(intValue).ToString())];

        private const string DB_RES_ROOT_DIR = "DatabaseResources";
        private static readonly string DELIMITER_FILE_PATH = Path.Combine(
            Package.Current.InstalledPath,
            DB_RES_ROOT_DIR,
            "delimiter.txt"
        );
        private static readonly string LANGUAGES_FILE_PATH = Path.Combine(
            Package.Current.InstalledPath,
            DB_RES_ROOT_DIR,
            "languages.txt"
        );
        private static readonly Dictionary<TagCategory, string> CATEGORY_DIR_DICT = new() {
            { TagCategory.Tag, "Tags" },
            { TagCategory.Male, "Males" },
            { TagCategory.Female, "Females" },
            { TagCategory.Artist, "Artists" },
            { TagCategory.Group, "Groups" },
            { TagCategory.Character, "Characters" },
            { TagCategory.Series, "Series" }
        };

        public static readonly int DATABASE_INIT_OP_NUM = Tag.TAG_CATEGORIES.Length * ALPHABETS_WITH_123.Length + 1;
        public static event Action<int> DatabaseInitProgressChanged;
        public static event Action ChangeToIndeterminateEvent;
        public static void InitDatabase(HitomiContext context) {
            string delimiter = File.ReadAllText(DELIMITER_FILE_PATH);
            int progressValue = 0;
            // add tags
            foreach (TagCategory category in Tag.TAG_CATEGORIES) {
                string categoryStr = CATEGORY_DIR_DICT[category];
                string dir = Path.Combine(
                    Package.Current.InstalledPath,
                    DB_RES_ROOT_DIR,
                    categoryStr
                );
                foreach (string alphanumStr in ALPHABETS_WITH_123) {
                    string path = Path.Combine(dir, $"{categoryStr.ToLower()}-{alphanumStr}.txt");
                    string[] tagInfoStrs = File.ReadAllLines(path);
                    context.Tags.AddRange(tagInfoStrs.Select(
                        tagInfoStr => {
                            string[] tagInfoArr = tagInfoStr.Split(delimiter);
                            return new Tag() {
                                Category = category,
                                Value = tagInfoArr[0],
                                GalleryCount = int.Parse(tagInfoArr[1])
                            };
                        }
                    ));
                    DatabaseInitProgressChanged?.Invoke(++progressValue);
                }
            }
            // add gallery languages and its local names
            string[][] languages = File.ReadAllLines(LANGUAGES_FILE_PATH).Select(pair => pair.Split(delimiter)).ToArray();
            context.GalleryLanguages.AddRange(languages.Select(
                pair => {
                    return new GalleryLanguage() {
                        SearchParamValue = pair[0],
                        LocalName = pair[1]
                    };
                }
            ));
            // add gallery types
            context.GalleryTypes.AddRange(
                Enumerable.Range(0, Enum.GetNames(typeof(GalleryType)).Length)
                .Select(i => new GalleryTypeEntity() { GalleryType = (GalleryType)i })
            );

            // add sort directions
            context.SortDirections.AddRange(
                Enumerable.Range(0, Enum.GetNames(typeof(SortDirection)).Length)
                .Select(i => new SortDirectionEntity() { SortDirection = (SortDirection)i })
            );

            // change to indeterminate because SaveChanges() takes a long time
            ChangeToIndeterminateEvent?.Invoke();
            // add gallery sorts
            context.SaveChanges();
            context.GallerySorts.AddRange(
                Enumerable.Range(0, Enum.GetNames(typeof(GallerySortProperty)).Length)
                .Select(i => new GallerySortEntity() {
                    GallerySortProperty = (GallerySortProperty)i,
                    SortDirectionEntity = context.SortDirections.First()
                })
            );
            // default DownloadTime sort
            context.GallerySorts.Find(GallerySortProperty.DownloadTime).IsActive = true;

            context.SaveChanges();
            ClearInvocationList();
        }

        private static void ClearInvocationList() {
            var invocList = DatabaseInitProgressChanged?.GetInvocationList();
            if (invocList != null) {
                foreach (Delegate d in invocList) {
                    DatabaseInitProgressChanged -= (Action<int>)d;
                }
            }
            invocList = ChangeToIndeterminateEvent?.GetInvocationList();
            if (invocList != null) {
                foreach (Delegate d in invocList) {
                    ChangeToIndeterminateEvent -= (Action)d;
                }
            }
        }

        public void AddExampleTagFilters() {
            ResourceMap resourceMap = MainResourceMap.GetSubtree("ExampleTagFilterNames");
            TagFilters.AddRange(
                new() {
                    Name = resourceMap.GetValue("ExampleTagFilterName_1").ValueAsString,
                    Tags = [
                        GetTag("full color", TagCategory.Tag),
                        GetTag("very long hair", TagCategory.Female),
                    ]
                },
                new() {
                    Name = resourceMap.GetValue("ExampleTagFilterName_2").ValueAsString,
                    Tags = [
                        GetTag("glasses", TagCategory.Female),
                        GetTag("sole male", TagCategory.Male),
                    ]
                },
                new() {
                    Name = resourceMap.GetValue("ExampleTagFilterName_3").ValueAsString,
                    Tags = [
                        GetTag("naruto", TagCategory.Series),
                        GetTag("big breasts", TagCategory.Female),
                    ]
                },
                new() {
                    Name = resourceMap.GetValue("ExampleTagFilterName_4").ValueAsString,
                    Tags = [
                        GetTag("non-h imageset", TagCategory.Tag)
                    ]
                }
            );
            SaveChanges();
        }

        /// <returns><see cref="Tag"/> or <c>null</c></returns>
        public Tag GetTag(string value, TagCategory category) {
            string formattedValue = value.ToLower();
            return Tags
                .FirstOrDefault(tag =>
                    tag.Value == formattedValue &&
                    tag.Category == category
                );
        }
    }
}