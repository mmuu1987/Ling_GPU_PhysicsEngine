using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MassEngine.Game.Tests
{
    public sealed class WarSandboxPlayabilityTests
    {
        [TestCase(2)]
        [TestCase(3)]
        public void AllArmyResultsStayFrozenAfterTelemetryAndRosterChange(int winner)
        {
            var armies = new ArmyRuntimeState[4]; var alive = new int[4]; alive[winner] = 31;
            for (int i = 0; i < armies.Length; i++) armies[i] = new ArmyRuntimeState
                { teamId = i, displayName = "Army " + i, initialUnitCount = 100 + i };
            var sample = new BattleTelemetrySnapshot { valid = true, aliveByTeam = alive, battleSeconds = 42 };
            var result = WarSandboxBattleResult.Capture(WarSandboxBattlePhase.ArmyVictory, armies, sample,
                WarSandboxVictoryReason.Annihilation, winner);
            alive[winner] = 0; armies[winner].initialUnitCount = 1; armies[winner].displayName = "Changed";
            var row = result.GetArmy(winner); row.survivors = 999;
            var restored = JsonUtility.FromJson<WarSandboxBattleResult>(JsonUtility.ToJson(result));
            Assert.That(restored.ArmyCount, Is.EqualTo(4));
            Assert.That(restored.winnerTeamId, Is.EqualTo(winner));
            Assert.That(restored.GetArmy(winner).survivors, Is.EqualTo(31));
            Assert.That(restored.GetArmy(winner).initial, Is.EqualTo(100 + winner));
            Assert.That(restored.GetArmy(winner).displayName, Is.EqualTo("Army " + winner));
            Assert.That(restored.GetArmy(winner).Casualties, Is.EqualTo(69 + winner));
            Assert.That(restored.battleSeconds, Is.EqualTo(42));
        }

        [Test]
        public void ResultsSkipUnusedSlotsAndDoNotInventLossesForMissingSamples()
        {
            var armies = new[] { new ArmyRuntimeState { teamId = 0, initialUnitCount = 40 },
                new ArmyRuntimeState { teamId = 1, initialUnitCount = 0 },
                new ArmyRuntimeState { teamId = 3, initialUnitCount = 30 } };
            var result = WarSandboxBattleResult.Capture(WarSandboxBattlePhase.Ended, armies,
                new BattleTelemetrySnapshot { valid = true, aliveByTeam = new[] { 900 } }, WarSandboxVictoryReason.ManualEnd, -1);
            Assert.That(result.ArmyCount, Is.EqualTo(2)); Assert.That(result.winnerTeamId, Is.EqualTo(-1));
            Assert.That(result.GetArmy(0).survivors, Is.EqualTo(40));
            Assert.That(result.GetArmy(1).teamId, Is.EqualTo(3)); Assert.That(result.GetArmy(1).survivors, Is.EqualTo(30));
            Assert.That(result.TryGetArmy(1, out _), Is.False);
        }

        [Test]
        public void SettingsPersistAcrossStoreInstancesAndInvalidWritesPreserveTheFile()
        {
            string path = Path.Combine(Path.GetTempPath(), "SandboxAudio-" + Guid.NewGuid().ToString("N"), "settings.json");
            try
            {
                var store = new WarSandboxSettingsStore(path);
                Assert.That(store.TryLoad(out var defaults, out var error), Is.True, error);
                Assert.That(File.Exists(path), Is.False); Assert.That(defaults.IsValid, Is.True);
                var settings = WarSandboxAudioSettings.Default; settings.volume = .27f; settings.muted = true;
                Assert.That(store.TrySave(settings, out error), Is.True, error);
                Assert.That(new WarSandboxSettingsStore(path).TryLoad(out var loaded, out error), Is.True, error);
                Assert.That(loaded.volume, Is.EqualTo(.27f)); Assert.That(loaded.muted, Is.True);
                string before = File.ReadAllText(path); settings.volume = float.NaN;
                Assert.That(store.TrySave(settings, out error), Is.False); Assert.That(File.ReadAllText(path), Is.EqualTo(before));
                Assert.That(Directory.GetFiles(Path.GetDirectoryName(path)).Length, Is.EqualTo(1));
            }
            finally { if (Directory.Exists(Path.GetDirectoryName(path))) Directory.Delete(Path.GetDirectoryName(path), true); }
        }

        [Test]
        public void FailedSettingsReplacementPreservesBytesAndCleansTemporaryFile()
        {
            string folder = Path.Combine(Path.GetTempPath(), "SandboxAudio-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(folder, "settings.json");
            try
            {
                var store = new WarSandboxSettingsStore(path);
                Assert.That(store.TrySave(WarSandboxAudioSettings.Default, out var error), Is.True, error);
                byte[] before = File.ReadAllBytes(path);
                var changed = WarSandboxAudioSettings.Default; changed.volume = .17f; changed.muted = true;
                using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    Assert.That(store.TrySave(changed, out error), Is.False);
                    Assert.That(error, Is.Not.Empty);
                    Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
                    Assert.That(Directory.GetFiles(folder), Has.Length.EqualTo(1));
                }
                Assert.That(new WarSandboxSettingsStore(path).TryLoad(out var loaded, out error), Is.True, error);
                Assert.That(loaded.volume, Is.EqualTo(WarSandboxAudioSettings.Default.volume));
                Assert.That(loaded.muted, Is.False);
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }

        [TestCase("{broken")]
        [TestCase("{}")]
        [TestCase("{\"version\":2,\"volume\":0.5}")]
        [TestCase("{\"version\":1,\"volume\":4}")]
        public void DamagedSettingsFallBackWithoutOverwritingTheOriginal(string contents)
        {
            string path = Path.Combine(Path.GetTempPath(), "SandboxAudio-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllText(path, contents);
                Assert.That(new WarSandboxSettingsStore(path).TryLoad(out var settings, out var error), Is.False);
                Assert.That(settings.IsValid, Is.True); Assert.That(error, Is.Not.Empty);
                Assert.That(File.ReadAllText(path), Is.EqualTo(contents));
            }
            finally { File.Delete(path); }
        }

        [TestCase(WarSandboxSoundCue.Command)]
        [TestCase(WarSandboxSoundCue.Rejected)]
        [TestCase(WarSandboxSoundCue.Start)]
        [TestCase(WarSandboxSoundCue.ControlPoint)]
        [TestCase(WarSandboxSoundCue.Finish)]
        public void FeedbackCuesContainBoundedNonSilentAudioWithSmoothEndpoints(WarSandboxSoundCue cue)
        {
            var clip = WarSandboxSoundSynthesis.Create(cue);
            try
            {
                var samples = new float[clip.samples]; Assert.That(clip.GetData(samples, 0), Is.True);
                float peak = 0; foreach (float sample in samples) { Assert.That(float.IsNaN(sample), Is.False); peak = Mathf.Max(peak, Mathf.Abs(sample)); }
                Assert.That(peak, Is.InRange(.01f, .99f)); Assert.That(clip.length, Is.InRange(.05f, 1f));
                Assert.That(Mathf.Abs(samples[0]), Is.LessThan(.00001f)); Assert.That(Mathf.Abs(samples[samples.Length - 1]), Is.LessThan(.00001f));
            }
            finally { Object.DestroyImmediate(clip); }
        }
    }
}
