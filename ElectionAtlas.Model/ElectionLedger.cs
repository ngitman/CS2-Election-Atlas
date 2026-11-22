using System;

namespace ElectionAtlas.Model
{
    /// <summary>What a month tick asks the game side to run.</summary>
    public readonly struct MonthTick
    {
        public MonthTick(bool isNewMonth, bool runPoll, bool runElection)
        {
            IsNewMonth = isNewMonth;
            RunPoll = runPoll;
            RunElection = runElection;
        }

        public readonly bool IsNewMonth;
        public readonly bool RunPoll;
        public readonly bool RunElection;
    }

    /// <summary>
    /// Everything about a city's elections that is kept between months and saved: the schedule, who governs,
    /// shutdown months this term, past elections and polls. Pure bookkeeping, so it can be unit-tested; the
    /// game side feeds it dates and City Hall state and runs the polls and elections it asks for.
    /// Dates are month indexes (<see cref="ElectionSchedule.MonthIndex"/>).
    /// </summary>
    public sealed class ElectionLedger
    {
        /// <summary>
        /// Save format. 1: ledger, elections, polls. 2: adds an "includes data" flag after the version. 3: adds a
        /// district table after the flag (each live district entity with its id at save time), so ids can be mapped
        /// to the renumbered entities on load.
        /// </summary>
        public const int CurrentVersion = 3;

        public GovernmentState State = GovernmentState.NoCityHall;
        public int UnlockedMonth = ElectionSchedule.NotScheduled;
        public int NextElectionMonth = ElectionSchedule.NotScheduled;
        public int LastMonthSeen = ElectionSchedule.NotScheduled;
        public string GoverningPartyId;
        public float ShutdownMonths;
        public ElectionArchive Archive = new ElectionArchive();
        public PollHistory Polls = new PollHistory();

        /// <summary>Call when the City Hall gate changes state.</summary>
        public void OnGovernmentChanged(GovernmentState from, GovernmentState to, int now, ElectionCadence cadence)
        {
            State = to;
            if (from == GovernmentState.NoCityHall && to == GovernmentState.AwaitingFounding)
            {
                UnlockedMonth = now;
                NextElectionMonth = ElectionSchedule.FoundingElection(now, cadence);
                ShutdownMonths = 0f;
            }
            else if (to == GovernmentState.NoCityHall)
            {
                // Demolished before the founding vote: the founding clock starts over next time.
                UnlockedMonth = ElectionSchedule.NotScheduled;
                NextElectionMonth = ElectionSchedule.NotScheduled;
                ShutdownMonths = 0f;
            }
            else if (from == GovernmentState.Suspended && to == GovernmentState.Active)
            {
                // Missed elections aren't made up: continue from the next date on the cadence.
                SkipMissedElections(now, cadence);
            }
        }

        /// <summary>Fills in a schedule when one is missing, e.g. a city loaded without saved election data.</summary>
        public void EnsureScheduled(int now, ElectionCadence cadence)
        {
            if (NextElectionMonth != ElectionSchedule.NotScheduled)
                return;

            if (State == GovernmentState.AwaitingFounding)
            {
                if (UnlockedMonth == ElectionSchedule.NotScheduled) UnlockedMonth = now;
                NextElectionMonth = ElectionSchedule.FoundingElection(UnlockedMonth, cadence);
            }
            else if (State == GovernmentState.Active)
            {
                NextElectionMonth = ElectionSchedule.NextAfter(now, cadence);
            }
        }

        /// <summary>
        /// Call on every update with the current month. Only acts once per new month: counts a shutdown month
        /// and says whether a poll and an election are due.
        /// </summary>
        public MonthTick OnMonth(int now, bool cityHallShutDown)
        {
            if (LastMonthSeen != ElectionSchedule.NotScheduled && now <= LastMonthSeen)
                return default;

            LastMonthSeen = now;
            bool governing = State == GovernmentState.AwaitingFounding || State == GovernmentState.Active;
            if (governing && cityHallShutDown)
                ShutdownMonths += 1f;

            return new MonthTick(
                isNewMonth: true,
                runPoll: GovernmentGate.PollsRun(State),
                runElection: governing && ElectionSchedule.IsDue(now, NextElectionMonth));
        }

        /// <summary>The government's condition for the model: shutdown months this term, or the efficiency gap.</summary>
        public GovernmentCondition Condition(bool hasCityHall, bool shutDown, float efficiency)
        {
            float deficit = hasCityHall && !shutDown ? 1f - efficiency : 0f;
            return new GovernmentCondition(ShutdownMonths, deficit);
        }

        /// <summary>Records a held election and schedules the next one.</summary>
        public void RecordElection(ElectionSnapshot snapshot, int now, ElectionCadence cadence)
        {
            Archive.Add(snapshot);
            GoverningPartyId = snapshot.GoverningParty?.Id;
            ShutdownMonths = 0f;
            NextElectionMonth = ElectionSchedule.NextAfter(now, cadence);
            if (State == GovernmentState.AwaitingFounding)
                State = GovernmentState.Active;
        }

        /// <summary>Moves an overdue election to the next date on the cadence after now.</summary>
        public void SkipMissedElections(int now, ElectionCadence cadence)
        {
            if (NextElectionMonth == ElectionSchedule.NotScheduled)
            {
                NextElectionMonth = ElectionSchedule.NextAfter(now, cadence);
                return;
            }

            int step = ElectionSchedule.MonthsBetween(cadence);
            while (NextElectionMonth <= now)
                NextElectionMonth += step;
        }

        /// <summary>
        /// Replaces every district id in the archive and the polls with <paramref name="remap"/>'s, as copies (the
        /// records themselves are immutable). Everything else, including which polls keep district detail, is kept.
        /// </summary>
        public void RemapDistricts(DistrictRemap remap)
        {
            if (remap == null) throw new ArgumentNullException(nameof(remap));

            var archive = new ElectionArchive();
            foreach (var election in Archive.All) archive.Add(election.WithDistrictIds(remap));

            var polls = new PollHistory(Polls.Capacity, Polls.DistrictWindow);
            foreach (var record in Polls.Records) polls.Add(record.WithDistrictIds(remap));

            Archive = archive;
            Polls = polls;
        }

        /// <summary>Starts over (new city, or a dev reset).</summary>
        public void Reset()
        {
            State = GovernmentState.NoCityHall;
            UnlockedMonth = ElectionSchedule.NotScheduled;
            NextElectionMonth = ElectionSchedule.NotScheduled;
            LastMonthSeen = ElectionSchedule.NotScheduled;
            GoverningPartyId = null;
            ShutdownMonths = 0f;
            Archive = new ElectionArchive();
            Polls = new PollHistory();
        }
    }
}
