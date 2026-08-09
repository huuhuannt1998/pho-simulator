using Pho.Domain.Cooking;
using Pho.Domain.Identity;

namespace Pho.Domain.Customers
{
    /// <summary>
    /// What <see cref="ICustomerWorld.TryTakeServedDish"/> hands back to the
    /// customer. Not specified beyond name/usage sites in architecture.md
    /// section 6.2 -- designed here to carry exactly what
    /// <see cref="CustomerBrain"/> needs to build a
    /// <c>Pho.Domain.Satisfaction.ServiceRecord</c> (Quality, Accuracy01) and
    /// to complete the transaction (QuotedPrice, since
    /// <see cref="ICustomerWorld.PlaceOrder"/> returns only an
    /// <c>OrderId</c>, not a price -- the concrete order's quoted price is
    /// resolved server-side and threaded back through the served dish).
    /// </summary>
    public readonly struct ServedDish
    {
        public readonly RecipeId Recipe;
        public readonly DishQuality Quality;
        public readonly float Accuracy01;
        public readonly decimal QuotedPrice;

        public ServedDish(RecipeId recipe, DishQuality quality, float accuracy01, decimal quotedPrice)
        {
            Recipe = recipe;
            Quality = quality;
            Accuracy01 = accuracy01;
            QuotedPrice = quotedPrice;
        }
    }
}
