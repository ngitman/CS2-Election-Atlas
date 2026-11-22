using System;
using System.Linq;
using System.Text.Json;
using ElectionAtlas.Model;
using Xunit;

namespace ElectionAtlas.Model.Tests
{
    public class PartyLineupSettingsTests
    {
        [Fact]
        public void DraftKeepsExistingIdsAndAssignsNewIdsThatSurviveReload()
        {
            var current = SampleParties.Two();
            var json = JsonSerializer.Serialize(new[]
            {
                Row("left", "Civic Alliance", "#9988AA", -0.3f, 0.1f),
                Row("right", "Right"),
                Row("", "Parti des Érables"),
            });

            var draft = PartyLineupSettings.ReadDraft(json, current);
            var loaded = PartyLineupSettings.ReadSaved(PartyLineupSettings.Write(draft));

            Assert.Equal("left", loaded[0].Id);
            Assert.Equal("Civic Alliance", loaded[0].Name);
            Assert.Equal(0x9988AAu, loaded[0].Color);
            Assert.Equal(new Position(-0.3f, 0.1f), loaded[0].Position);
            Assert.Equal("right", loaded[1].Id);
            Assert.True(Guid.TryParseExact(loaded[2].Id, "N", out _));
            Assert.Equal(draft[2].Id, loaded[2].Id);
            Assert.Equal("Parti des Érables", loaded[2].Name);
        }

        [Fact]
        public void PresetsCanRestoreTheirOriginalIds()
        {
            var preset = SampleParties.Four();
            var restored = PartyLineupSettings.ReadDraft(PartyLineupSettings.Write(preset), SampleParties.Two());
            Assert.Equal(preset.Select(p => p.Id), restored.Select(p => p.Id));
        }

        [Fact]
        public void DraftRejectsInvalidLineups()
        {
            var current = SampleParties.Two();
            foreach (var json in new[]
            {
                "not JSON",
                JsonSerializer.Serialize(new[] { Row("left", "Left") }),
                JsonSerializer.Serialize(new[] { Row("left", "Left"), Row("left", "Another") }),
                JsonSerializer.Serialize(new[] { Row("left", "Left"), Row("right", " left ") }),
                JsonSerializer.Serialize(new[] { Row("left", "Left"), Row("other", "Another") }),
                JsonSerializer.Serialize(new[] { Row("left", "Left"), Row("right", "Right", "##112233") }),
                JsonSerializer.Serialize(new[] { Row("left", "Left"), Row("right", "Right", economic: 1.1f) }),
            })
                Assert.Throws<FormatException>(() => PartyLineupSettings.ReadDraft(json, current));
        }

        [Fact]
        public void SavedLineupCannotAssignAReplacementId()
        {
            var json = JsonSerializer.Serialize(new[] { Row("left", "Left"), Row("", "Right") });
            Assert.Throws<FormatException>(() => PartyLineupSettings.ReadSaved(json));
        }

        private static object Row(string id, string name, string color = "#445566", float economic = 0f, float social = 0f)
            => new { id, name, color, economic, social };
    }
}
