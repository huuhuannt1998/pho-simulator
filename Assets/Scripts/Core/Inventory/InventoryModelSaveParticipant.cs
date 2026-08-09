using System;
using System.Collections.Generic;
using Pho.Domain.Contracts;
using Pho.Domain.Identity;
using Pho.Domain.Inventory;
using Pho.Save.Dto;
using Pho.Save.Participation;

namespace Pho.Core.Inventory
{
    /// <summary>
    /// ISaveParticipant companion for the shared InventoryModel.
    /// InventoryService itself is a stateless [AutoInstall] factory (see its
    /// own class doc comment) -- the model it constructs is the thing with
    /// state, so the participant wraps that model directly rather than the
    /// installer. Registered by InventoryService.Install right after it
    /// creates the model.
    /// </summary>
    public sealed class InventoryModelSaveParticipant : ISaveParticipant
    {
        readonly InventoryModel _model;

        public InventoryModelSaveParticipant(InventoryModel model)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
        }

        public int RestoreOrder => InstallOrder.Inventory;

        public void Capture(SaveFile save)
        {
            var lots = new List<LotDto>(_model.Lots.Count);
            foreach (var lot in _model.Lots)
            {
                lots.Add(new LotDto
                {
                    ingredientId = lot.Ingredient.Value,
                    quantity = lot.Quantity,
                    qualityAtPurchase = lot.QualityAtPurchase01,
                    freshness = lot.Freshness01,
                    purchasedOnDay = lot.PurchasedOnDay,
                    storage = lot.StoredIn.ToString(),
                });
            }

            save.inventory.lots = lots;
        }

        public void Restore(SaveFile save, IGameDatabase db)
        {
            _model.Clear();

            var lots = save.inventory?.lots;
            if (lots == null) return;

            foreach (var dto in lots)
            {
                var storage = (StorageRequirement)Enum.Parse(typeof(StorageRequirement), dto.storage);
                _model.AddLot(new IngredientId(dto.ingredientId), dto.quantity, dto.qualityAtPurchase, dto.freshness, dto.purchasedOnDay, storage);
            }
        }
    }
}
