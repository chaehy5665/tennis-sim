using System.IO;
using NUnit.Framework;
using UnityEngine;
using TennisSim.Viewer.Editor;

namespace TennisSim.Viewer.Tests
{
    public sealed class ReplayEditModeTests
    {
        [Test]
        public void RealSampleDataContract()
        {
            ReplayChecks.Run(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Replays", "sample-42.json")), (name, test) => {
                Assert.DoesNotThrow(() => test(), name);
            });
        }
        [Test]
        public void CandidateDataContract()
        {
            string path = System.Environment.GetEnvironmentVariable("TENNISSIM_CANDIDATE_REPLAY");
            if (string.IsNullOrEmpty(path)) Assert.Ignore("Set TENNISSIM_CANDIDATE_REPLAY to test candidate; NOT_RUN");
            ReplayChecks.Run(File.ReadAllText(path), (name, test) => Assert.DoesNotThrow(() => test(), name), false);
        }
        [Test]
        public void SceneSetupIsRepeatableAndConnected()
        {
            ReplaySceneSetup.Setup(); ReplaySceneSetup.Setup();
            var viewers = Object.FindObjectsOfType<ReplayPresenter>();
            Assert.That(viewers.Length, Is.EqualTo(1));
            Assert.That(viewers[0].ReplayFile, Is.EqualTo("sample-42.json"));
            Assert.That(File.Exists(ReplaySceneSetup.ScenePath), Is.True);
        }
        [Test]
        public void UnityVectorMapping()
        {
            var mapped = ReplayPresenter.Map(new Position(-4.115, 2.65, 11.885));
            Assert.That(mapped.x, Is.EqualTo(-4.115).Within(1e-6));
            Assert.That(mapped.y, Is.EqualTo(2.65).Within(1e-6));
            Assert.That(mapped.z, Is.EqualTo(11.885).Within(1e-6));
        }
    }
}
