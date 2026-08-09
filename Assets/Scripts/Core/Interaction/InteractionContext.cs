using UnityEngine;

namespace Pho.Core.Interaction
{
    /// <summary>
    /// Everything an IInteractable needs to evaluate/perform an interaction.
    ///
    /// Deliberate deviation from GDD §46, which types
    /// `Interact(PlayerInteractor player)` directly: coupling every station
    /// to a concrete player class would force a rewrite of every station once
    /// employees (post-vertical-slice) need to drive the same interactables.
    /// Wrapping the actor behind IInteractorAgent decouples stations from
    /// the player entirely -- see architecture.md §5.
    /// </summary>
    public readonly struct InteractionContext
    {
        public readonly IInteractorAgent Agent;
        public readonly RaycastHit Hit;
        public readonly InteractionVerb Verb;

        public InteractionContext(IInteractorAgent agent, RaycastHit hit, InteractionVerb verb)
        {
            Agent = agent;
            Hit = hit;
            Verb = verb;
        }
    }
}
