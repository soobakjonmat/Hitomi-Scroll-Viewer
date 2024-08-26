﻿using HitomiScrollViewerLib.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Windows.ApplicationModel.Resources;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static HitomiScrollViewerLib.SharedResources;
using static HitomiScrollViewerLib.Utils;

namespace HitomiScrollViewerLib.DbContexts {
    public class HitomiContext : DbContext {
        public DbSet<TagFilterSet> TagFilterSets { get; set; }
        public DbSet<Gallery> Galleries { get; set; }
        public DbSet<Tag> Tags { get; set; }
        public DbSet<GalleryLanguage> GalleryLanguages { get; set; }

        private static HitomiContext _main;
        public static HitomiContext Main {
            get => _main ??= new HitomiContext();
            set => _main = value;
        }
                
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) {
            // db file storage location = Windows.Storage.ApplicationData.Current.LocalFolder.Path
            optionsBuilder.UseSqlite($"Data Source={MAIN_DATABASE_PATH_V3}");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder) {
            modelBuilder.Entity<Gallery>()
                .HasMany(t => t.Files)
                .WithOne()
                .OnDelete(DeleteBehavior.Cascade);
        }

        private static readonly string[] ALPHABETS_WITH_123 =
            Enumerable.Concat(
                ["123"],
                Enumerable.Range('a', 26).Select(intValue => Convert.ToChar(intValue).ToString())
            ).ToArray();

        private const string TAG_RES_ROOT_DIR = "TagResources";
        private static readonly string DELIMITER_FILE_PATH = Path.Combine(
            Windows.ApplicationModel.Package.Current.InstalledPath,
            TAG_RES_ROOT_DIR,
            "delimiter.txt"
        );
        private static readonly string LANGUAGES_FILE_PATH = Path.Combine(
            Windows.ApplicationModel.Package.Current.InstalledPath,
            TAG_RES_ROOT_DIR,
            "languages.txt"
        );
        private static readonly Dictionary<Category, string> CATEGORY_DIR_DICT = new() {
            { Category.Tag, "Tags" },
            { Category.Male, "Males" },
            { Category.Female, "Females" },
            { Category.Artist, "Artists" },
            { Category.Group, "Groups" },
            { Category.Character, "Characters" },
            { Category.Series, "Series" }
        };

        public static readonly int DATABASE_INIT_OP_NUM = Tag.CATEGORY_NUM * ALPHABETS_WITH_123.Length + 1;
        public event EventHandler<int> DatabaseInitProgressChanged;
        public event EventHandler ChangeToIndeterminateEvent;
        public static void InitDatabase() {
            string delimiter = File.ReadAllText(DELIMITER_FILE_PATH);
            int progressValue = 0;
            // add tags
            for (int i = 0; i < Tag.CATEGORY_NUM; i++) {
                Category category = (Category)i;
                string categoryStr = CATEGORY_DIR_DICT[category];
                string dir = Path.Combine(
                    Windows.ApplicationModel.Package.Current.InstalledPath,
                    TAG_RES_ROOT_DIR,
                    categoryStr
                );
                foreach (string alphanumStr in ALPHABETS_WITH_123) {
                    string path = Path.Combine(dir, $"{categoryStr.ToLower()}-{alphanumStr}.txt");
                    string[] tagInfoStrs = File.ReadAllLines(path);
                    Main.Tags.AddRange(tagInfoStrs.Select(
                        tagInfoStr => {
                            string[] tagInfoArr = tagInfoStr.Split(delimiter);
                            return new Tag() {
                                Category = category,
                                Value = tagInfoArr[0],
                                GalleryCount = int.Parse(tagInfoArr[1])
                            };
                        }
                    ));
                    Main.DatabaseInitProgressChanged?.Invoke(null, ++progressValue);
                }
            }
            // add languages
            string[] languages = File.ReadAllLines(LANGUAGES_FILE_PATH);
            Main.GalleryLanguages.AddRange(languages.Select(
                language => {
                    return new GalleryLanguage() {
                        SearchParamValue = language
                    };
                }
            ));

            /*
             * this line isn't actually meaningful to the user because
             * Main.ChangeToIndeterminateEvent?.Invoke(); is
             * called straight after it
            */
            // Main.DatabaseInitProgressChanged?.Invoke(null, ++progressValue);
            Main.ChangeToIndeterminateEvent?.Invoke(null, null);
            Main.SaveChanges();
            ClearInvocationList();
        }

        private static void ClearInvocationList() {
            var invocList = Main.DatabaseInitProgressChanged?.GetInvocationList();
            if (invocList != null) {
                foreach (Delegate d in invocList) {
                    Main.DatabaseInitProgressChanged -= (EventHandler<int>)d;
                }
            }
            invocList = Main.ChangeToIndeterminateEvent?.GetInvocationList();
            if (invocList != null) {
                foreach (Delegate d in invocList) {
                    Main.ChangeToIndeterminateEvent -= (EventHandler)d;
                }
            }
        }

        public void AddExampleTagFilterSets() {
            ResourceMap resourceMap = MainResourceMap.GetSubtree("ExampleTFSNames");
            TagFilterSets.AddRange(
                new() {
                    Name = resourceMap.GetValue("ExampleTagFilterSet_1").ValueAsString,
                    Tags = [
                        Tag.GetTag("full color", Category.Tag),
                        Tag.GetTag("very long hair", Category.Female),
                    ]
                },
                new() {
                    Name = resourceMap.GetValue("ExampleTagFilterSet_2").ValueAsString,
                    Tags = [
                        Tag.GetTag("glasses", Category.Female),
                        Tag.GetTag("sole male", Category.Male),
                    ]
                },
                new() {
                    Name = resourceMap.GetValue("ExampleTagFilterSet_3").ValueAsString,
                    Tags = [
                        Tag.GetTag("naruto", Category.Series),
                        Tag.GetTag("big breasts", Category.Tag),
                    ]
                },
                new() {
                    Name = resourceMap.GetValue("ExampleTagFilterSet_4").ValueAsString,
                    Tags = [
                        Tag.GetTag("non-h imageset", Category.Tag)
                    ]
                }
            );
            SaveChanges();
        }
    }
}