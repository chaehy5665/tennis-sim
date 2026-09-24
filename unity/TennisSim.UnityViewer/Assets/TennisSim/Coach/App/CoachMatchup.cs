using TennisSim.Core;

namespace TennisSim.Coach
{
    // The fixed matchup of the vertical slice: Ember (baseline) against Rook, the baseline preset with forehand and
    // backhand swapped. Player selection belongs to the season layer, which does not exist yet.
    public static class CoachMatchup
    {
        public static MatchInput Input(uint seed)
        {
            var me = PlayerProfile.Preset("baseline", "A");
            var rook = PlayerProfile.Preset("baseline", "B"); rook.Name = "Rook";
            (rook.ForehandPower, rook.BackhandPower) = (rook.BackhandPower, rook.ForehandPower);
            (rook.ForehandControl, rook.BackhandControl) = (rook.BackhandControl, rook.ForehandControl);
            return new MatchInput { Seed = seed, Players = new[] { me, rook } };
        }
    }
}
