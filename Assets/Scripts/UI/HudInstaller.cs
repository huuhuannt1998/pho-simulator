using Pho.Core;
using Pho.UI.Presenters;
using UnityEngine;

namespace Pho.UI
{
    /// <summary>
    /// Constructs the four HUD presenters, binds them to the event bus,
    /// registers them into <see cref="GameContext"/>, and spawns the
    /// <see cref="HudView"/> that draws them.
    ///
    /// GAP THIS CLOSES: before this installer, NOTHING in the project ever
    /// constructed a presenter. All four were fully-written, fully-correct,
    /// fully-unreferenced classes -- the game ran with no on-screen UI at
    /// all. (Same class of gap as KitchenBindInstaller/CustomerBindInstaller:
    /// a well-built module with no one calling its Bind seam. Found by
    /// grepping for references to each presenter and finding only comments.)
    ///
    /// SPLIT DESIGN: this [AutoInstall] type is a plain C# class, NOT a
    /// MonoBehaviour, because GameBootstrap builds every discovered
    /// installer via Activator.CreateInstance -- which cannot construct a
    /// MonoBehaviour. It therefore creates a GameObject and AddComponents
    /// HudView onto it. Identical pattern (and identical reasoning) to
    /// RestaurantStateService/RestaurantStateServiceBehaviour -- read that
    /// file's SPLIT DESIGN note.
    ///
    /// No reflection needed here (unlike KitchenBindInstaller): Pho.UI
    /// already references Pho.Core, and this installer lives in Pho.UI, so
    /// it can name every type it touches directly. The dependency direction
    /// is legal.
    ///
    /// Runs at <see cref="InstallOrder.UI"/> (700), the highest existing
    /// slot -- the HUD observes every other system and is depended on by
    /// none, so it installs last.
    /// </summary>
    [AutoInstall]
    public sealed class HudInstaller : IInstaller
    {
        public int Order => InstallOrder.UI;

        public void Install(GameContext ctx)
        {
            var cash = new CashPresenter();
            var prompt = new InteractionPromptPresenter();
            var orders = new OrderBoardPresenter();
            var daySummary = new DaySummaryPresenter();

            cash.Bind(ctx.Events);
            prompt.Bind(ctx.Events);
            orders.Bind(ctx.Events);
            daySummary.Bind(ctx.Events);

            // Registered so a future real uGUI/UI Toolkit view (or a test)
            // can pull the same presenter instances rather than building a
            // second set subscribed to the same events.
            ctx.Register<CashPresenter>(cash);
            ctx.Register<InteractionPromptPresenter>(prompt);
            ctx.Register<OrderBoardPresenter>(orders);
            ctx.Register<DaySummaryPresenter>(daySummary);

            var host = new GameObject(nameof(HudView));
            var view = host.AddComponent<HudView>();
            view.Bind(cash, prompt, orders, daySummary);

            ctx.Register<HudView>(view);
        }
    }
}
