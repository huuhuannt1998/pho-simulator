using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Pho.Core;
using Pho.UI.Presenters;

namespace Pho.PlayTests
{
    /// <summary>
    /// architecture.md §11's Tier-3 <c>InteractionPlayTests</c> suite:
    /// "spherecast acquires target, prompt text correct".
    ///
    /// WHY THIS EXISTS SEPARATELY FROM VerticalSliceGoldenPathTest: the
    /// golden-path test drives stations through a hand-rolled
    /// <c>FakeInteractorAgent</c> and synthesised <c>InteractionContext</c>s,
    /// deliberately bypassing physics so it stays fast and non-flaky. That
    /// is the right call for THAT test, but it leaves the entire real input
    /// path -- PlayerInteractor's SphereCast, its layer mask, its
    /// hysteresis, and the InteractionTargetChanged event it publishes --
    /// completely unexercised.
    ///
    /// That blind spot let a genuinely game-breaking bug ship unnoticed:
    /// SceneBuilder left <c>interactableMask</c> at LayerMask's serialized
    /// default of 0, and <c>Physics.SphereCast</c> with a zero mask matches
    /// nothing, so the player could not interact with a single object in
    /// the game -- with no error, no warning, and every other test green.
    /// This suite covers that path so it cannot regress silently again.
    /// </summary>
    public class InteractionPlayTests
    {
        // The RestaurantSign is the cleanest raycast target in the generated
        // scene: it's a tall thin post (spanning roughly y=0..1.8) and so
        // sits squarely in front of the eye at ~y=1.7. A counter-height
        // object like the PassCounter (top at y=1.0) would pass UNDER a
        // level forward ray and produce a confusing false failure.
        //
        // Its position is read from the scene at runtime rather than
        // hardcoded here, so this test doesn't silently rot if SceneBuilder's
        // layout constants move.
        const float StandOffDistance = 1.2f; // well inside PlayerInteractor's 2.5 range

        [UnityTest]
        public IEnumerator PlayerInteractor_SphereCast_AcquiresTarget_AndPublishesCorrectPromptText()
        {
            SceneManager.LoadScene("Boot", LoadSceneMode.Single);
            yield return null;
            yield return null;

            var ctx = GameBootstrap.Current;
            Assert.That(ctx, Is.Not.Null, "GameBootstrap did not run.");

            var player = GameObject.Find("Player");
            Assert.That(player, Is.Not.Null, "Player not found in Boot.unity.");

            // Disable the motor so gravity/input can't drift the player out
            // of position between the teleport and the spherecast.
            var motor = player.GetComponent(FindType("Pho.Player.FirstPersonMotor")) as MonoBehaviour;
            if (motor != null) motor.enabled = false;

            var controller = player.GetComponent<CharacterController>();
            if (controller != null) controller.enabled = false;

            var sign = GameObject.Find("RestaurantSign");
            Assert.That(sign, Is.Not.Null, "RestaurantSign not found in Boot.unity -- was the scene rebuilt after it was added?");

            // Stand just short of the sign on its -Z side, facing +Z straight
            // at it. Player root at y=1 puts the eye at ~y=1.7, which is
            // within the sign's vertical span.
            player.transform.position = new Vector3(sign.transform.position.x, 1f, sign.transform.position.z - StandOffDistance);
            player.transform.rotation = Quaternion.identity;

            // PlayerInteractor's spherecast runs in Update, and the prompt is
            // published only on a CHANGE of target, so give it frames to run.
            yield return null;
            yield return null;

            Assert.That(ctx.TryGet<InteractionPromptPresenter>(out var promptPresenter), Is.True,
                "No InteractionPromptPresenter registered -- HudInstaller did not run.");

            Assert.That(promptPresenter.ShowPrompt, Is.True,
                "PlayerInteractor never acquired the RestaurantSign. The most likely cause is interactableMask being empty (a zero LayerMask matches nothing in Physics.SphereCast).");

            // The restaurant starts in Prep, so the sign's own prompt text
            // (see RestaurantSign.GetInteractionText) must be the open one.
            Assert.That(promptPresenter.PromptText, Is.EqualTo("Press E to open the restaurant"),
                "Acquired a target, but the prompt text did not come from the RestaurantSign -- the spherecast may be hitting something else.");
        }

        [UnityTest]
        public IEnumerator PlayerInteractor_LookingAtNothing_PublishesNoPrompt()
        {
            SceneManager.LoadScene("Boot", LoadSceneMode.Single);
            yield return null;
            yield return null;

            var ctx = GameBootstrap.Current;
            Assert.That(ctx, Is.Not.Null);

            var player = GameObject.Find("Player");
            Assert.That(player, Is.Not.Null);

            var motor = player.GetComponent(FindType("Pho.Player.FirstPersonMotor")) as MonoBehaviour;
            if (motor != null) motor.enabled = false;
            var controller = player.GetComponent<CharacterController>();
            if (controller != null) controller.enabled = false;

            // Stand in open floor well away from the kitchen (x=+5) and
            // dining (x=-5) clusters, facing straight up at empty sky.
            player.transform.position = new Vector3(0f, 1f, -8f);
            player.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);

            // Enough frames to exceed missFramesToClearHighlight (3) -- the
            // hysteresis deliberately holds a target briefly to stop the
            // prompt flickering at collider edges.
            for (int i = 0; i < 6; i++) yield return null;

            Assert.That(ctx.TryGet<InteractionPromptPresenter>(out var promptPresenter), Is.True);
            Assert.That(promptPresenter.ShowPrompt, Is.False,
                "Prompt should be hidden when the player is looking at nothing interactable.");
        }

        static System.Type FindType(string fullName)
        {
            foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(fullName);
                if (type != null) return type;
            }
            Assert.Fail($"Type '{fullName}' not found in any loaded assembly.");
            return null;
        }
    }
}
