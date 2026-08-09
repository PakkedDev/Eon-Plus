using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Input;
using System;

namespace FortniteLauncher.Pages
{
    public sealed partial class ShopItemDetailsPage : Page
    {
        private Button _selectedThumbnail;

        public ShopItemDetailsPage()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            DataContext = e.Parameter as HistoricalShopItemViewModel;
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            NavigateBackToShop();
        }

        private void BackKeyboardAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            args.Handled = true;
            NavigateBackToShop();
        }

        private void NavigateBackToShop()
        {
            if (Frame.CanGoBack)
            {
                Frame.GoBack(new DrillInNavigationTransitionInfo());
            }
        }

        private void ImageThumbnail_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button thumbnail || thumbnail.Tag is not string imageUrl || !Uri.TryCreate(imageUrl, UriKind.Absolute, out var imageUri))
            {
                return;
            }

            HeroImage.Source = new BitmapImage(imageUri);
            SetThumbnailState(_selectedThumbnail, false);
            SetThumbnailState(thumbnail, true);
            _selectedThumbnail = thumbnail;
        }

        private static void SetThumbnailState(Button thumbnail, bool selected)
        {
            if (thumbnail?.Content is not Border frame) return;

            frame.Background = new SolidColorBrush(selected
                ? Microsoft.UI.ColorHelper.FromArgb(255, 48, 54, 77)
                : Microsoft.UI.ColorHelper.FromArgb(255, 37, 41, 59));
            frame.BorderBrush = new SolidColorBrush(selected
                ? Microsoft.UI.ColorHelper.FromArgb(255, 154, 166, 206)
                : Microsoft.UI.ColorHelper.FromArgb(255, 63, 68, 88));
        }
    }
}
