using System.Collections;
using NUnit.Framework;
using TennisSim.Core;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace TennisSim.Coach.Tests
{
    public sealed class CoachPlayModeTests
    {
        [UnityTest]
        public IEnumerator PreMatchChangeoverAndReviewScreensBuild()
        {
            var host = new GameObject("CoachTest");
            var app = host.AddComponent<CoachApp>();
            app.Seed = 3;
            yield return null;
            Assert.That(app.Root, Is.Not.Null);
            Assert.That(app.Session.Phase, Is.EqualTo(CoachPhase.PreMatch));
            Assert.That(app.Root.Query<Button>(className: "tsc-chip").ToList().Count, Is.EqualTo(9), "tactic chips");

            // A passive A makes the opponent coach counter with Aggressive at the first changeover (OpponentCoach rule).
            app.StartMatch(new Tactic { Aggression = Aggression.Safe });
            yield return null;
            yield return null;
            Assert.That(app.Session.Phase, Is.EqualTo(CoachPhase.Playing));
            Assert.That(app.Root.Q<CourtView>(), Is.Not.Null);

            app.SkipToNextStop();
            yield return null;
            Assert.That(app.Session.Phase, Is.EqualTo(CoachPhase.Changeover));
            Assert.That(app.Root.Q(className: "tsc-changeover"), Is.Not.Null);
            Assert.That(app.Root.Q(className: "tsc-inset--lines-2"), Is.Not.Null, "choice description box");
            Assert.That(app.Root.Q(className: "tsc-inset--lines-3"), Is.Not.Null, "change summary box");
            Assert.That(app.Root.Q(className: "tsc-table-group"), Is.Not.Null, "serve course group header");

            Assert.That(app.Root.Q(className: "tsc-banner"), Is.Not.Null, "opponent coach banner at the first changeover");
            int banners = 0;
            while (app.Session.Phase != CoachPhase.Finished)
            {
                if (app.Session.Phase == CoachPhase.Changeover)
                {
                    if (app.Root.Q(className: "tsc-banner") != null) banners++;
                    app.Resume(null);
                }
                else app.SkipToNextStop();
                yield return null;
            }
            Assert.That(banners, Is.GreaterThan(0), "opponent coach banner shown");
            Assert.That(app.Root.Q<HalfCourtView>(), Is.Not.Null);
            Assert.That(app.Session.Engine.Record.Status, Is.EqualTo("Completed"));
            Object.Destroy(host);
        }

        // Without a change, the opponent coach's note on the user's change is the same InfoBanner with its own kicker
        // (seed 42: Aggressive at the first changeover gives a note-only banner at the second, CoachChecks).
        [UnityTest]
        public IEnumerator NoteOnlyBannerShowsAtTheSecondChangeover()
        {
            var host = new GameObject("CoachNoteTest");
            var app = host.AddComponent<CoachApp>();
            app.Seed = 42;
            yield return null;
            app.StartMatch(new Tactic());
            yield return null;
            app.SkipToNextStop();
            yield return null;
            Assert.That(app.Session.Phase, Is.EqualTo(CoachPhase.Changeover));
            app.Resume(new Tactic { Aggression = Aggression.Aggressive });
            yield return null;
            app.SkipToNextStop();
            yield return null;
            Assert.That(app.Session.Phase, Is.EqualTo(CoachPhase.Changeover));
            Assert.That(app.Session.OpponentChange, Is.Null, "no change at the second changeover");
            Assert.That(app.Session.OpponentNote, Is.Not.Null, "a note instead");
            Assert.That(app.Root.Q(className: "tsc-banner"), Is.Not.Null, "the note shows as the InfoBanner");
            Object.Destroy(host);
        }
    }
}
