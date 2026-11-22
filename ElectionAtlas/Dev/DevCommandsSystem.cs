#if DEV_TOOLS
using System;
using System.IO;
using System.Linq;
using System.Text;
using Colossal.UI.Binding;
using ElectionAtlas.Diagnostics;
using ElectionAtlas.Model;
using ElectionAtlas.Systems;
using Game.Simulation;
using Game.UI;
using UnityEngine;

namespace ElectionAtlas.Dev
{
    /// <summary>
    /// Dev builds only (DEV_TOOLS). Commands behind the Developer section of the Elections panel, so most
    /// testing needs no relaunch. Elections and polls go through the real ElectionSystem (same archive, same
    /// schedule); the time controls move the mod's idea of "now" forward so the calendar can be tested without
    /// waiting in game.
    /// </summary>
    public partial class DevCommandsSystem : UISystemBase
    {
        private const string kGroup = ElectionsUISystem.kGroup;

        private CityHallSystem m_CityHall;
        private ElectionSystem m_Elections;
        private TimeSystem m_TimeSystem;
        private VoterReader m_Reader; // its own instance: never the one the census is using

        private ValueBinding<string> m_Status;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_CityHall = World.GetOrCreateSystemManaged<CityHallSystem>();
            m_Elections = World.GetOrCreateSystemManaged<ElectionSystem>();
            m_TimeSystem = World.GetOrCreateSystemManaged<TimeSystem>();
            m_Reader = new VoterReader(this);

            AddBinding(new ValueBinding<bool>(kGroup, "devTools", true));
            AddBinding(m_Status = new ValueBinding<string>(kGroup, "devStatus", "Developer tools ready."));

            AddBinding(new TriggerBinding(kGroup, "devRunElection", () => Run("run election", m_Elections.RunElectionNow)));
            AddBinding(new TriggerBinding(kGroup, "devRunPoll", () => Run("run poll", m_Elections.RunPollNow)));
            AddBinding(new TriggerBinding(kGroup, "devAdvanceMonth", () => Run("advance month", () => m_Elections.DevAdvanceMonths(1))));
            AddBinding(new TriggerBinding(kGroup, "devSkipToElection", () => Run("skip to election", m_Elections.DevSkipToElection)));
            AddBinding(new TriggerBinding(kGroup, "devReset", () => Run("reset elections", m_Elections.DevReset)));
            AddBinding(new TriggerBinding(kGroup, "devSwitchParties", () => Run("switch parties", SwitchParties)));
            AddBinding(new TriggerBinding(kGroup, "devSpikeReport", () => Run("spike report", SpikeReport)));
            AddBinding(new TriggerBinding(kGroup, "devExportCity", () => Run("export city", ExportCity)));
            AddBinding(new TriggerBinding(kGroup, "devBenchmark", () => Run("benchmark readers", BenchmarkReaders)));
            AddBinding(new TriggerBinding<string>(kGroup, "devSetGovernment", s => Run("set government", () => SetGovernment(s))));

            Mod.Log.Info("[dev] developer commands registered");
        }

        // ---------------------------------------------------------------- commands

        private string SwitchParties()
        {
            var settings = Mod.Settings;
            if (settings == null) return "Settings aren't loaded.";
            settings.PartyLineup = settings.PartyLineup == PartyLineup.TwoParties ? PartyLineup.FourParties : PartyLineup.TwoParties;
            return $"Parties now: {string.Join(", ", m_Elections.CurrentParties().Select(p => p.Name))} (Options > Election Atlas > Parties). Applies from the next poll or election.";
        }

        private string SpikeReport()
        {
            var logger = World.GetExistingSystemManaged<SpikeLoggerSystem>();
            if (logger == null)
                return "Spike logger isn't running.";
            long ms = logger.RunFullReportNow("dev command");
            return $"Spike report written to ElectionAtlas.Spikes.log in {ms} ms.";
        }

        private string ExportCity()
        {
            var table = m_Reader.ReadAll(out _, out string[] names);
            string dir = Path.Combine(Application.persistentDataPath, "ModsData", Mod.Id, "dev");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"city-{DateTime.Now:yyyyMMdd-HHmmss}.csv");

            int month = Math.Min(11, Math.Max(0, (int)Math.Floor(m_TimeSystem.normalizedDate * 12f)));
            using (var writer = new StreamWriter(path, false, new UTF8Encoding(false)))
            {
                GroupTableCsv.Write(writer, table, names,
                    $"game date {m_TimeSystem.year}-{month + 1:00}, exported {DateTime.Now:yyyy-MM-dd HH:mm}");
            }

            Mod.Log.Info($"[dev] exported {table.Groups.Count} voter groups to {path}");
            return $"Exported {table.TotalVoters:N0} voters in {table.Groups.Count:N0} groups to\n{path}";
        }

        /// <summary>
        /// Times the voter reader and the model on this city: a full read on the calling thread (what dev commands
        /// and fallbacks pay), the worker time of one census slice, and the model warm.
        /// </summary>
        private string BenchmarkReaders()
        {
            const int runs = 7;
            const int slice = 1024; // ElectionSystem.kSliceHouseholds
            var table = m_Reader.ReadAll(out _, out _); // warm-up
            var full = new double[runs];
            var sw = new System.Diagnostics.Stopwatch();
            for (int i = 0; i < runs; i++)
            {
                sw.Restart(); m_Reader.ReadAll(out _, out _); full[i] = sw.Elapsed.TotalMilliseconds;
            }
            Array.Sort(full);
            double fullMs = full[runs / 2];
            int households = m_Reader.Stats.Households;
            int slices = Math.Max(1, (households + slice - 1) / slice);
            double sliceMs = fullMs / slices;

            var parties = m_Elections.CurrentParties();
            var cfg = new ModelConfig();
            var election = new double[runs];
            var poll = new double[runs];
            for (int i = 0; i < runs; i++)
            {
                sw.Restart(); ElectionModel.RunElection(table, parties, null, GoverningRule.PopularVote, TurnoutPreset.UsGeneral, cfg, (ulong)(i + 1)); election[i] = sw.Elapsed.TotalMilliseconds;
                sw.Restart(); ElectionModel.RunPoll(table, parties, null, cfg); poll[i] = sw.Elapsed.TotalMilliseconds;
            }
            Array.Sort(election);
            Array.Sort(poll);

            var lines = new[]
            {
                $"Benchmark on {households:N0} households / {table.TotalVoters:N0} voters (Burst code: {(m_Reader.LastReadUsedBurst ? "yes" : "NO, managed fallback")}).",
                $"Full read on the calling thread, median of {runs}: {fullMs:0.00} ms ({fullMs * 1000.0 / Math.Max(1, households):0.00} µs/household; min {full[0]:0.00} ms).",
                $"Monthly census: {slices} slices of {slice} households, ~{sliceMs:0.000} ms each on a worker thread; main thread only schedules them.",
                $"Model on {table.Groups.Count:N0} groups, median: election {election[runs / 2]:0.00} ms, poll {poll[runs / 2]:0.00} ms.",
            };
            Mod.Log.Info("[benchmark] " + string.Join(" | ", lines));
            return string.Join(Environment.NewLine, lines);
        }

        private string SetGovernment(string state)
        {
            if (!Enum.TryParse(state, out GovernmentState parsed))
                return $"Unknown government state '{state}'.";
            m_CityHall.DevSetState(parsed);
            return $"Government state forced to {parsed}. The next City Hall check (a few seconds) re-applies the gate rules from it.";
        }

        // ---------------------------------------------------------------- helpers

        private void Run(string name, Func<string> command)
        {
            try
            {
                m_Status.Update(command());
            }
            catch (Exception ex)
            {
                Mod.Log.Error($"[dev] {name} failed: {ex}");
                m_Status.Update($"{name} failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
#endif
