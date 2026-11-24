using System;
using System.Collections.Generic;

namespace ElectionAtlas.Model
{
    /// <summary>
    /// Small hand-made election snapshots for Election Night, shared by the Lab (which prints them and writes them as
    /// JSON for UI work), the replay tests and any later dev preview. Built the way <see cref="ElectionSnapshot.Capture"/>
    /// builds a real one: winners and margins from <see cref="RegionTally"/>, colors from <see cref="MapTint"/>, the
    /// governing party from <see cref="ElectionModel.PickGoverningParty"/>.
    /// </summary>
    public static class SampleNights
    {
        public const string CloseRaceName = "close-race";
        public const string CloseRaceMostDistrictsName = "close-race-most-districts";
        public const string FourPartiesName = "four-parties";
        public const string OutsideResidentsName = "outside-residents";

        /// <summary>
        /// Two parties, ten districts. Left wins the popular vote by 155 of 20,265 votes while Right carries six
        /// districts to four, and Lakeside is decided by 5 votes. The reveal changes the lead more than once.
        /// Under <see cref="GoverningRule.MostDistricts"/> Right governs instead.
        /// </summary>
        public static ElectionSnapshot CloseRace(GoverningRule rule = GoverningRule.PopularVote)
        {
            return Build(1, 2031, 3, rule, 0x7EA1_0001UL, 0.62f, SampleParties.Two(), new[]
            {
                District(101, "Harbor Point", 4200, 1650, 1010),
                District(102, "Mill Row", 3900, 1480, 1100),
                District(103, "Old Town", 3100, 1120, 1050),
                District(104, "Midtown", 3600, 1190, 1240),
                District(105, "Riverside", 2800, 860, 1090),
                District(106, "The Heights", 2500, 640, 1080),
                District(107, "Elm Park", 2200, 760, 820),
                District(108, "Northgate", 2900, 1050, 930),
                District(109, "Cedar Hill", 2000, 560, 830),
                District(110, "Lakeside", 2600, 900, 905),
            });
        }

        /// <summary>
        /// Four parties, eight districts, most districts governs. Labour edges the Conservatives by 50 votes and wins
        /// three districts; the Greens and Conservatives take two each and the Liberals one.
        /// </summary>
        public static ElectionSnapshot FourParties()
        {
            return Build(2, 2032, 9, GoverningRule.MostDistricts, 0x7EA1_0002UL, 0.55f, SampleParties.Four(), new[]
            {
                //                                          Greens Labour Liberals Conservatives
                District(201, "Riverside", 5000, 900, 1300, 700, 600),
                District(202, "University Hill", 3500, 1100, 600, 500, 300),
                District(203, "Old Town", 4000, 700, 900, 850, 800),
                District(204, "Westbrook", 4500, 300, 600, 900, 1300),
                District(205, "Marina", 3000, 400, 450, 950, 700),
                District(206, "Foundry", 4200, 350, 1400, 400, 750),
                District(207, "Oakmont", 3800, 250, 500, 800, 1350),
                District(208, "Greenway", 2600, 750, 500, 450, 400),
            });
        }

        /// <summary>
        /// Two parties, four districts and residents outside every drawn district. Left wins three of the four seats,
        /// but the outside vote leans Right and gives Right the popular vote, so Right governs.
        /// </summary>
        public static ElectionSnapshot OutsideResidents()
        {
            return Build(3, 2033, 6, GoverningRule.PopularVote, 0x7EA1_0003UL, 0.6f, SampleParties.Two(), new[]
            {
                District(301, "Central", 3000, 1100, 900),
                District(302, "Eastside", 2500, 950, 700),
                District(303, "Hillcrest", 2000, 600, 750),
                District(304, "Southport", 2800, 1000, 850),
                District(DistrictInfo.OutsideId, DistrictInfo.OutsideName, 3500, 700, 1500),
            });
        }

        /// <summary>Every sample by name, in a stable order.</summary>
        public static IReadOnlyList<KeyValuePair<string, ElectionSnapshot>> All()
        {
            return new[]
            {
                new KeyValuePair<string, ElectionSnapshot>(CloseRaceName, CloseRace()),
                new KeyValuePair<string, ElectionSnapshot>(CloseRaceMostDistrictsName, CloseRace(GoverningRule.MostDistricts)),
                new KeyValuePair<string, ElectionSnapshot>(FourPartiesName, FourParties()),
                new KeyValuePair<string, ElectionSnapshot>(OutsideResidentsName, OutsideResidents()),
            };
        }

        /// <summary>A district as raw counts: id, name, eligible voters, then votes per party.</summary>
        public static (long Id, string Name, long Eligible, long[] Votes) District(long id, string name, long eligible, params long[] votes) =>
            (id, name, eligible, votes);

        /// <summary>
        /// A snapshot from raw district counts, filled in as <see cref="ElectionSnapshot.Capture"/> would (winner,
        /// margin and color per district, then the governing party under <paramref name="rule"/>).
        /// </summary>
        public static ElectionSnapshot Build(int number, int year, int month, GoverningRule rule, ulong seed, float targetTurnout,
            IReadOnlyList<Party> parties, IReadOnlyList<(long Id, string Name, long Eligible, long[] Votes)> districts, MapTint tint = null)
        {
            if (parties == null) throw new ArgumentNullException(nameof(parties));
            if (districts == null) throw new ArgumentNullException(nameof(districts));
            tint = tint ?? new MapTint();

            var partySnaps = new PartySnapshot[parties.Count];
            for (int p = 0; p < partySnaps.Length; p++)
            {
                var party = parties[p];
                partySnaps[p] = new PartySnapshot(party.Id, party.Name, party.Color, party.Position.Economic, party.Position.Social);
            }

            var districtSnaps = new DistrictSnapshot[districts.Count];
            for (int i = 0; i < districtSnaps.Length; i++)
            {
                var d = districts[i];
                var tally = RegionTally.From(d.Votes, d.Eligible);
                int w = tally.WinnerIndex;
                uint color = tint.DistrictColor(w, w >= 0 ? parties[w].Color : 0u, tally.Margin);
                districtSnaps[i] = new DistrictSnapshot(d.Id, d.Name, d.Eligible, (long[])d.Votes.Clone(), w, tally.Margin, color);
            }

            // The snapshot sums its own totals; read them back to pick who governs, then freeze the real one.
            var draft = new ElectionSnapshot(number, year, month, rule, seed, targetTurnout, -1, partySnaps, districtSnaps);
            int governing = ElectionModel.PickGoverningParty(rule, draft.PartyVotes, draft.DistrictsWon);
            return new ElectionSnapshot(number, year, month, rule, seed, targetTurnout, governing, partySnaps, districtSnaps);
        }
    }
}
