using Newtonsoft.Json;
using System.Collections.Generic;

namespace FortniteLauncher
{
    public sealed class HistoricalShopApiResponse
    {
        [JsonProperty("status")] public int Status { get; set; }
        [JsonProperty("data")] public HistoricalShopData Data { get; set; }
    }

    public sealed class HistoricalShopData
    {
        [JsonProperty("featured")] public List<HistoricalShopItem> Featured { get; set; } = new();
        [JsonProperty("daily")] public List<HistoricalShopItem> Daily { get; set; } = new();
        [JsonProperty("sections")] public List<HistoricalShopSection> Sections { get; set; } = new();
    }

    public sealed class HistoricalShopSection
    {
        [JsonProperty("displayName")] public string DisplayName { get; set; }
        [JsonProperty("items")] public List<string> Items { get; set; } = new();
    }

    public sealed class HistoricalShopItem
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("price")] public string Price { get; set; }
        [JsonProperty("priceIconLink")] public string PriceIconLink { get; set; }
        [JsonProperty("rarity")] public string Rarity { get; set; }
        [JsonProperty("type")] public string Type { get; set; }
        [JsonProperty("readableType")] public string ReadableType { get; set; }
        [JsonProperty("description")] public string Description { get; set; }
        [JsonProperty("slug")] public string Slug { get; set; }
        [JsonProperty("bundleSet")] public bool BundleSet { get; set; }
        [JsonProperty("bannerText")] public bool BannerText { get; set; }
        [JsonProperty("history")] public bool History { get; set; }
        [JsonProperty("legoAssoc")] public bool LegoAssoc { get; set; }
        [JsonProperty("offer")] public bool Offer { get; set; }
        [JsonProperty("introducedIn")] public string IntroducedIn { get; set; }
        [JsonProperty("releaseDate")] public string ReleaseDate { get; set; }
        [JsonProperty("lastSeen")] public string LastSeen { get; set; }
        [JsonProperty("occurrences")] public int? Occurrences { get; set; }
        [JsonProperty("cosmeticId")] public string CosmeticId { get; set; }
        [JsonProperty("itemSet")] public string ItemSet { get; set; }
        [JsonProperty("itemSetText")] public string ItemSetText { get; set; }
        [JsonProperty("legoAvailable")] public bool LegoAvailable { get; set; }
        [JsonProperty("legoImages")] public HistoricalShopLegoImages LegoImages { get; set; }
        [JsonProperty("beanImages")] public HistoricalShopBeanImages BeanImages { get; set; }
        [JsonProperty("variants")] public List<HistoricalShopVariant> Variants { get; set; } = new();
        [JsonProperty("images")] public HistoricalShopImages Images { get; set; }
    }

    public sealed class HistoricalShopImages
    {
        [JsonProperty("icon")] public string Icon { get; set; }
        [JsonProperty("png")] public object Png { get; set; }
        [JsonProperty("gallery")] public object Gallery { get; set; }
        [JsonProperty("featured")] public object Featured { get; set; }
    }

    public sealed class HistoricalShopLegoImages
    {
        [JsonProperty("small")] public string Small { get; set; }
        [JsonProperty("large")] public string Large { get; set; }
    }

    public sealed class HistoricalShopBeanImages
    {
        [JsonProperty("small")] public string Small { get; set; }
        [JsonProperty("large")] public string Large { get; set; }
    }

    public sealed class HistoricalShopVariant
    {
        [JsonProperty("channel")] public string Channel { get; set; }
        [JsonProperty("type")] public string Type { get; set; }
        [JsonProperty("options")] public List<HistoricalShopVariantOption> Options { get; set; } = new();
    }

    public sealed class HistoricalShopVariantOption
    {
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("image")] public string Image { get; set; }
    }
}
