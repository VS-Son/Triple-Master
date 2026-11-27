using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Scripts.IAP_shop.ShopCellView.PackCellView
{
    // Shared look for the buy button of one-time packs: gray and not clickable once purchased.
    // Cells are recycled by the scroller, so the normal state is restored explicitly as well.
    public static class PurchaseButtonState
    {
        public static void Apply(Image button, TMP_Text label, Sprite normalSprite, Sprite purchasedSprite, bool isPurchased)
        {
            if (button == null) return;
            button.sprite = isPurchased && purchasedSprite != null ? purchasedSprite : normalSprite;
            button.raycastTarget = !isPurchased;
            if (label != null) label.raycastTarget = false;
        }
    }
}
