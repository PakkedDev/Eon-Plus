using System;

namespace FortniteLauncher
{
    public static class ShopAppearanceSettings
    {
        public static event EventHandler Changed;

        public static void NotifyChanged()
        {
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }
}
