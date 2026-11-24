using Colossal.UI.Binding;
using ElectionAtlas.Model;

namespace ElectionAtlas.Systems
{
    /// <summary>
    /// The live election: the newest election while its own Election Day plays (city clock or timed), until its
    /// finished count has been on screen. It owns the button beside Chirper and the two notices (polls closed,
    /// results in). The button's status comes from the <c>replay</c> binding, and the notices are checked when
    /// the replay publishes, so both change only with the hour or the count.
    /// Invariant: while <see cref="m_LiveElection"/> is set, <see cref="m_Replay"/> plays that election.
    /// </summary>
    public partial class ElectionsUISystem
    {
        private enum LivePhase { Voting, Counting, Results }

        private ElectionSnapshot m_LiveElection;
        private LivePhase m_LiveAnnounced;
        private ValueBinding<bool> m_LiveButton;
        private bool m_NotificationsOn;

        private RawValueBinding m_NoticeBinding;
        private ElectionSnapshot m_NoticeSnapshot;
        private bool m_NoticeIsResults;
        private int m_NoticeId;

        private void BindLiveElection()
        {
            m_NotificationsOn = Mod.Settings?.ElectionNotifications ?? true;
            if (Mod.Settings != null) Mod.Settings.onSettingsApplied += OnSettingsApplied;

            AddBinding(m_LiveButton = new ValueBinding<bool>(kGroup, "liveElection", false));
            AddBinding(m_NoticeBinding = new RawValueBinding(kGroup, "electionNotice", Guarded("electionNotice", WriteElectionNotice)));
            AddBinding(new TriggerBinding(kGroup, "toggleLiveElection", ToggleLiveElection));
            AddBinding(new TriggerBinding<int>(kGroup, "openElectionNotice", OpenElectionNotice));
            AddBinding(new TriggerBinding<int, bool>(kGroup, "dismissElectionNotice", (id, timedOut) =>
            {
                if (id != m_NoticeId || m_NoticeSnapshot == null) return;
                LogNotice(m_NoticeSnapshot, m_NoticeIsResults, timedOut ? "hidden after 8 seconds" : "dismissed");
                ClearElectionNotice();
            }));
        }

        private void UnbindLiveElection()
        {
            if (Mod.Settings != null) Mod.Settings.onSettingsApplied -= OnSettingsApplied;
        }

        /// <summary>Turning "Election notifications" off hides the button and any notice at once.</summary>
        private void OnSettingsApplied(Game.Settings.Setting setting)
        {
            bool on = Mod.Settings?.ElectionNotifications ?? true;
            if (on == m_NotificationsOn) return;
            m_NotificationsOn = on;
            if (!on) ClearElectionNotice();
            SyncLiveButton();
#if DEV_TOOLS
            Mod.Log.Info($"[election day] notice: notifications turned {(on ? "on" : "off")}");
#endif
        }

        /// <summary>Right after the newest election's Election Day starts. Moments it starts past aren't announced.</summary>
        private void BeginLiveElection()
        {
            if (m_Replay == null) return;
            m_LiveElection = m_Replay.Night.Snapshot;
            m_LiveAnnounced = PhaseOf(m_Replay);
            SyncLiveButton();
        }

        private void EndLiveElection()
        {
            if (m_LiveElection == null) return;
            m_LiveElection = null;
            SyncLiveButton();
        }

        private void SyncLiveButton()
        {
            bool show = m_LiveElection != null && m_NotificationsOn;
            if (show == m_LiveButton.value) return;
            m_LiveButton.Update(show);
#if DEV_TOOLS
            Mod.Log.Info(show ? $"[election day] live button: shown for election #{m_LiveElection.Number}" : "[election day] live button: hidden");
#endif
        }

        private static LivePhase PhaseOf(ElectionNightPlayback play) =>
            play.IsFinished ? LivePhase.Results : play.IsVoting ? LivePhase.Voting : LivePhase.Counting;

        /// <summary>From PublishReplay: announces the polls closing and the final count, once each.</summary>
        private void ObserveLiveElection()
        {
            if (m_LiveElection == null) return;
            var phase = PhaseOf(m_Replay);
            if (phase <= m_LiveAnnounced) return;
            m_LiveAnnounced = phase;
            bool results = phase == LivePhase.Results;
            // The open panel is showing this replay, so a notice would only repeat it.
            if (m_PanelOpen.value) LogNotice(m_LiveElection, results, "not shown, the panel shows the replay");
            else ShowElectionNotice(m_LiveElection, results);
        }

        /// <summary>The button beside Chirper: opens the panel on the live replay, or closes the panel.</summary>
        private void ToggleLiveElection()
        {
            if (m_PanelOpen.value)
            {
#if DEV_TOOLS
                Mod.Log.Info("[election day] live button: closed the panel");
#endif
                ClosePanel();
                return;
            }
            if (m_LiveElection == null || !m_Elections.HasLoadedCity) return;
            WatchElectionDay(m_LiveElection.Number - 1); // rejoins the live replay
            OpenPanel();
#if DEV_TOOLS
            Mod.Log.Info($"[election day] live button: opened the replay of election #{m_LiveElection.Number}");
#endif
        }

        private bool ShowElectionNotice(ElectionSnapshot snapshot, bool results)
        {
            if (!m_NotificationsOn || snapshot == null) return false;
            m_NoticeSnapshot = snapshot;
            m_NoticeIsResults = results;
            m_NoticeId++;
            m_NoticeBinding.Update();
            LogNotice(snapshot, results, "shown");
            return true;
        }

        private void ClearElectionNotice()
        {
            if (m_NoticeSnapshot == null) return;
            m_NoticeSnapshot = null;
            m_NoticeBinding.Update();
        }

        private bool IsArchived(ElectionSnapshot snapshot)
        {
            int index = snapshot.Number - 1;
            var archive = m_Elections.Ledger.Archive;
            return (uint)index < (uint)archive.Count && archive[index] == snapshot;
        }

        /// <summary>Watch opens the live replay; View results opens the Districts tab on that election.</summary>
        private void OpenElectionNotice(int id)
        {
            var snapshot = m_NoticeSnapshot;
            if (id != m_NoticeId || snapshot == null) return;
            bool results = m_NoticeIsResults;
            ClearElectionNotice();
            if (!m_Elections.HasLoadedCity || !IsArchived(snapshot)) return;
            LogNotice(snapshot, results, results ? "View results" : "Watch");

            int index = snapshot.Number - 1;
            if (results)
            {
                EndReplay();
                SelectElection(index, m_Elections.Ledger.Archive.Count);
            }
            else
            {
                WatchElectionDay(index); // rejoins the live replay
                if (snapshot != m_LiveElection) SkipToCount(); // a preview for an archived election
            }
            m_MapView.Update(false);
            OpenPanel();
        }

        private void WriteElectionNotice(IJsonWriter writer)
        {
            if (m_NoticeSnapshot == null) writer.WriteNull();
            else writer.ElectionNotice(m_NoticeId, m_NoticeIsResults, m_Elections.CityName,
                m_NoticeIsResults ? m_NoticeSnapshot.GoverningParty?.Name : null);
        }

        [System.Diagnostics.Conditional("DEV_TOOLS")]
        private static void LogNotice(ElectionSnapshot snapshot, bool results, string what) =>
            Mod.Log.Info($"[election day] notice: {(results ? "results" : "polls closed")} for election #{snapshot.Number}, {what}");

#if DEV_TOOLS
        /// <summary>Previews a notice for the live election, or the latest one; it shows even while the panel is open.</summary>
        internal string DevShowElectionNotice(bool results)
        {
            var snapshot = m_LiveElection ?? m_Elections.Ledger.Archive.Latest;
            if (snapshot == null) return "Run an election to preview its notifications.";
            if (!ShowElectionNotice(snapshot, results)) return "Election notifications are turned off in Options.";
            return results ? "Showing the results notice." : "Showing the polls-closed notice.";
        }
#endif
    }
}
