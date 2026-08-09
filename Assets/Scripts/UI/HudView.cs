using System.Text;
using Pho.Domain.Events;
using Pho.UI.Presenters;
using UnityEngine;

namespace Pho.UI
{
    /// <summary>
    /// The on-screen HUD view. Renders whatever the four plain-C#
    /// presenters (<see cref="CashPresenter"/>,
    /// <see cref="InteractionPromptPresenter"/>,
    /// <see cref="OrderBoardPresenter"/>, <see cref="DaySummaryPresenter"/>)
    /// currently hold, and owns their disposal.
    ///
    /// IMGUI (OnGUI), DELIBERATELY -- read before "upgrading" this:
    /// every presenter in Pho.UI/Presenters was built view-less on purpose
    /// ("a future uGUI/UI Toolkit view polls these properties ... rather
    /// than this class touching any Unity UI type" -- CashPresenter's own
    /// doc comment). That seam is the valuable part and it is already
    /// correct. What was missing was ANY view at all: nothing in the
    /// project constructed a presenter, so the game shipped with zero
    /// on-screen UI. This class is the smallest honest thing that closes
    /// that gap -- no Canvas prefab, no TextMeshPro dependency, no font
    /// assets, no uGUI hierarchy to author and keep in sync with
    /// SceneBuilder. It is intentionally plain and replaceable: a real
    /// uGUI/UI Toolkit view can be dropped in later reading the exact same
    /// presenter properties, and this file deleted, with zero changes
    /// anywhere else.
    ///
    /// This is a vertical-slice readability tool ("can I see my cash go up
    /// when a customer pays?"), NOT the GDD §43 HUD design. Art/layout/feel
    /// are explicitly a later pass.
    /// </summary>
    public sealed class HudView : MonoBehaviour
    {
        CashPresenter _cash;
        InteractionPromptPresenter _prompt;
        OrderBoardPresenter _orders;
        DaySummaryPresenter _daySummary;
        bool _bound;

        GUIStyle _labelStyle;
        GUIStyle _promptStyle;

        /// <summary>
        /// Injection seam, mirroring the Kitchen-station / PlayerInteractor
        /// Bind(...) convention used throughout this codebase. Inert (draws
        /// nothing, never throws) until called.
        /// </summary>
        public void Bind(CashPresenter cash, InteractionPromptPresenter prompt, OrderBoardPresenter orders, DaySummaryPresenter daySummary)
        {
            _cash = cash;
            _prompt = prompt;
            _orders = orders;
            _daySummary = daySummary;
            _bound = true;
        }

        void OnDestroy()
        {
            // The presenters are IDisposable event subscribers; leaking them
            // would leave dead handlers on the bus across scene reloads.
            _cash?.Dispose();
            _prompt?.Dispose();
            _orders?.Dispose();
            _daySummary?.Dispose();
        }

        /// <summary>Returns false if a usable GUI skin isn't available -- defensive, for headless/batchmode runs where OnGUI can fire without a real skin.</summary>
        bool EnsureStyles()
        {
            if (_labelStyle != null) return true;
            if (GUI.skin == null) return false;

            _labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 16 };
            _labelStyle.normal.textColor = Color.white;

            _promptStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, alignment = TextAnchor.MiddleCenter };
            _promptStyle.normal.textColor = Color.white;
            return true;
        }

        void OnGUI()
        {
            if (!_bound) return;
            if (!EnsureStyles()) return;

            DrawCash();
            DrawInteractionPrompt();
            DrawOrderBoard();
            DrawDaySummary();
        }

        void DrawCash()
        {
            if (_cash == null) return;

            // HasReceivedUpdate distinguishes "no CashChanged seen yet" from
            // "balance really is $0" -- see CashPresenter's own doc comment.
            var text = _cash.HasReceivedUpdate
                ? $"Cash: ${_cash.Cash:0.00}"
                : "Cash: --";

            GUI.Label(new Rect(12f, 10f, 300f, 24f), text, _labelStyle);

            if (_cash.HasReceivedUpdate && _cash.LastDelta != 0m)
            {
                var sign = _cash.LastDelta > 0m ? "+" : "-";
                GUI.Label(
                    new Rect(12f, 32f, 300f, 24f),
                    $"{sign}${System.Math.Abs(_cash.LastDelta):0.00} ({_cash.LastCategory})",
                    _labelStyle);
            }
        }

        void DrawInteractionPrompt()
        {
            if (_prompt == null || !_prompt.ShowPrompt || string.IsNullOrEmpty(_prompt.PromptText)) return;

            // Centred, slightly below the crosshair line -- the conventional
            // first-person prompt position.
            GUI.Label(new Rect(0f, Screen.height * 0.58f, Screen.width, 28f), _prompt.PromptText, _promptStyle);
        }

        void DrawOrderBoard()
        {
            if (_orders == null) return;

            var tickets = _orders.Orders;
            if (tickets == null || tickets.Count == 0) return;

            var sb = new StringBuilder();
            sb.AppendLine("Orders:");

            int shown = 0;
            for (int i = 0; i < tickets.Count && shown < MaxOrdersShown; i++)
            {
                var ticket = tickets[i];

                // OrderBoardPresenter deliberately retains terminal orders
                // for the whole session (see its class remarks), which is
                // right for a board but wrong for a live HUD -- filter to
                // the ones still in flight.
                if (IsTerminal(ticket.State)) continue;

                sb.AppendLine($"  {ticket.Recipe} -- {ticket.State}");
                shown++;
            }

            if (shown == 0) return;

            GUI.Label(new Rect(Screen.width - 260f, 10f, 250f, 24f * (shown + 1)), sb.ToString(), _labelStyle);
        }

        const int MaxOrdersShown = 8;

        /// <summary>Every OrderState an order can never leave -- see OrderStateMachine's transition table.</summary>
        static bool IsTerminal(OrderState state) =>
            state == OrderState.Completed
            || state == OrderState.Cancelled
            || state == OrderState.Expired
            || state == OrderState.Refunded;

        void DrawDaySummary()
        {
            if (_daySummary == null || !_daySummary.HasReport) return;

            var report = _daySummary.LatestReport;
            if (report == null) return;

            var sb = new StringBuilder();
            sb.AppendLine($"Day {_daySummary.Day} complete");
            sb.AppendLine($"  Revenue:  ${report.Revenue:0.00}");
            sb.AppendLine($"  Cost:     ${report.IngredientCost:0.00}");
            sb.AppendLine($"  Rent:     ${report.Rent:0.00}");
            sb.AppendLine($"  Utilities:${report.Utilities:0.00}");
            sb.AppendLine($"  Profit:   ${report.Profit:0.00}");

            GUI.Label(new Rect(12f, Screen.height - 130f, 320f, 120f), sb.ToString(), _labelStyle);
        }
    }
}
