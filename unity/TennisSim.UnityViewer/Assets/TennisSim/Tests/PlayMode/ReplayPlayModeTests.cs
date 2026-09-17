using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TennisSim.Viewer.Tests
{
    public sealed class ReplayPlayModeTests
    {
        [UnityTest]
        public IEnumerator RealSceneWholeReplayAndControls() { return VerifyReplay(null); }
        [UnityTest]
        public IEnumerator CandidateWholeReplayAndControls()
        {
            string path = System.Environment.GetEnvironmentVariable("TENNISSIM_CANDIDATE_REPLAY");
            if (string.IsNullOrEmpty(path)) Assert.Ignore("Set TENNISSIM_CANDIDATE_REPLAY to test candidate; NOT_RUN");
            return VerifyReplay(path);
        }
        private IEnumerator VerifyReplay(string candidatePath)
        {
            yield return SceneManager.LoadSceneAsync("Replay", LoadSceneMode.Single);
            yield return null;
            var viewers = Object.FindObjectsOfType<ReplayPresenter>(); Assert.That(viewers.Length, Is.EqualTo(1));
            var viewer = viewers[0];
            if (candidatePath != null) viewer.Load(candidatePath);
            Assert.That(viewer.Error, Is.Empty); Assert.That(viewer.Controller, Is.Not.Null);
            Assert.That(GameObject.Find("Court"), Is.Not.Null); Assert.That(GameObject.Find("Net"), Is.Not.Null);
            Assert.That(GameObject.Find("ReplayCamera").GetComponent<Camera>(), Is.Not.Null);
            Assert.That(GameObject.Find("ReplayLight").GetComponent<Light>(), Is.Not.Null);
            Assert.That(viewer.PlayerA, Is.Not.Null); Assert.That(viewer.PlayerB, Is.Not.Null); Assert.That(viewer.Ball, Is.Not.Null);
            viewer.BuildStage(); Assert.That(viewer.transform.childCount, Is.EqualTo(1));
            foreach (var collider in viewer.GetComponentsInChildren<Collider>()) Assert.That(collider.enabled, Is.False);
            Assert.That(viewer.GetComponentsInChildren<Rigidbody>().Length, Is.Zero);
            var c = viewer.Controller;
            if (candidatePath == null)
            {
                Assert.That(c.Timeline.Data.FinalScore.PointsPlayed, Is.EqualTo(27));
                Assert.That(c.Timeline.Data.FinalScore.Winner, Is.EqualTo(1));
                Assert.That(c.Timeline.Data.FinalScore.Games, Is.EqualTo(new[] { 0, 6 }));
            }
            c.Restart(); var initial = viewer.Ball.position; c.SetPlaying(true);
            yield return null; Assert.That(c.Time, Is.GreaterThan(0));
            c.SetPlaying(false); double paused = c.Time; yield return null; Assert.That(c.Time, Is.EqualTo(paused));
            // Disable automatic Update during deterministic full traversal, still present actual Transforms.
            viewer.enabled = false;
            foreach (double speed in new[] { .25, 1.0, 2.0 }) { c.Restart(); c.SetSpeed(speed); c.SetPlaying(true); c.Advance(1); Assert.That(c.Time, Is.EqualTo(speed)); }
            c.Seek(1.5); viewer.Present(); Assert.That(Vector3.Distance(initial, viewer.Ball.position), Is.GreaterThan(.01f));
            c.Restart(); c.SetSpeed(2); c.SetPlaying(true); int events = 0, frames = 0;
            while (c.Playing)
            {
                c.Advance(1.0 / 30); viewer.Present();
                foreach (var e in c.CrossedEvents) Assert.That(e.Sequence, Is.EqualTo(events++));
                var s = c.State;
                Assert.That(Vector3.Distance(viewer.Ball.position, ReplayPresenter.Map(s.Ball)), Is.LessThan(1e-6f));
                Assert.That(Vector3.Distance(viewer.PlayerA.position - Vector3.up * .9f, ReplayPresenter.Map(s.A)), Is.LessThan(1e-6f));
                if (++frames % 1000 == 0) yield return null;
            }
            Assert.That(events, Is.EqualTo(c.Timeline.Data.Events.Length)); Assert.That(c.State.Score.PointsPlayed, Is.EqualTo(c.Timeline.Data.FinalScore.PointsPlayed));
            Assert.That(c.State.Score.Winner, Is.EqualTo(c.Timeline.Data.FinalScore.Winner)); Assert.That(c.State.Score.Games, Is.EqualTo(c.Timeline.Data.FinalScore.Games));
            foreach (var e in c.Timeline.Data.Events)
            {
                if (!e.IsMarker) continue;
                c.Seek(e.Time); viewer.Present();
                Assert.That(viewer.MarkerEvent, Is.Not.Null);
                Assert.That(viewer.MarkerEvent.Time, Is.EqualTo(e.Time));
                Assert.That(Vector3.Distance(viewer.Marker.position, ReplayPresenter.Map(e.State.Ball)), Is.LessThan(1e-6f));
            }
            c.Seek(100); viewer.Present(); Vector3 expected = viewer.Ball.position;
            c.Seek(200); c.Seek(100); viewer.Present(); Assert.That(viewer.Ball.position, Is.EqualTo(expected));
            c.Restart(); viewer.Present(); Assert.That(c.Time, Is.Zero); Assert.That(viewer.MarkerEvent, Is.Null);
            LogAssert.NoUnexpectedReceived();
            Debug.Log("TENNISSIM_FULL_REPLAY_VERIFIED points=" + c.Timeline.Data.FinalScore.PointsPlayed + " events=" + events + " file=" + c.Timeline.Data.FileName);
        }
    }
}
