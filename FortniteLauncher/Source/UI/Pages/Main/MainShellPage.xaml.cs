using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System.Threading.Tasks;
using Windows.Foundation;

namespace FortniteLauncher.Pages
{
    public sealed partial class MainShellPage : Page
    {
        public static NavigationView STATIC_MainNavigation;
        private bool _shopServiceAvailable;

        public MainShellPage()
        {
            this.InitializeComponent();
            NavigationService.InitializeNavigationService(MainNavigation, MainBreadcrumb, RootFrame);
            MainNavigation.SelectedItem = PlayPageItem;
            MainNavigation.LayoutUpdated += MainNavigation_LayoutUpdated;
        }

        private void MainNavigation_SelectionChanged(NavigationView Sender, NavigationViewSelectionChangedEventArgs Args)
        {
            if ((Args.SelectedItem as NavigationViewItem) == PlayPageItem) { NavigationService.Navigate(typeof(PlayPage), true); NavigationService.ChangeBreadcrumbVisibility(false); }
            if ((Args.SelectedItem as NavigationViewItem) == DownloadsItem) { NavigationService.Navigate(typeof(DownloadsPage), true); }
            if ((Args.SelectedItem as NavigationViewItem) == ItemShopItem) { NavigationService.Navigate(typeof(ItemShopPage), true); }
            if ((Args.SelectedItem as NavigationViewItem) == LeaderboardItem) { NavigationService.Navigate(typeof(LeaderboardPage), true); }
            if ((Args.SelectedItem as NavigationViewItem) == ServerStatusItem) { NavigationService.Navigate(typeof(ServerStatusPage), true); }
            if ((Args.SelectedItem as NavigationViewItem) == SettingsItem) { NavigationService.Navigate(typeof(SettingsPage), true); }
            ElementSoundPlayer.Play(ElementSoundKind.Invoke);
        }

        private void MainBreadcrumb_ItemClicked(BreadcrumbBar Sender, BreadcrumbBarItemClickedEventArgs Args)
        {
            if (Args.Index < NavigationService.BreadCrumbs.Count - 1)
            {
                var Crumb = (NavigationService.Breadcrumb)Args.Item;
                Crumb.NavigateToFromBreadcrumb(Args.Index);
            }
        }

        private async void MainNavigation_Loaded(object Sender, RoutedEventArgs Event)
        {
            STATIC_MainNavigation = MainNavigation;
            await CheckShopServiceAvailabilityAsync();
        }

        private async Task CheckShopServiceAvailabilityAsync()
        {
            var isAvailable = await new HistoricalShopService().IsShopServiceAvailableAsync();
            _shopServiceAvailable = isAvailable;
            ItemShopItem.IsEnabled = isAvailable;
            ShopServiceUnavailableOverlay.Visibility = isAvailable ? Visibility.Collapsed : Visibility.Visible;
            ToolTipService.SetToolTip(
                ItemShopItem,
                isAvailable ? null : "Shop Services are currently unavailable");
            UpdateShopServiceUnavailableOverlay();
        }

        private void MainNavigation_LayoutUpdated(object sender, object e)
        {
            if (!_shopServiceAvailable)
            {
                UpdateShopServiceUnavailableOverlay();
            }
        }

        private void UpdateShopServiceUnavailableOverlay()
        {
            if (ItemShopItem.ActualWidth <= 0 || ItemShopItem.ActualHeight <= 0) return;

            var bounds = ItemShopItem.TransformToVisual(PageRoot).TransformBounds(
                new Rect(0, 0, ItemShopItem.ActualWidth, ItemShopItem.ActualHeight));
            Canvas.SetLeft(ShopServiceUnavailableOverlay, bounds.X);
            Canvas.SetTop(ShopServiceUnavailableOverlay, bounds.Y);
            ShopServiceUnavailableOverlay.Width = bounds.Width;
            ShopServiceUnavailableOverlay.Height = bounds.Height;
        }

        private void ShopServiceUnavailableOverlay_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            e.Handled = true;
        }
    }
}
