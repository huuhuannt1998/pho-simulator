using Pho.Domain.Identity;

namespace Pho.Domain.Events
{
    public readonly struct BowlAssembled : IGameEvent
    {
        public readonly int BowlInstanceId;
        public readonly RecipeId MatchedRecipe;
        public readonly float Accuracy01;
        public readonly float Quality01;

        public BowlAssembled(int bowlInstanceId, RecipeId matchedRecipe, float accuracy01, float quality01)
        {
            BowlInstanceId = bowlInstanceId;
            MatchedRecipe = matchedRecipe;
            Accuracy01 = accuracy01;
            Quality01 = quality01;
        }
    }

    public readonly struct BrothReady : IGameEvent
    {
        public readonly float Quality01;

        public BrothReady(float quality01) => Quality01 = quality01;
    }
}
