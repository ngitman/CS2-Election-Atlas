using Colossal.UI.Binding;
using ElectionAtlas.Model;

namespace ElectionAtlas.Systems
{
    public partial class ElectionsUISystem
    {
        // Only a newly run election drives automatic notices. History and sample replays share the same playback code.
        private ElectionSnapshot m_NoticeElection;
        private bool m_NoticeSawPollsClose;

        private RawValueBinding m_NoticeBinding;
        private ElectionSnapshot m_NoticeSnapshot;
        private bool m_NoticeIsResults;
        private int m_NoticeId;

        private static bool ElectionNotificationsEnabled => Mod.Settings?.ElectionNotifications ?? true;

        private void BindElectionNotice()
        {
            AddBinding(m_NoticeBinding = new RawValueBinding(kGroup, "electionNotice", Guarded("electionNotice", WriteElectionNotice)));
            AddBinding(new TriggerBinding<int>(kGroup, "openElectionNotice", OpenElectionNotice));
            AddBinding(new TriggerBinding<int>(kGroup, "dismissElectionNotice", id =>
            {
                if (id == m_NoticeId) ClearElectionNotice();
            }));
        }

        private void ObserveReplayForNotice()
        {
            var snapshot = m_NoticeElection;
            if (snapshot == null) return;

            if (m_Replay.IsFinished)
            {
                m_NoticeElection = null;
                ShowElectionNotice(snapshot, results: true);
            }
            else if (!m_Replay.IsVoting && !m_NoticeSawPollsClose)
            {
                m_NoticeSawPollsClose = true;
                ShowElectionNotice(snapshot, results: false);
            }
        }

        private bool ShowElectionNotice(ElectionSnapshot snapshot, bool results)
        {
            if (!ElectionNotificationsEnabled) return false;
            m_NoticeSnapshot = snapshot;
            m_NoticeIsResults = results;
            m_NoticeId++;
            m_NoticeBinding.Update();
#if DEV_TOOLS
            Mod.Log.Info($"[election day] notice: {(results ? "results" : "polls closed")} for election #{snapshot.Number}");
#endif
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
            return index < archive.Count && archive[index] == snapshot;
        }

        private void OpenElectionNotice(int id)
        {
            var snapshot = m_NoticeSnapshot;
            if (id != m_NoticeId || snapshot == null) return;
            ClearElectionNotice();
            if (!m_Elections.HasLoadedCity || !ElectionNotificationsEnabled || !IsArchived(snapshot)) return;

            int index = snapshot.Number - 1;
            if (m_NoticeIsResults) EndReplay();
            else if (m_Replay?.Night.Snapshot != snapshot)
                WatchElectionDay(index);

            SelectElection(index, m_Elections.Ledger.Archive.Count);
            m_MapView.Update(false);
            OpenPanel();
        }

        private void WriteElectionNotice(IJsonWriter writer)
        {
            if (m_NoticeSnapshot == null) writer.WriteNull();
            else writer.ElectionNotice(m_NoticeId, m_NoticeIsResults, m_Elections.CityName,
                m_NoticeIsResults ? m_NoticeSnapshot.GoverningParty?.Name : null);
        }

#if DEV_TOOLS
        internal string DevShowElectionNotice(bool results)
        {
            var snapshot = m_Elections.Ledger.Archive.Latest;
            if (snapshot == null) return "Run an election to preview its notifications.";
            if (!ShowElectionNotice(snapshot, results)) return "Election notifications are turned off in Options.";
            return results ? "Showing the results notice." : "Showing the polls-closed notice.";
        }
#endif
    }
}
