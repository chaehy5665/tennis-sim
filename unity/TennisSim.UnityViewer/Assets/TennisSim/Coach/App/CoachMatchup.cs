using TennisSim.Core;

namespace TennisSim.Coach
{
    // The fixed matchup of the vertical slice: Ember (baseline) against Rook (the backhander archetype, docs/
    // PLAYER_TYPES.md: the baseline player with forehand and backhand swapped). Player selection belongs to the
    // season layer, which does not exist yet.
    public static class CoachMatchup
    {
        public static MatchInput Input(uint seed) =>
            new MatchInput { Seed = seed, Players = new[] { PlayerProfile.Preset("baseline", "A"), PlayerProfile.Preset("backhander", "B") } };
    }
}
