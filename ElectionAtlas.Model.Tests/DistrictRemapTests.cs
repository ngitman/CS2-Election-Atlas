using System;
using System.Collections.Generic;
using System.Linq;
using ElectionAtlas.Model;
using Xunit;

namespace ElectionAtlas.Model.Tests
{
    public class DistrictRemapTests
    {
        private static readonly PartySnapshot[] Parties =
        {
            new PartySnapshot("left", "Left", 0xCC3333, -0.3f, -0.2f),
            new PartySnapshot("right", "Right", 0x3355CC, 0.3f, 0.2f),
        };

        private static DistrictSnapshot District(long id, string name, long left = 60, long right = 40) =>
            new DistrictSnapshot(id, name, left + right + 50, new[] { left, right }, left >= right ? 0 : 1,
                Math.Abs(left - right) / (float)(left + right), 0x123456);

        private static DistrictSnapshot Outside() => District(DistrictInfo.OutsideId, DistrictInfo.OutsideName, 30, 20);

        private static ElectionSnapshot Election(int number, params DistrictSnapshot[] districts) =>
            new ElectionSnapshot(number, 2030 + number, 3, GoverningRule.MostDistricts, 7, 0.4f, 0, Parties, districts);

        private static PollRecord Poll(int month, long[] ids) =>
            new PollRecord(month, "left", new[] { "left", "right" }, 55f, new[] { 0.5f, 0.5f }, ids, ids?.Select(i => 50f + i % 7).ToArray());

        private static ElectionLedger Ledger(ElectionSnapshot[] elections, params PollRecord[] polls)
        {
            var ledger = new ElectionLedger();
            foreach (var e in elections) ledger.Archive.Add(e);
            foreach (var p in polls) ledger.Polls.Add(p);
            return ledger;
        }

        private static long[] Ids(ElectionSnapshot e) => e.Districts.Select(d => d.DistrictId).ToArray();

        [Fact]
        public void RemapDistricts_ReplacesSavedIdsInEveryElectionAndPoll()
        {
            var ledger = Ledger(
                new[]
                {
                    Election(1, District(100, "Old Town"), District(200, "Harbor", 30, 70), Outside()),
                    Election(2, District(200, "Harbor"), District(100, "Old Town", 45, 55), Outside()),
                },
                Poll(10, new long[] { 100, 200, DistrictInfo.OutsideId }),
                Poll(11, null));
            var before = ledger.Archive[0];
            var remap = new DistrictRemap(new Dictionary<long, long> { [100] = 5L << 32 | 7, [200] = 1L << 32 | 3 });

            ledger.RemapDistricts(remap);

            Assert.Equal(new[] { 5L << 32 | 7, 1L << 32 | 3, DistrictInfo.OutsideId }, Ids(ledger.Archive[0]));
            Assert.Equal(new[] { 1L << 32 | 3, 5L << 32 | 7, DistrictInfo.OutsideId }, Ids(ledger.Archive[1]));
            Assert.Equal(new[] { 5L << 32 | 7, 1L << 32 | 3, DistrictInfo.OutsideId }, ledger.Polls.Records[0].DistrictIds);
            Assert.Equal(new[] { 52f, 54f, 49f }, ledger.Polls.Records[0].DistrictApproval);
            Assert.Null(ledger.Polls.Records[1].DistrictIds);
            Assert.Equal((2, 0, 2), (remap.Matched, remap.Unmatched, remap.Total));

            // Everything but the ids is unchanged, totals included.
            var after = ledger.Archive[0];
            Assert.Equal(before.Districts.Select(d => d.Name), after.Districts.Select(d => d.Name));
            Assert.Equal(before.PartyVotes, after.PartyVotes);
            Assert.Equal(before.DistrictsWon, after.DistrictsWon);
            Assert.Equal((before.Number, before.Year, before.Seed), (after.Number, after.Year, after.Seed));
            Assert.Same(before.Parties, after.Parties);
        }

        [Fact]
        public void RemapDistricts_KeepsThePollWindow()
        {
            var ledger = new ElectionLedger { Polls = new PollHistory(capacity: 10, districtWindow: 2) };
            for (int m = 0; m < 5; m++) ledger.Polls.Add(Poll(m, new long[] { 100 }));

            ledger.RemapDistricts(new DistrictRemap(new Dictionary<long, long> { [100] = 4L << 32 }));

            Assert.Equal((10, 2, 5), (ledger.Polls.Capacity, ledger.Polls.DistrictWindow, ledger.Polls.Count));
            Assert.Equal(new long[][] { null, null, null, new[] { 4L << 32 }, new[] { 4L << 32 } },
                ledger.Polls.Records.Select(r => r.DistrictIds));
        }

        [Fact]
        public void Map_GivesEachDissolvedDistrictOneUniqueIdEverywhere()
        {
            long live = 1L << 32 | 9;
            var ledger = Ledger(
                new[]
                {
                    Election(1, District(100, "Old Town"), District(200, "Gone Hills"), District(300, "Lost Bay")),
                    Election(2, District(300, "Lost Bay"), District(200, "Gone Hills"), District(100, "Old Town")),
                },
                Poll(10, new long[] { 300, 100, 200 }));
            var remap = new DistrictRemap(new Dictionary<long, long> { [100] = live });

            ledger.RemapDistricts(remap);

            long gone = ledger.Archive[0].Districts[1].DistrictId, lost = ledger.Archive[0].Districts[2].DistrictId;
            Assert.NotEqual(gone, lost);
            Assert.Equal(new[] { lost, gone, live }, Ids(ledger.Archive[1]));
            Assert.Equal(new[] { lost, live, gone }, ledger.Polls.Records[0].DistrictIds);
            Assert.Equal((1, 2, 3), (remap.Matched, remap.Unmatched, remap.Total));

            foreach (long id in new[] { gone, lost })
            {
                Assert.True(id < DistrictInfo.OutsideId);              // never -1, never a live (positive) id
                Assert.Equal(-1, (int)(id >> 32));                     // as an entity: version -1, which no entity has
                Assert.InRange((int)(uint)(id & 0xFFFFFFFFL), 0, 10);  // and a small, valid index
                Assert.True(Math.Abs(id) < 1L << 53);                  // exact as a JavaScript number
                Assert.True(ledger.Archive[0].Districts.Single(d => d.DistrictId == id).IsSeat);
            }
        }

        [Fact]
        public void DissolvedId_StaysBelowTheOutsideBucket()
        {
            Assert.Equal(DistrictInfo.FirstDissolvedId, DistrictInfo.DissolvedId(0));
            Assert.True(DistrictInfo.DissolvedId(int.MaxValue) < DistrictInfo.OutsideId);
            Assert.NotEqual(DistrictInfo.DissolvedId(0), DistrictInfo.DissolvedId(1));
            Assert.Throws<ArgumentOutOfRangeException>(() => DistrictInfo.DissolvedId(-1));
        }

        [Fact]
        public void Map_LeavesTheOutsideBucketAlone()
        {
            var ledger = Ledger(new[] { Election(1, District(100, "Old Town"), Outside()) },
                Poll(10, new long[] { DistrictInfo.OutsideId, 100 }));
            // Even a table that claims to know -1 can't move it.
            var remap = new DistrictRemap(new Dictionary<long, long> { [DistrictInfo.OutsideId] = 1L << 32, [100] = 2L << 32 });

            ledger.RemapDistricts(remap);

            var outside = ledger.Archive[0].Districts[1];
            Assert.Equal(DistrictInfo.OutsideId, outside.DistrictId);
            Assert.False(outside.IsSeat);
            Assert.Equal(DistrictInfo.OutsideId, ledger.Polls.Records[0].DistrictIds[0]);
            Assert.Equal(new[] { 1, 0 }, ledger.Archive[0].DistrictsWon);
            Assert.Equal((1, 0, 1), (remap.Matched, remap.Unmatched, remap.Total));
        }

        [Fact]
        public void ByUniqueName_MatchesNamesUniqueOnBothSides()
        {
            var ledger = Ledger(
                new[]
                {
                    Election(1, District(100, "Old Town"), District(200, "Harbor"), District(300, "Hills"), Outside()),
                    Election(2, District(200, "Harbor"), District(100, "Old Town"), Outside()),
                },
                Poll(10, new long[] { 100, 200, 300, 400 }));
            var current = new[]
            {
                new DistrictInfo(1L << 32 | 4, "Harbor"),
                new DistrictInfo(1L << 32 | 2, "Old Town"),
                new DistrictInfo(1L << 32 | 8, "New Park"),
                DistrictInfo.Outside,
            };

            var remap = DistrictRemap.ByUniqueName(ledger, current);
            ledger.RemapDistricts(remap);

            Assert.Equal(new[] { 1L << 32 | 2, 1L << 32 | 4 }, Ids(ledger.Archive[0]).Take(2));
            long hills = ledger.Archive[0].Districts[2].DistrictId;
            Assert.True(hills < DistrictInfo.OutsideId); // dissolved before this save
            Assert.Equal(DistrictInfo.OutsideId, ledger.Archive[0].Districts[3].DistrictId);
            Assert.Equal(new[] { 1L << 32 | 4, 1L << 32 | 2, DistrictInfo.OutsideId }, Ids(ledger.Archive[1]));

            // Polls follow the elections; a district that was only polled has no name to match by.
            var poll = ledger.Polls.Records[0].DistrictIds;
            Assert.Equal(new[] { 1L << 32 | 2, 1L << 32 | 4, hills }, poll.Take(3));
            Assert.True(poll[3] < DistrictInfo.OutsideId && poll[3] != hills);
            Assert.Equal((2, 2, 4), (remap.Matched, remap.Unmatched, remap.Total));
        }

        [Fact]
        public void ByUniqueName_LeavesDuplicateNamesUnmapped()
        {
            var ledger = Ledger(new[]
            {
                // Two saved districts share "Riverside"; "Midtown" is unique in the save but not in the city now.
                Election(1, District(100, "Riverside"), District(200, "Riverside"), District(300, "Midtown"), District(400, "Docks")),
            });
            var current = new[]
            {
                new DistrictInfo(1L << 32 | 1, "Riverside"),
                new DistrictInfo(1L << 32 | 2, "Midtown"),
                new DistrictInfo(1L << 32 | 3, "Midtown"),
                new DistrictInfo(1L << 32 | 4, "Docks"),
            };

            var remap = DistrictRemap.ByUniqueName(ledger, current);
            ledger.RemapDistricts(remap);

            var ids = Ids(ledger.Archive[0]);
            Assert.Equal(1L << 32 | 4, ids[3]);
            Assert.All(ids.Take(3), id => Assert.True(id < DistrictInfo.OutsideId));
            Assert.Equal(3, ids.Take(3).Distinct().Count());
            Assert.Equal((1, 3), (remap.Matched, remap.Unmatched));
        }

        [Fact]
        public void ByUniqueName_HandlesRenamedDistricts()
        {
            // 100 was renamed North -> Northgate. Only the new name exists now: matched.
            // 200 was renamed East -> Eastgate, and today both names belong to different districts: ambiguous, unmatched.
            var ledger = Ledger(new[]
            {
                Election(1, District(100, "North"), District(200, "East")),
                Election(2, District(100, "Northgate"), District(200, "Eastgate")),
            });
            var current = new[]
            {
                new DistrictInfo(1L << 32 | 1, "Northgate"),
                new DistrictInfo(1L << 32 | 2, "East"),
                new DistrictInfo(1L << 32 | 3, "Eastgate"),
            };

            var remap = DistrictRemap.ByUniqueName(ledger, current);
            ledger.RemapDistricts(remap);

            Assert.Equal(1L << 32 | 1, ledger.Archive[0].Districts[0].DistrictId);
            Assert.Equal(1L << 32 | 1, ledger.Archive[1].Districts[0].DistrictId);
            Assert.True(ledger.Archive[0].Districts[1].DistrictId < DistrictInfo.OutsideId);
            Assert.Equal(ledger.Archive[0].Districts[1].DistrictId, ledger.Archive[1].Districts[1].DistrictId);
            Assert.Equal((1, 1), (remap.Matched, remap.Unmatched));
        }
    }
}
