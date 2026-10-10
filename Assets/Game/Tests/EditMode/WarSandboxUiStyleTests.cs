using MassEngine.Game.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MassEngine.Game.Tests
{
    /// <summary>B "tactical command" UI: catalog facts, list groups and the top-right battle system strip.</summary>
    public sealed class WarSandboxUiStyleTests
    {
        [Test]
        public void DescriptionSplitsIntoCatalogFacts()
        {
            Assert.That(WarSandboxFrontEnd.TryParseDescription("2 支军团 · 1,200 人 · 歼灭战\n正面对冲。", out var armies, out var troops, out var mode, out var flavour), Is.True);
            Assert.That(armies, Is.EqualTo("2")); Assert.That(troops, Is.EqualTo("1,200")); Assert.That(mode, Is.EqualTo("歼灭战")); Assert.That(flavour, Is.EqualTo("正面对冲。"));
            Assert.That(WarSandboxFrontEnd.TryParseDescription("3 支军团 · 384 人 · 据点战", out armies, out troops, out mode, out flavour), Is.True);
            Assert.That(flavour, Is.Empty);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("一段没有固定格式的说明")]
        [TestCase("2 支军团 · 歼灭战\n缺少人数")]
        [TestCase("两军 · 512 人 · 歼灭战")]
        public void UnstructuredDescriptionIsKeptVerbatim(string description)
        {
            Assert.That(WarSandboxFrontEnd.TryParseDescription(description, out var armies, out var troops, out _, out var flavour), Is.False);
            Assert.That(armies, Is.Null); Assert.That(troops, Is.Null);
            Assert.That(flavour, Is.EqualTo(description ?? ""));
        }

        [TestCase("launch-open", "首发战役")]
        [TestCase("launch-point", "首发战役")]
        [TestCase("cavalry-mounted-knight", "新兵种")]
        [TestCase("dragons-dragon-evolved", "新兵种")]
        [TestCase("giants-alien", "新兵种")]
        [TestCase("nonhuman2-spider", "新兵种")]
        [TestCase("unified-large", "自由编成")]
        [TestCase("custom-field", "战场")]
        [TestCase(null, "战场")]
        public void CatalogGroupsFollowOfficialIdPrefixes(string id, string group)
        {
            Assert.That(WarSandboxFrontEnd.CatalogGroup(id), Is.EqualTo(group));
        }

        [Test]
        public void EveryOfficialEntryShowsStructuredFactsInAContiguousGroup()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>(OfficialRosterBuilder.CatalogPath);
            Assert.That(catalog, Is.Not.Null, OfficialRosterBuilder.CatalogPath);
            var seen = new System.Collections.Generic.List<string>(); string previous = null;
            foreach (var entry in catalog.entries)
            {
                Assert.That(WarSandboxFrontEnd.TryParseDescription(entry.description, out _, out _, out _, out _), Is.True, entry.id + ": " + entry.description);
                string group = WarSandboxFrontEnd.CatalogGroup(entry.id);
                if (group != previous) { Assert.That(seen, Does.Not.Contain(group), "group split in the list: " + group); seen.Add(group); previous = group; }
            }
            Assert.That(seen, Is.EqualTo(new[] { "首发战役", "新兵种", "自由编成" }));
        }

        [TestCase(640, 480)]
        [TestCase(1280, 720)]
        [TestCase(1920, 1080)]
        public void SystemStripSitsTopRightAndMatchesTheUiLayout(float width, float height)
        {
            Rect screen = WarSandboxFrontEnd.NavigationRect(width, height);
            Assert.That(screen.yMin, Is.LessThan(height * 0.1f)); Assert.That(screen.xMax, Is.GreaterThan(width * 0.9f));
            Vector2? previous = WarSandboxUGUI.ScreenSizeOverride;
            var owner = new GameObject("Navigation layout scale contract");
            WarSandboxUGUI uiSurface = null;
            try
            {
                WarSandboxUGUI.ScreenSizeOverride = new Vector2(width, height);
                uiSurface = new WarSandboxUGUI(owner.transform, "navigation-layout-test", 0);
                float scale = uiSurface.Scale;
                Assert.That(scale, Is.EqualTo(Mathf.Clamp(Mathf.Min(width / 1280f, height / 720f), 0.5f, 1.5f)));
                Rect ui = WarSandboxFrontEnd.NavigationLayout(uiSurface.Width);
                Assert.That(ui.x * scale, Is.EqualTo(screen.x).Within(0.01f)); Assert.That(ui.y * scale, Is.EqualTo(screen.y).Within(0.01f));
                Assert.That(ui.width * scale, Is.EqualTo(screen.width).Within(0.01f)); Assert.That(ui.height * scale, Is.EqualTo(screen.height).Within(0.01f));
                Assert.That(screen.xMax, Is.EqualTo(width - 16 * scale).Within(0.01f));
            }
            finally
            {
                WarSandboxUGUI.ScreenSizeOverride = previous;
                Object.DestroyImmediate(owner);
                if (uiSurface != null)
                {
                    // Dispose is a Play-mode API (deferred Destroy). Reclaim this fixture's
                    // native allocations immediately, then let Dispose unregister the instance.
                    foreach (string field in new[] { "font", "mono", "roundedSprite", "roundedTexture" })
                    {
                        var resource = (Object)typeof(WarSandboxUGUI).GetField(field,
                            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(uiSurface);
                        if (resource != null) Object.DestroyImmediate(resource);
                    }
                    uiSurface.Dispose();
                }
            }
        }
    }
}
