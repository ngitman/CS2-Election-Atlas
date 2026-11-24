using System;
using System.Collections.Generic;
using System.Linq;
using ElectionAtlas.Model;
using Xunit;

namespace ElectionAtlas.Model.Tests
{
    public class ElectionNightTests
    {
        private static readonly ModelConfig Cfg = new ModelConfig();
        private static readonly int[] WaveSizes = { 1, 2, 5 };

        // ---- fixtures --------------------------------------------------------------------------------------

        /// <summary>A model run captured the way the mod does it, with ids like the mod's and optionally an outside bucket.</summary>
        private static ElectionSnapshot ModelSnapshot(GroupTable table, Party[] parties, GoverningRule rule, ulong seed = 9, int outside = -1)
        {
            var result = ElectionModel.RunElection(table, parties, null, rule, TurnoutPreset.UsGeneral, Cfg, seed, default, outside);
            var infos = Enumerable.Range(0, table.DistrictCount)
                .Select(i => i == outside ? DistrictInfo.Outside : new DistrictInfo((1L << 32) | (uint)(500 + i), $"District {i}"))
                .ToArray();
            return ElectionSnapshot.Capture(result, parties, infos, 1, 2031, 4, Cfg.Map);
        }

        /// <summary>Two small rich districts vote Right, one huge poor district votes Left (as in ElectionTests).</summary>
        private static GroupTable SplitCity() => new GroupTable(3, new[]
        {
            new VoterGroup(0, AgeBand.Adult, 4, 3, JobSector.Office, 200, 50f),
            new VoterGroup(1, AgeBand.Adult, 4, 3, JobSector.Office, 200, 50f),
            new VoterGroup(2, AgeBand.Adult, 0, 0, JobSector.Unemployed, 5000, 50f),
        });

        private static ElectionSnapshot Hand(GoverningRule rule, params (long, string, long, long[])[] districts) =>
            SampleNights.Build(1, 2031, 4, rule, 77, 0.6f, SampleParties.Two(), districts);

        private static ElectionSnapshot OneDistrict() =>
            Hand(GoverningRule.PopularVote, SampleNights.District(1, "Only", 1000, 300, 340));

        private static ElectionSnapshot NoVotes() => Hand(GoverningRule.MostDistricts,
            SampleNights.District(1, "North", 800, 0, 0),
            SampleNights.District(2, "South", 600, 0, 0),
            SampleNights.District(DistrictInfo.OutsideId, DistrictInfo.OutsideName, 50, 0, 0));

        private static ElectionSnapshot AllTies(GoverningRule rule) => Hand(rule,
            SampleNights.District(1, "North", 800, 250, 250),
            SampleNights.District(2, "South", 600, 100, 100),
            SampleNights.District(3, "East", 900, 400, 400));

        /// <summary>Every kind of snapshot the replay must handle.</summary>
        private static IEnumerable<ElectionSnapshot> AllCases()
        {
            foreach (var sample in SampleNights.All()) yield return sample.Value;
            foreach (var rule in new[] { GoverningRule.PopularVote, GoverningRule.MostDistricts })
            {
                yield return ModelSnapshot(TestCity.Random(districts: 24, seed: 3), TestCity.TwoParties, rule);
                yield return ModelSnapshot(TestCity.Random(districts: 18, seed: 4), SampleParties.Four(), rule, outside: 17);
                yield return ModelSnapshot(SplitCity(), TestCity.TwoParties, rule);
                yield return AllTies(rule);
            }
            yield return OneDistrict();
            yield return NoVotes();
        }

        private static IEnumerable<ElectionNight> AllNights() =>
            from s in AllCases() from w in WaveSizes select ElectionNight.ByDistrict(s, w);

        /// <summary>Everything a frame shows, as one comparable string per frame.</summary>
        private static List<string> Signature(ElectionNight night)
        {
            var frames = new List<string>();
            for (int f = 0; f < night.FrameCount; f++)
            {
                var fr = night[f];
                var wave = Enumerable.Range(0, fr.WaveCount).Select(k => night.Units[fr.WaveUnit(k)].Name);
                var votes = Enumerable.Range(0, night.PartyCount).Select(fr.PartyVotes);
                var seats = Enumerable.Range(0, night.PartyCount).Select(fr.Seats);
                frames.Add($"{f}|{fr.UnitsReported}|{fr.SeatsReported}|{string.Join(",", wave)}|{string.Join(",", votes)}|"
                    + $"{string.Join(",", seats)}|{fr.City.Eligible}|{fr.LeaderIndex}|{fr.LeadChanged}|{fr.IsFinal}");
            }
            return frames;
        }

        private static int[] RevealOrder(ElectionNight night) => Enumerable.Range(0, night.UnitCount).Select(night.RevealedUnit).ToArray();

        // ---- running counts --------------------------------------------------------------------------------

        [Fact]
        public void EachPartysRunningCount_RisesMonotonically()
        {
            foreach (var night in AllNights())
            {
                for (int f = 1; f < night.FrameCount; f++)
                {
                    ReportFrame before = night[f - 1], now = night[f];
                    for (int p = 0; p < night.PartyCount; p++)
                    {
                        Assert.True(now.PartyVotes(p) >= before.PartyVotes(p), $"party {p} fell in frame {f}");
                        Assert.True(now.Seats(p) >= before.Seats(p), $"party {p} lost a seat in frame {f}");
                    }

                    Assert.True(now.Votes >= before.Votes);
                    Assert.True(now.City.Eligible >= before.City.Eligible);
                    Assert.True(now.UnitsReported > before.UnitsReported, "every frame after the first reports something");
                    Assert.True(now.SeatsReported >= before.SeatsReported);
                }
            }
        }

        [Fact]
        public void Frames_AddUpTheirOwnWaves()
        {
            foreach (var night in AllNights())
            {
                var votes = new long[night.PartyCount];
                for (int f = 0; f < night.FrameCount; f++)
                {
                    var frame = night[f];
                    for (int k = 0; k < frame.WaveCount; k++)
                    {
                        var unit = night.Units[frame.WaveUnit(k)];
                        for (int p = 0; p < votes.Length; p++) votes[p] += unit.PartyVotes[p];
                    }

                    for (int p = 0; p < votes.Length; p++) Assert.Equal(votes[p], frame.PartyVotes(p));
                    Assert.Equal(frame.City.Votes, votes.Sum());
                }
            }
        }

        [Fact]
        public void Waves_ReportEveryUnitExactlyOnce_InOrder()
        {
            var night = ElectionNight.ByDistrict(SampleNights.CloseRace(), waveSize: 3);

            Assert.Equal(5, night.FrameCount); // polls close, then 3 + 3 + 3 + 1
            Assert.Equal(new[] { 0, 3, 3, 3, 1 }, Enumerable.Range(0, 5).Select(f => night[f].WaveCount));

            var seen = new List<int>();
            for (int f = 0; f < night.FrameCount; f++)
                for (int k = 0; k < night[f].WaveCount; k++)
                {
                    int unit = night[f].WaveUnit(k);
                    seen.Add(unit);
                    Assert.Equal(f, night.UnitReportFrame(unit));
                    Assert.True(night[f].IsUnitReported(unit));
                    Assert.False(night[f - 1].IsUnitReported(unit));
                }

            Assert.Equal(RevealOrder(night), seen);
            Assert.Equal(Enumerable.Range(0, 10), seen.OrderBy(u => u));
        }

        [Fact]
        public void RegionReportFrame_IsWhenTheMapShowsTheDistrict()
        {
            foreach (var night in AllNights())
                for (int f = 0; f < night.FrameCount; f++)
                    for (int r = 0; r < night.Snapshot.Districts.Length; r++)
                        Assert.Equal(night.RegionReportFrame(r) <= f, night[f].IsRegionReported(r));
        }

        // ---- the final frame is the archive ----------------------------------------------------------------

        [Fact]
        public void FinalFrame_EqualsTheSnapshotExactly()
        {
            foreach (var night in AllNights())
            {
                var s = night.Snapshot;
                var final = night.Final;
                var votes = new long[night.PartyCount];
                var seats = new int[night.PartyCount];
                final.CopyTo(votes, seats);

                Assert.True(final.IsFinal);
                Assert.Equal(s.PartyVotes, votes);
                Assert.Equal(s.DistrictsWon, seats);
                Assert.Equal(s.Votes, final.Votes);
                Assert.Equal(s.Eligible, final.City.Eligible);
                Assert.Equal(night.UnitCount, final.UnitsReported);
                Assert.Equal(night.SeatCount, final.SeatsReported);
                Assert.Equal(s.GoverningPartyIndex, final.LeaderIndex);
                Assert.All(Enumerable.Range(0, s.Districts.Length), r => Assert.True(final.IsRegionReported(r)));
            }
        }

        [Fact]
        public void FullCount_AgreesWithTheArchivedGoverningParty_UnderBothRules()
        {
            foreach (var night in AllNights())
            {
                var s = night.Snapshot;
                var votes = new long[night.PartyCount];
                var seats = new int[night.PartyCount];
                night.Final.CopyTo(votes, seats);

                Assert.Equal(s.GoverningPartyIndex, ElectionModel.PickGoverningParty(s.Rule, votes, seats));
            }
        }

        [Fact]
        public void MostDistricts_CanDisagreeWithThePopularVote_AndTheReplayEndsOnTheArchive()
        {
            var popular = ElectionNight.ByDistrict(ModelSnapshot(SplitCity(), TestCity.TwoParties, GoverningRule.PopularVote));
            var districts = ElectionNight.ByDistrict(ModelSnapshot(SplitCity(), TestCity.TwoParties, GoverningRule.MostDistricts));

            Assert.Equal(0, popular.Final.LeaderIndex);   // Left: the huge poor district
            Assert.Equal(1, districts.Final.LeaderIndex); // Right: two districts to one
            Assert.Equal(0, districts.Final.City.WinnerIndex);
            Assert.Equal(districts.Snapshot.GoverningPartyIndex, districts.Final.LeaderIndex);

            var sample = ElectionNight.ByDistrict(SampleNights.CloseRace(GoverningRule.MostDistricts));
            Assert.Equal(1, sample.Final.LeaderIndex);     // Right carries six districts...
            Assert.Equal(0, sample.Final.City.WinnerIndex); // ...while Left wins the popular vote
            Assert.Equal(new[] { 4, 6 }, new[] { sample.Final.Seats(0), sample.Final.Seats(1) });
        }

        [Fact]
        public void FinalLeader_IsTheArchive_EvenWhereTheCountWouldSayOtherwise()
        {
            // An archive whose governing party isn't what today's rule gives on its counts (an older rule, say).
            var s = OneDistrict();
            var tampered = new ElectionSnapshot(s.Number, s.Year, s.Month, s.Rule, s.Seed, s.TargetTurnout, 0, s.Parties, s.Districts);

            var night = ElectionNight.ByDistrict(tampered);

            Assert.Equal(1, night.Final.City.WinnerIndex);
            Assert.Equal(0, night.Final.LeaderIndex);
        }

        // ---- same snapshot, same night ---------------------------------------------------------------------

        [Fact]
        public void SameSnapshot_GivesTheSameSequence()
        {
            foreach (var s in AllCases())
                foreach (int w in WaveSizes)
                    Assert.Equal(Signature(ElectionNight.ByDistrict(s, w)), Signature(ElectionNight.ByDistrict(s, w)));
        }

        [Fact]
        public void SnapshotRebuiltFieldByField_GivesTheSameSequence()
        {
            foreach (var s in AllCases())
            {
                var rebuilt = Rebuild(s, id => id);
                Assert.Equal(Signature(ElectionNight.ByDistrict(s)), Signature(ElectionNight.ByDistrict(rebuilt)));
            }
        }

        [Fact]
        public void ReloadedSnapshotWithRemappedDistrictIds_GivesTheSameSequence()
        {
            // The game renumbers district entities on load and the mod remaps every saved id (save format v3), so ids
            // change while the stored district order doesn't. Districts that no longer exist get negative ids near
            // -2^32. New ids here run the other way and half of them are negative, so an order sorted by id would differ.
            static long Remap(long id) =>
                id == DistrictInfo.OutsideId ? id
                : id % 2 == 0 ? -(1L << 32) + (id & 0xFFFF) // like a dissolved district
                : (1L << 40) - id;

            foreach (var s in AllCases())
            {
                var reloaded = Rebuild(s, Remap);
                var before = ElectionNight.ByDistrict(s);
                var after = ElectionNight.ByDistrict(reloaded);

                Assert.Equal(Signature(before), Signature(after));
                Assert.Equal(RevealOrder(before), RevealOrder(after));
                Assert.Equal(reloaded.Districts.Select(d => d.DistrictId), after.Units.Select(u => u.Id));
            }
        }

        private static ElectionSnapshot Rebuild(ElectionSnapshot s, Func<long, long> mapId)
        {
            var parties = s.Parties.Select(p => new PartySnapshot(p.Id, p.Name, p.Color, p.Economic, p.Social)).ToArray();
            var districts = s.Districts.Select(d => new DistrictSnapshot(
                mapId(d.DistrictId), d.Name, d.Eligible, d.PartyVotes.ToArray(), d.WinnerIndex, d.Margin, d.MapColor)).ToArray();
            return new ElectionSnapshot(s.Number, s.Year, s.Month, s.Rule, s.Seed, s.TargetTurnout, s.GoverningPartyIndex, parties, districts);
        }

        [Fact]
        public void TheSeedPicksTheOrder()
        {
            var s = ModelSnapshot(TestCity.Random(districts: 24, seed: 3), TestCity.TwoParties, GoverningRule.PopularVote);
            var orders = Enumerable.Range(0, 8)
                .Select(seed => new ElectionSnapshot(s.Number, s.Year, s.Month, s.Rule, (ulong)seed, s.TargetTurnout, s.GoverningPartyIndex, s.Parties, s.Districts))
                .Select(r => string.Join(",", RevealOrder(ElectionNight.ByDistrict(r))))
                .Distinct()
                .Count();

            Assert.Equal(8, orders);
        }

        [Fact]
        public void CloseRace_RevealOrderIsPinned()
        {
            // Players replay archived elections across mod updates: a change to the shuffle changes every old replay.
            var night = ElectionNight.ByDistrict(SampleNights.CloseRace());

            Assert.Equal(new[] { 1, 6, 8, 9, 4, 7, 5, 2, 0, 3 }, RevealOrder(night));
        }

        // ---- the fixtures ----------------------------------------------------------------------------------

        [Fact]
        public void CloseRace_HasALeadChange()
        {
            var night = ElectionNight.ByDistrict(SampleNights.CloseRace());
            var changes = Enumerable.Range(0, night.FrameCount).Where(f => night[f].LeadChanged).ToArray();

            Assert.Equal(2, night.LeadChanges);
            Assert.Equal(2, changes.Length);
            foreach (int f in changes) Assert.NotEqual(night[f - 1].LeaderIndex, night[f].LeaderIndex);
            Assert.Equal(155, night.Final.City.LeadVotes);
        }

        [Fact]
        public void FourParties_SplitsTheSeats_AndEndsOnTheArchive()
        {
            var s = SampleNights.FourParties();
            var night = ElectionNight.ByDistrict(s);

            Assert.Equal(4, night.PartyCount);
            Assert.Equal(new[] { 2, 3, 1, 2 }, s.DistrictsWon);
            Assert.Equal(1, s.GoverningPartyIndex); // Labour
            Assert.Equal(1, night.Final.LeaderIndex);
            Assert.True(night.LeadChanges >= 1);
        }

        [Fact]
        public void OutsideBucket_CountsCitywide_ButIsNeverASeat()
        {
            var s = SampleNights.OutsideResidents();
            var night = ElectionNight.ByDistrict(s);
            int outside = Array.FindIndex(s.Districts, d => !d.IsSeat);
            int frame = night.UnitReportFrame(outside);
            ReportFrame before = night[frame - 1], at = night[frame];

            Assert.Equal(4, night.SeatCount);
            Assert.Equal(5, night.UnitCount);
            for (int p = 0; p < 2; p++)
            {
                Assert.Equal(before.PartyVotes(p) + s.Districts[outside].PartyVotes[p], at.PartyVotes(p));
                Assert.Equal(before.Seats(p), at.Seats(p));
            }
            Assert.Equal(before.SeatsReported, at.SeatsReported);
            Assert.True(at.IsRegionReported(outside));

            Assert.Equal(new[] { 3, 1 }, new[] { night.Final.Seats(0), night.Final.Seats(1) });
            Assert.Equal(1, night.Final.City.WinnerIndex); // the outside vote gives Right the popular vote
            Assert.Equal(1, night.Final.LeaderIndex);
        }

        [Fact]
        public void OutsideBucket_InAModelRun_IsNeverASeat()
        {
            foreach (var rule in new[] { GoverningRule.PopularVote, GoverningRule.MostDistricts })
            {
                var s = ModelSnapshot(TestCity.Random(districts: 18, seed: 4), SampleParties.Four(), rule, outside: 17);
                var night = ElectionNight.ByDistrict(s);

                Assert.Equal(17, night.SeatCount);
                Assert.Equal(17, Enumerable.Range(0, 4).Sum(night.Final.Seats));
                Assert.True(s.Districts[17].Votes > 0);
            }
        }

        // ---- edge cases ------------------------------------------------------------------------------------

        [Fact]
        public void OneDistrict_IsOneFrameAfterThePollsClose()
        {
            var night = ElectionNight.ByDistrict(OneDistrict(), waveSize: 4);

            Assert.Equal(2, night.FrameCount);
            Assert.Equal(0, night.First.UnitsReported);
            Assert.Equal(-1, night.First.LeaderIndex);
            Assert.True(night[1].IsFinal);
            Assert.Equal(1, night.Final.LeaderIndex);
            Assert.Equal(0, night.LeadChanges);
        }

        [Fact]
        public void NoVotes_NobodyLeadsAtAnyPoint()
        {
            var night = ElectionNight.ByDistrict(NoVotes());

            Assert.Equal(-1, night.Snapshot.GoverningPartyIndex);
            Assert.Equal(4, night.FrameCount); // eligible voters but no ballots: each still reports
            for (int f = 0; f < night.FrameCount; f++)
            {
                Assert.Equal(-1, night[f].LeaderIndex);
                Assert.Equal(0, night[f].Votes);
                Assert.Equal(0, night[f].Seats(0) + night[f].Seats(1));
            }
            Assert.Equal(2, night.Final.SeatsReported);
            Assert.Equal(0, night.LeadChanges);
        }

        [Fact]
        public void EveryDistrictTied_GoesToTheLowerIndex_LikeTheModel()
        {
            foreach (var rule in new[] { GoverningRule.PopularVote, GoverningRule.MostDistricts })
            {
                var night = ElectionNight.ByDistrict(AllTies(rule));

                Assert.True(night.Final.City.IsTie);
                Assert.Equal(new[] { 3, 0 }, night.Snapshot.DistrictsWon);
                Assert.Equal(3, night.Final.Seats(0));
                for (int f = 1; f < night.FrameCount; f++) Assert.Equal(0, night[f].LeaderIndex);
                Assert.Equal(0, night.LeadChanges);
            }
        }

        [Fact]
        public void UnitsWithNoVoters_AreInWhenThePollsClose()
        {
            var s = Hand(GoverningRule.PopularVote,
                SampleNights.District(1, "North", 800, 300, 200),
                SampleNights.District(2, "Empty", 0, 0, 0),
                SampleNights.District(3, "South", 600, 100, 250),
                SampleNights.District(DistrictInfo.OutsideId, DistrictInfo.OutsideName, 0, 0, 0));
            var night = ElectionNight.ByDistrict(s);

            Assert.Equal(2, night.EmptyUnits);
            Assert.Equal(3, night.FrameCount);
            Assert.Equal(new[] { 1, 3 }, new[] { night.First.WaveUnit(0), night.First.WaveUnit(1) });
            Assert.Equal(1, night.First.SeatsReported); // the empty district has its (empty) result; the outside bucket isn't a seat
            Assert.Equal(0, night.First.Votes);
            Assert.Equal(-1, night.First.LeaderIndex);
            Assert.Equal(0, night.RegionReportFrame(1));
            Assert.Equal(0, night.UnitReportFrame(3));
        }

        [Fact]
        public void AnElectionWithNoDistricts_IsOneFinalFrame()
        {
            var night = ElectionNight.ByDistrict(Hand(GoverningRule.PopularVote));

            Assert.Equal(1, night.FrameCount);
            Assert.True(night.First.IsFinal);
            Assert.Equal(-1, night.Final.LeaderIndex);
            Assert.Equal(0, night.SeatCount);
        }

        // ---- units other than districts (precincts later) --------------------------------------------------

        [Fact]
        public void SplitUnits_DecideASeatOnlyWhenTheWholeDistrictIsIn()
        {
            var s = SampleNights.CloseRace();
            var units = new List<ReportingUnit>();
            for (int r = 0; r < s.Districts.Length; r++)
            {
                var d = s.Districts[r];
                long[] a = d.PartyVotes.Select(v => v / 3).ToArray();
                long[] b = d.PartyVotes.Select((v, p) => v - a[p]).ToArray();
                units.Add(new ReportingUnit(d.DistrictId * 10 + 1, d.Name + " A", r, d.Eligible / 3, a));
                units.Add(new ReportingUnit(d.DistrictId * 10 + 2, d.Name + " B", r, d.Eligible - d.Eligible / 3, b));
            }

            var night = ElectionNight.FromUnits(s, units, waveSize: 3);

            Assert.Equal(20, night.UnitCount);
            Assert.Equal(1 + 7, night.FrameCount);
            for (int f = 0; f < night.FrameCount; f++)
            {
                var frame = night[f];
                int complete = Enumerable.Range(0, s.Districts.Length).Count(r =>
                    frame.IsUnitReported(2 * r) && frame.IsUnitReported(2 * r + 1));
                Assert.Equal(complete, frame.SeatsReported);
                Assert.Equal(complete, frame.Seats(0) + frame.Seats(1));
                for (int r = 0; r < s.Districts.Length; r++)
                    Assert.Equal(frame.IsUnitReported(2 * r) && frame.IsUnitReported(2 * r + 1), frame.IsRegionReported(r));
            }

            Assert.Equal(s.PartyVotes, Enumerable.Range(0, 2).Select(night.Final.PartyVotes));
            Assert.Equal(s.DistrictsWon, Enumerable.Range(0, 2).Select(night.Final.Seats));
            Assert.Equal(s.GoverningPartyIndex, night.Final.LeaderIndex);
        }

        [Fact]
        public void Units_MustAddUpToTheArchive()
        {
            var s = OneDistrict();
            var d = s.Districts[0];

            Assert.Throws<ArgumentException>(() => ElectionNight.FromUnits(s, new[] { new ReportingUnit(1, "Only", 0, d.Eligible, new long[] { 300, 339 }) }));
            Assert.Throws<ArgumentException>(() => ElectionNight.FromUnits(s, new[] { new ReportingUnit(1, "Only", 0, d.Eligible - 1, d.PartyVotes) }));
            Assert.Throws<ArgumentException>(() => ElectionNight.FromUnits(s, Array.Empty<ReportingUnit>()));
            Assert.Throws<ArgumentException>(() => ElectionNight.FromUnits(s, new[] { new ReportingUnit(1, "Only", 1, d.Eligible, d.PartyVotes) }));
            Assert.Throws<ArgumentException>(() => ElectionNight.FromUnits(s, new[] { new ReportingUnit(1, "Only", 0, d.Eligible, new long[] { 640 }) }));
            Assert.Throws<ArgumentException>(() => new ReportingUnit(1, "Only", 0, 10, new long[] { -1, 5 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => ElectionNight.ByDistrict(s, waveSize: 0));
            Assert.Throws<ArgumentNullException>(() => ElectionNight.ByDistrict(null));
            Assert.Throws<ArgumentOutOfRangeException>(() => ElectionNight.ByDistrict(s).Frame(2));
            Assert.Throws<ArgumentOutOfRangeException>(() => ElectionNight.ByDistrict(s).Final.PartyVotes(2));
        }
    }
}
