using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace FortniteLauncher.Pages
{
    public sealed partial class ItemShopPage : Page
    {
        private readonly HistoricalShopService _shopService = new();
        private readonly ObservableCollection<HistoricalShopSectionViewModel> _sections = new();
        private readonly Storyboard _refreshIconStoryboard = new();
        private readonly DispatcherTimer _loadingMessageTimer = new() { Interval = TimeSpan.FromMilliseconds(3500) };
        private readonly DispatcherTimer _shopRefreshTimer = new() { Interval = TimeSpan.FromSeconds(1) };
        private readonly TranslateTransform _loadingStatusTranslation = new();
        private static readonly TimeZoneInfo UkTimeZone = GetUkTimeZone();
        private static readonly TimeSpan ShopRefreshRetryInterval = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan ShopRefreshRetryWindow = TimeSpan.FromMinutes(5);
        private static readonly string[] LoadingMessages =
        {
            "Searching all realities...",
            "Initiating Zero Fusion...",
            "Checking the Battle Bus manifest...",
            "Calling in the Item Shop squad...",
            "Dusting off the V-Bucks display...",
            "Sorting today's locker favourites...",
            "Scanning the island for fresh drops...",
            "Stocking the shelves...",
            "We're nearly there...",
        };
        private DateTimeOffset _selectedDate;
        private bool _initialLoadStarted;
        private bool _isLoadingMessageTransitioning;
        private bool _isAutomaticRefreshInProgress;
        private int _loadingMessageIndex;
        private DateTimeOffset _nextShopRefreshUtc;
        private DateTimeOffset _shopRefreshRetryDeadlineUtc;
        private DateTimeOffset _lastSuccessfulShopLoadUtc;
        private TextBlock LoadingStatusText => (TextBlock)LoadingState.Children[1];

        // TEMPORARY: renders five random examples of every archive rarity alongside the live Eon shop.
        private static readonly bool ShowRarityPreview = false;
        private static readonly IReadOnlyDictionary<string, string> RarityPreviewSampleDates = new Dictionary<string, string>
        {
            ["common"] = "2025-03-15",
            ["uncommon"] = "2026-02-06",
            ["rare"] = "2025-12-29",
            ["epic"] = "2026-01-01",
            ["legendary"] = "2023-02-11",
            ["icon_series"] = "2023-04-10",
            ["marvel"] = "2023-03-05",
            ["dc"] = "2022-07-19",
            ["gaming_legends"] = "2025-11-11",
            ["star_wars"] = "2023-09-02",
            ["slurp"] = "2023-07-19",
            ["dark"] = "2023-04-30",
            ["shadow"] = "2020-09-18",
            ["lava"] = "2026-06-12",
            ["lamborghini"] = "2026-05-08",
            ["mclaren"] = "2024-02-13",
            ["frozen"] = "2026-07-17"
        };

        public ItemShopPage()
        {
            InitializeComponent();
            NavigationCacheMode = NavigationCacheMode.Required;
            ConfigureRefreshAnimation();
            LoadingStatusText.RenderTransform = _loadingStatusTranslation;
            _loadingMessageTimer.Tick += LoadingMessageTimer_Tick;
            _shopRefreshTimer.Tick += ShopRefreshTimer_Tick;
            ShopSectionsRepeater.ItemsSource = _sections;
            ShopAppearanceSettings.Changed += ShopAppearanceSettings_Changed;
            Loaded += ItemShopPage_Loaded;
            Unloaded += ItemShopPage_Unloaded;
        }

        private void ShopAppearanceSettings_Changed(object sender, EventArgs e)
        {
            if (_sections.Count == 0) return;

            for (var index = 0; index < _sections.Count; index++)
            {
                var section = _sections[index];
                var updatedItems = section.Items.Select(item => item with
                {
                    CardBackgroundImageUrl = RarityBackgroundImageUrl(item.Rarity),
                    RarityBadgeVisibility = RarityBadgeVisibility(item.Rarity),
                }).ToList();
                _sections[index] = new HistoricalShopSectionViewModel(section.Title, updatedItems);
            }
        }

        private async void ItemShopPage_Loaded(object sender, RoutedEventArgs e)
        {
            StartShopRefreshCountdown();
            if (_initialLoadStarted) return;

            _initialLoadStarted = true;
            await LoadShopAsync();
        }

        private void ItemShopPage_Unloaded(object sender, RoutedEventArgs e) => _shopRefreshTimer.Stop();

        private static TimeZoneInfo GetUkTimeZone()
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time"); }
            catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Europe/London"); }
        }

        private void StartShopRefreshCountdown()
        {
            var now = DateTimeOffset.UtcNow;
            if (_shopRefreshRetryDeadlineUtc > now)
            {
                _nextShopRefreshUtc = now;
            }
            else if (_initialLoadStarted && _lastSuccessfulShopLoadUtc < GetMostRecentShopRefreshUtc(now))
            {
                _shopRefreshRetryDeadlineUtc = now.Add(ShopRefreshRetryWindow);
                _nextShopRefreshUtc = now;
            }
            else
            {
                _shopRefreshRetryDeadlineUtc = default;
                _nextShopRefreshUtc = GetNextShopRefreshUtc(now);
            }
            UpdateShopRefreshCountdown();
            _shopRefreshTimer.Start();
        }

        private static DateTimeOffset GetNextShopRefreshUtc(DateTimeOffset now)
        {
            var londonNow = TimeZoneInfo.ConvertTime(now, UkTimeZone);
            var localRefresh = new DateTime(londonNow.Year, londonNow.Month, londonNow.Day, 18, 0, 0, DateTimeKind.Unspecified);
            if (londonNow.TimeOfDay >= TimeSpan.FromHours(18)) localRefresh = localRefresh.AddDays(1);

            return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localRefresh, UkTimeZone));
        }

        private static DateTimeOffset GetMostRecentShopRefreshUtc(DateTimeOffset now)
        {
            var londonNow = TimeZoneInfo.ConvertTime(now, UkTimeZone);
            var localRefresh = new DateTime(londonNow.Year, londonNow.Month, londonNow.Day, 18, 0, 0, DateTimeKind.Unspecified);
            if (londonNow.TimeOfDay < TimeSpan.FromHours(18)) localRefresh = localRefresh.AddDays(-1);

            return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localRefresh, UkTimeZone));
        }

        private void UpdateShopRefreshCountdown()
        {
            if (_nextShopRefreshUtc == default) return;

            var remaining = _nextShopRefreshUtc - DateTimeOffset.UtcNow;
            if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
            ShopRefreshCountdownLabel.Text = $"Refreshes in {remaining.Hours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}";
            ShopRefreshCountdownLabel.Visibility = Visibility.Visible;
        }

        private async void ShopRefreshTimer_Tick(object sender, object e)
        {
            if (_isAutomaticRefreshInProgress) return;

            if (DateTimeOffset.UtcNow < _nextShopRefreshUtc)
            {
                UpdateShopRefreshCountdown();
                return;
            }

            _isAutomaticRefreshInProgress = true;
            var previousShopDate = _selectedDate;
            SetRefreshVisual(isRefreshing: true);
            await LoadShopAsync(forceRefresh: true);
            SetRefreshVisual(isRefreshing: false);
            _isAutomaticRefreshInProgress = false;

            if (_selectedDate != previousShopDate)
            {
                StartShopRefreshCountdown();
                return;
            }

            ScheduleShopRefreshRetry();
        }

        private void ScheduleShopRefreshRetry()
        {
            var now = DateTimeOffset.UtcNow;
            if (_shopRefreshRetryDeadlineUtc == default)
                _shopRefreshRetryDeadlineUtc = now.Add(ShopRefreshRetryWindow);

            if (now >= _shopRefreshRetryDeadlineUtc)
            {
                StartShopRefreshCountdown();
                return;
            }

            _nextShopRefreshUtc = now.Add(ShopRefreshRetryInterval);
            UpdateShopRefreshCountdown();
        }

        private async void LoadingMessageTimer_Tick(object sender, object e)
        {
            if (_isLoadingMessageTransitioning) return;

            _isLoadingMessageTransitioning = true;
            await AnimateLoadingStatusAsync(0, -12, 170);
            _loadingMessageIndex = (_loadingMessageIndex + 1) % LoadingMessages.Length;
            LoadingStatusText.Text = LoadingMessages[_loadingMessageIndex];
            LoadingStatusText.Opacity = 0;
            _loadingStatusTranslation.X = 12;
            await AnimateLoadingStatusAsync(1, 0, 250);
            _isLoadingMessageTransitioning = false;
        }

        private Task AnimateLoadingStatusAsync(double opacity, double offsetX, int durationMilliseconds)
        {
            var completion = new TaskCompletionSource<bool>();
            var storyboard = new Storyboard();
            var opacityAnimation = new DoubleAnimation
            {
                To = opacity,
                Duration = new Duration(TimeSpan.FromMilliseconds(durationMilliseconds)),
                EnableDependentAnimation = true,
            };
            var translationAnimation = new DoubleAnimation
            {
                To = offsetX,
                Duration = new Duration(TimeSpan.FromMilliseconds(durationMilliseconds)),
                EnableDependentAnimation = true,
            };

            Storyboard.SetTarget(opacityAnimation, LoadingStatusText);
            Storyboard.SetTargetProperty(opacityAnimation, "Opacity");
            Storyboard.SetTarget(translationAnimation, _loadingStatusTranslation);
            Storyboard.SetTargetProperty(translationAnimation, "X");
            storyboard.Children.Add(opacityAnimation);
            storyboard.Children.Add(translationAnimation);
            storyboard.Completed += (_, _) => completion.TrySetResult(true);
            storyboard.Begin();
            return completion.Task;
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            SetRefreshVisual(isRefreshing: true);
            await LoadShopAsync(forceRefresh: true);
            SetRefreshVisual(isRefreshing: false);
        }

        private void ConfigureRefreshAnimation()
        {
            var animation = new DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = new Duration(TimeSpan.FromMilliseconds(750)),
                RepeatBehavior = RepeatBehavior.Forever,
            };
            Storyboard.SetTarget(animation, RefreshIconRotation);
            Storyboard.SetTargetProperty(animation, "Angle");
            _refreshIconStoryboard.Children.Add(animation);
        }

        private void SetRefreshVisual(bool isRefreshing)
        {
            RefreshButtonLabel.Text = isRefreshing ? "Refreshing" : "Refresh";
            if (isRefreshing)
            {
                _refreshIconStoryboard.Begin();
                return;
            }

            _refreshIconStoryboard.Stop();
            RefreshIconRotation.Angle = 0;
        }

        private async Task LoadShopAsync(bool forceRefresh = false)
        {
            var keepCurrentShopVisible = forceRefresh && _sections.Count > 0;
            if (!keepCurrentShopVisible) SetState(isLoading: true);
            else RefreshButton.IsEnabled = false;
            ShopDateLabel.Text = "Fetching Eon Shop....";

            try
            {
                var result = await _shopService.GetCurrentShopAsync(forceRefresh);
                if (result.Date is null)
                {
                    SetState(error: result.ErrorMessage ?? "The current Eon shop date could not be resolved.");
                    return;
                }

                _selectedDate = result.Date.Value;
                _lastSuccessfulShopLoadUtc = DateTimeOffset.UtcNow;
                UpdateHeader();
                UpdateShopRefreshCountdown();
                _sections.Clear();

                if (result.StatusCode == 404)
                {
                    SetState(isEmpty: true);
                    return;
                }

                if (!result.IsSuccess || result.Shop?.Data is null)
                {
                    SetState(error: result.ErrorMessage ?? "The item shop service could not be reached. Please try again.");
                    return;
                }

                foreach (var section in CreateSections(result.Shop.Data)) _sections.Add(section);
                if (ShowRarityPreview)
                {
                    foreach (var section in await CreateRarityPreviewSectionsAsync()) _sections.Add(section);
                }
                SetState(isEmpty: _sections.Count == 0);
            }
            catch (Exception)
            {
                SetState(error: "The item shop service could not be reached. Please try again.");
            }
        }

        private IEnumerable<HistoricalShopSectionViewModel> CreateSections(HistoricalShopData shop)
        {
            var allItems = shop.Featured.Concat(shop.Daily)
                .Where(item => !string.IsNullOrWhiteSpace(item.Id)).ToList();
            var itemsById = allItems.GroupBy(item => item.Id)
                .ToDictionary(group => group.Key, group => group.First());
            var assignedItemIds = new HashSet<string>();

            foreach (var section in shop.Sections ?? new List<HistoricalShopSection>())
            {
                if (string.IsNullOrWhiteSpace(section.DisplayName) || section.Items is null) continue;
                var items = section.Items.Where(itemsById.ContainsKey)
                    .Select(itemId => ToViewModel(itemsById[itemId])).ToList();
                if (items.Count == 0) continue;

                foreach (var itemId in section.Items) assignedItemIds.Add(itemId);
                yield return new HistoricalShopSectionViewModel(section.DisplayName, items);
            }

            if (assignedItemIds.Count > 0)
            {
                var remaining = allItems.Where(item => !assignedItemIds.Contains(item.Id))
                    .Select(ToViewModel).ToList();
                if (remaining.Count > 0) yield return new HistoricalShopSectionViewModel("More from the shop", remaining);
                yield break;
            }

            if (shop.Featured.Count > 0) yield return new HistoricalShopSectionViewModel("Featured", shop.Featured.Select(ToViewModel).ToList());
            if (shop.Daily.Count > 0) yield return new HistoricalShopSectionViewModel("Daily", shop.Daily.Select(ToViewModel).ToList());
        }

        private async Task<IReadOnlyList<HistoricalShopSectionViewModel>> CreateRarityPreviewSectionsAsync()
        {
            var previews = await Task.WhenAll(RarityPreviewSampleDates.Select(async sample => new
            {
                Rarity = NormalizeRarity(sample.Key),
                Label = sample.Key,
                Result = await _shopService.GetShopAsync(DateTimeOffset.ParseExact(
                    sample.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal))
            }));

            var sections = new List<HistoricalShopSectionViewModel>();
            foreach (var preview in previews)
            {
                if (!preview.Result.IsSuccess || preview.Result.Shop?.Data is not { } shop) continue;

                var availableItems = shop.Featured.Concat(shop.Daily)
                    .Where(item => NormalizeRarity(item.Rarity) == preview.Rarity)
                    .ToList();
                if (availableItems.Count == 0) continue;

                var items = availableItems.OrderBy(_ => Random.Shared.Next()).Take(5).ToList();
                while (items.Count < 5) items.Add(availableItems[Random.Shared.Next(availableItems.Count)]);

                sections.Add(new HistoricalShopSectionViewModel(
                    DisplayRarity(preview.Label),
                    items.Select(ToViewModel).ToList()));
            }

            return sections;
        }

        private static HistoricalShopItemViewModel ToViewModel(HistoricalShopItem item) => new(
            item.Name ?? "Unknown item",
            item.ReadableType ?? item.Type ?? "Item",
            string.IsNullOrWhiteSpace(item.Price) ? "—" : item.Price,
            item.Images?.Icon,
            item.PriceIconLink,
            RarityBrush(item.Rarity),
            RarityBadgeBackgroundBrush(item.Rarity),
            RarityBackgroundImageUrl(item.Rarity),
            item.Description ?? "No description is available for this item.",
            DisplayRarity(item.Rarity),
            (item.Rarity ?? "Unknown").ToUpperInvariant(),
            RarityBadgeImageUrl(item.Rarity),
            DetailRarityBadgeWidth(item.Rarity),
            DetailRarityBadgeHeight(item.Rarity),
            RarityBadgeCanvasWidth(item.Rarity),
            RarityBadgeCanvasHeight(item.Rarity),
            RarityBadgeOffsetY(item.Rarity),
            RarityBadgeImageVisibility(item.Rarity),
            RarityBadgeFallbackVisibility(item.Rarity),
            GetImageChoices(item),
            item.Id ?? "—",
            item.Slug ?? "—",
            item.BundleSet ? "Yes" : "No",
            item.BannerText ? "Yes" : "No",
            item.History ? "Yes" : "No",
            item.LegoAssoc ? "Yes" : "No",
            item.Offer ? "Yes" : "No",
            DisplayValue(item.ItemSetText ?? item.ItemSet),
            DisplayValue(item.IntroducedIn),
            FormatDate(item.ReleaseDate),
            FormatDate(item.LastSeen),
            item.Occurrences?.ToString(CultureInfo.InvariantCulture) ?? "Not available",
            DisplayValue(item.CosmeticId),
            item.BundleSet ? "Yes" : "No",
            HasLegoLargeImage(item) ? "Yes" : "No",
            RarityBadgeVisibility(item.Rarity));

        private static string DisplayValue(string value) => string.IsNullOrWhiteSpace(value) ? "Not available" : value;

        private static string DisplayRarity(string rarity)
        {
            if (string.IsNullOrWhiteSpace(rarity)) return "Unknown";
            return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(rarity.Replace("_", " ").ToLowerInvariant());
        }

        private static string FormatDate(string value) => DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToString("dd MMMM yyyy", CultureInfo.CurrentCulture)
            : "Not available";

        private static bool HasLegoLargeImage(HistoricalShopItem item) => Uri.TryCreate(item.LegoImages?.Large, UriKind.Absolute, out _);

        private static IReadOnlyList<ShopImageViewModel> GetImageChoices(HistoricalShopItem item)
        {
            var images = new List<ShopImageViewModel>();
            AddImage(images, item.Images?.Icon, "Item");
            AddImage(images, AsUrl(item.Images?.Png), "Render");
            AddImage(images, AsUrl(item.Images?.Featured), "Featured");
            AddImage(images, item.LegoImages?.Large, "LEGO");
            AddImage(images, item.BeanImages?.Large, "Bean");

            foreach (var variant in item.Variants ?? new List<HistoricalShopVariant>())
            foreach (var option in variant.Options ?? new List<HistoricalShopVariantOption>())
            {
                var label = DisplayValue(option.Name);
                AddImage(images, option.Image, label == "Not available" ? "Variant" : label);
            }

            return images.GroupBy(image => image.Url, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First()).ToList();
        }

        private static string AsUrl(object value) => value as string;

        private static void AddImage(ICollection<ShopImageViewModel> images, string url, string label)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out _)) images.Add(new ShopImageViewModel(url, label));
        }

        private static string NormalizeRarity(string rarity) => rarity?.Trim().ToLowerInvariant() switch
        {
            "dc series" or "dc_series" => "dc",
            "icon series" or "icon_series" => "icon",
            "marvel series" or "marvel_series" => "marvel",
            "gaming legends" or "gaming_legends" => "gaminglegends",
            _ => rarity?.Trim().ToLowerInvariant(),
        };

        private static SolidColorBrush RarityBrush(string rarity) => NormalizeRarity(rarity) switch
        {
            "common" => new SolidColorBrush(ColorHelper.FromArgb(255, 132, 132, 132)),
            "uncommon" => new SolidColorBrush(ColorHelper.FromArgb(255, 89, 178, 80)),
            "rare" => new SolidColorBrush(ColorHelper.FromArgb(255, 67, 145, 255)),
            "epic" => new SolidColorBrush(ColorHelper.FromArgb(255, 154, 88, 215)),
            "legendary" => new SolidColorBrush(ColorHelper.FromArgb(255, 234, 154, 44)),
            "mythic" => new SolidColorBrush(ColorHelper.FromArgb(255, 235, 191, 31)),
            "marvel" => new SolidColorBrush(ColorHelper.FromArgb(255, 232, 65, 47)),
            "dc" => new SolidColorBrush(ColorHelper.FromArgb(255, 56, 116, 255)),
            "icon" => new SolidColorBrush(ColorHelper.FromArgb(255, 65, 230, 232)),
            "gaminglegends" => new SolidColorBrush(ColorHelper.FromArgb(255, 121, 116, 255)),
            "star_wars" => new SolidColorBrush(ColorHelper.FromArgb(255, 242, 202, 76)),
            "dark" => new SolidColorBrush(ColorHelper.FromArgb(255, 102, 65, 151)),
            "shadow" => new SolidColorBrush(ColorHelper.FromArgb(255, 112, 73, 170)),
            "lava" => new SolidColorBrush(ColorHelper.FromArgb(255, 219, 82, 38)),
            "slurp" => new SolidColorBrush(ColorHelper.FromArgb(255, 46, 196, 222)),
            _ => new SolidColorBrush(ColorHelper.FromArgb(255, 125, 125, 125)),
        };

        private static SolidColorBrush RarityBadgeBackgroundBrush(string rarity)
        {
            var color = RarityBrush(rarity).Color;
            return new SolidColorBrush(ColorHelper.FromArgb(255, (byte)(color.R * .32), (byte)(color.G * .32), (byte)(color.B * .32)));
        }

        private static string RarityBadgeImageUrl(string rarity) => NormalizeRarity(rarity) switch
        {
            "common" => "https://builds-cdn.fortforge.co.uk/shop-ui/Common.png",
            "uncommon" => "https://builds-cdn.fortforge.co.uk/shop-ui/Uncommon%20Rarity.png",
            "rare" => "https://builds-cdn.fortforge.co.uk/shop-ui/Rare%20Rarity.png",
            "epic" => "https://builds-cdn.fortforge.co.uk/shop-ui/Epic%20Rarity.png",
            "legendary" => "https://builds-cdn.fortforge.co.uk/shop-ui/Legendary%20Rarity.png",
            "dc" => "https://builds-cdn.fortforge.co.uk/shop-ui/DC%20Series%20Rarity.png",
            "icon" => "https://builds-cdn.fortforge.co.uk/shop-ui/Icon%20Series%20Rarity.png",
            "marvel" => "https://builds-cdn.fortforge.co.uk/shop-ui/Marvel%20Rarity.png",
            "gaminglegends" => "https://builds-cdn.fortforge.co.uk/shop-ui/Gaming%20Legends.png",
            "star_wars" => "https://builds-cdn.fortforge.co.uk/shop-ui/Star%20Wars.png",
            "dark" => "https://builds-cdn.fortforge.co.uk/shop-ui/dark%20series%202.png",
            "shadow" => "https://builds-cdn.fortforge.co.uk/shop-ui/Shadow.png",
            "lava" => "https://builds-cdn.fortforge.co.uk/shop-ui/Lava.png",
            "frozen" => "https://builds-cdn.fortforge.co.uk/shop-ui/Frozen.png",
            "slurp" => "https://builds-cdn.fortforge.co.uk/shop-ui/Slurp%20Series.png",
            _ => null,
        };

        private static double DetailRarityBadgeWidth(string rarity) => NormalizeRarity(rarity) switch
        {
            "rare" or "epic" => 78,
            _ => 126,
        };

        private static double DetailRarityBadgeHeight(string rarity) => NormalizeRarity(rarity) switch
        {
            "rare" or "epic" => 26,
            _ => 40,
        };

        private static double RarityBadgeCanvasWidth(string rarity) => NormalizeRarity(rarity) switch
        {
            "uncommon" => 100,
            "rare" or "epic" => 60,
            _ => 96,
        };

        private static double RarityBadgeCanvasHeight(string rarity) => NormalizeRarity(rarity) switch
        {
            "uncommon" => 33,
            "rare" or "epic" => 20,
            _ => 32,
        };
        private static double RarityBadgeOffsetY(string rarity) => NormalizeRarity(rarity) switch
        {
            "common" or "uncommon" => 6,
            // These supplied series PNGs have less transparent top padding than the standard badge art.
            // Lower them to align their visible artwork with Rare and Epic.
            "rare" or "epic" or "legendary" or "dc" or "marvel" or "icon" or "gaminglegends" or "star_wars" or "dark" or "shadow" or "lava" or "frozen" or "slurp" => 6,
            _ => 0,
        };

        private static bool ShouldShowRarityBadge(string rarity) => NormalizeRarity(rarity) is not ("lamborghini" or "mclaren");

        private static Visibility RarityBadgeImageVisibility(string rarity) => ShouldShowRarityBadge(rarity) && RarityBadgeImageUrl(rarity) is not null
            ? Visibility.Visible
            : Visibility.Collapsed;

        private static Visibility RarityBadgeFallbackVisibility(string rarity) => ShouldShowRarityBadge(rarity) && RarityBadgeImageUrl(rarity) is null
            ? Visibility.Visible
            : Visibility.Collapsed;

        private static Visibility RarityBadgeVisibility(string rarity) => GlobalSettings.Options?.ShowRarityBadges == false || !ShouldShowRarityBadge(rarity)
            ? Visibility.Collapsed
            : Visibility.Visible;

        private static bool IsUncommonRarity(string rarity) => string.Equals(NormalizeRarity(rarity), "uncommon", StringComparison.OrdinalIgnoreCase);

        private static string RarityBackgroundImageUrl(string rarity)
        {
            var normalizedRarity = NormalizeRarity(rarity);
            var suppliedBackground = normalizedRarity switch
            {
                "gaminglegends" => "https://builds-cdn.fortforge.co.uk/shop-ui/Gaming%20Legends%20BG.jpeg",
                "star_wars" => "https://builds-cdn.fortforge.co.uk/shop-ui/Star%20Wars%20BG.webp",
                "icon" => "https://builds-cdn.fortforge.co.uk/shop-ui/Icon%20Series%20BG.webp",
                "dc" => "https://builds-cdn.fortforge.co.uk/shop-ui/DC%20BG.webp",
                "marvel" => "https://builds-cdn.fortforge.co.uk/shop-ui/Marvel%20BG.webp",
                "dark" => "https://builds-cdn.fortforge.co.uk/shop-ui/Dark%20Series%20BG.webp?v=2",
                "frozen" => "https://builds-cdn.fortforge.co.uk/shop-ui/Frozen%20BG.webp",
                "shadow" => "https://builds-cdn.fortforge.co.uk/shop-ui/Shadow%20BG.webp",
                "lava" => "https://builds-cdn.fortforge.co.uk/shop-ui/Lava%20BG.webp",
                "slurp" => "https://builds-cdn.fortforge.co.uk/shop-ui/Slurp%20Background.webp",
                "lamborghini" or "mclaren" => "https://builds-cdn.fortforge.co.uk/shop-ui/Rocket%20League%20BG.webp",
                _ => null,
            };
            if (suppliedBackground is not null) return suppliedBackground;

            var asset = normalizedRarity switch
            {
                "common" => "common", "uncommon" => "uncommon", "rare" => "rare", "epic" => "epic", "legendary" => "legendary",
                "icon" => "icon", "marvel" => "marvel", "dc" => "dc", _ => "special",
            };
            return $"ms-appx:///Content/Texture/Shop/Rarity/{asset}.svg";
        }

        private void UpdateHeader() => ShopDateLabel.Text = _selectedDate.ToString("dddd, dd MMMM yyyy", CultureInfo.CurrentCulture);

        private void SetState(bool isLoading = false, bool isEmpty = false, string error = null)
        {
            if (isLoading)
            {
                _loadingMessageIndex = 0;
                LoadingStatusText.Text = LoadingMessages[_loadingMessageIndex];
                _loadingMessageTimer.Start();
            }
            else
            {
                _loadingMessageTimer.Stop();
                _isLoadingMessageTransitioning = false;
                LoadingStatusText.Opacity = 1;
                _loadingStatusTranslation.X = 0;
            }

            LoadingState.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
            EmptyState.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
            ErrorState.Visibility = string.IsNullOrWhiteSpace(error) ? Visibility.Collapsed : Visibility.Visible;
            ShopScrollViewer.Visibility = isLoading || isEmpty || !string.IsNullOrWhiteSpace(error) ? Visibility.Collapsed : Visibility.Visible;
            RefreshButton.IsEnabled = !isLoading;
            if (!string.IsNullOrWhiteSpace(error)) ErrorMessage.Text = error;
        }

        private void ShopCard_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not HistoricalShopItemViewModel item) return;
            NavigationService.MainFrame.Navigate(typeof(ShopItemDetailsPage), item, new DrillInNavigationTransitionInfo());
        }

        private void ShopCard_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is Button button)
                SetCardHoverState(button, new Vector3(0, -2, 0), new Vector3(1.012f, 1.012f, 1), true);
        }

        private void ShopCard_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is Button button)
                SetCardHoverState(button, Vector3.Zero, Vector3.One, false);
        }

        private static void SetCardHoverState(Button button, Vector3 translation, Vector3 scale, bool isHovering)
        {
            button.Translation = translation;
            button.Scale = scale;

            if (button.Content is Border card && card.Child is Grid layout && layout.Children.Count > 1 && layout.Children[1] is Border footer)
            {
                footer.Background = new SolidColorBrush(isHovering
                    ? ColorHelper.FromArgb(255, 54, 59, 81)
                    : Colors.Transparent);
            }
        }

        private void CloseItemDetailsButton_Click(object sender, RoutedEventArgs e)
        {
            ItemDetailsOverlay.Opacity = 0;
            ItemDetailsOverlay.Visibility = Visibility.Collapsed;
        }
    }

    public sealed record HistoricalShopSectionViewModel(string Title, IReadOnlyList<HistoricalShopItemViewModel> Items);
    public sealed record HistoricalShopItemViewModel(string Name, string ReadableType, string Price, string IconUrl, string PriceIconUrl, SolidColorBrush RarityBrush, SolidColorBrush RarityBadgeBackgroundBrush, string CardBackgroundImageUrl, string Description, string Rarity, string RarityLabel, string RarityBadgeImageUrl, double DetailRarityBadgeWidth, double DetailRarityBadgeHeight, double RarityBadgeCanvasWidth, double RarityBadgeCanvasHeight, double RarityBadgeOffsetY, Visibility RarityBadgeImageVisibility, Visibility RarityBadgeFallbackVisibility, IReadOnlyList<ShopImageViewModel> ImageChoices, string Id, string Slug, string BundleSet, string BannerText, string History, string LegoAssociation, string Offer, string Set, string IntroducedIn, string ReleaseDate, string LastSeen, string ShopOccurrences, string Cid, string Bundle, string LegoStyle, Visibility RarityBadgeVisibility);
    public sealed record ShopImageViewModel(string Url, string Label);
}
