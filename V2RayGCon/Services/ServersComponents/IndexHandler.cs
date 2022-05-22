﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace V2RayGCon.Services.ServersComponents
{
    internal sealed class IndexHandler
    {
        ReaderWriterLockSlim locker;
        List<Controllers.CoreServerCtrl> coreServList;

        public IndexHandler(
            ReaderWriterLockSlim locker,
            List<Controllers.CoreServerCtrl> coreServList)
        {
            this.locker = locker;
            this.coreServList = coreServList;
        }

        #region properties

        #endregion

        #region public methods
        public void SortCoreServCtrlListBySpeedTestResult(
            ref List<VgcApis.Interfaces.ICoreServCtrl> coreList)
        {
            SortServerItemList(ref coreList, SpeedTestComparer);
        }

        public void ReverseCoreservCtrlListByIndex(
            ref List<VgcApis.Interfaces.ICoreServCtrl> coreList)
        {
            SortServerItemList(ref coreList, ReverseIndexComparer);
        }

        public void SortCoreServerCtrlListByDownloadTotal(
            ref List<VgcApis.Interfaces.ICoreServCtrl> coreList)
        {
            SortServerItemList(ref coreList, DownloadTotalDecComparer);
        }

        public void SortCoreServerCtrlListByUploadTotal(
            ref List<VgcApis.Interfaces.ICoreServCtrl> coreList)
        {
            SortServerItemList(ref coreList, UploadTotalDecComparer);
        }

        public void SortCoreServerCtrlListByLastModifyDate(
            ref List<VgcApis.Interfaces.ICoreServCtrl> coreList)
        {
            SortServerItemList(ref coreList, UtcTicksDecComparer);
        }

        public void SortCoreServCtrlListBySummary(
            ref List<VgcApis.Interfaces.ICoreServCtrl> coreList)
        {
            SortServerItemList(ref coreList, SummaryComparer);
        }


        public void ResetIndex()
        {
            var pkgs = new List<Tuple<double, VgcApis.Interfaces.CoreCtrlComponents.ICoreStates>>();

            locker.EnterReadLock();
            try
            {
                List<VgcApis.Interfaces.CoreCtrlComponents.ICoreStates> coreStates = coreServList
                   .OrderBy(c => c.GetCoreStates().GetIndex())
                   .Select(c => c.GetCoreStates())
                   .ToList();
                double idx = 0;
                foreach (var coreState in coreStates)
                {
                    var pkg = new Tuple<double, VgcApis.Interfaces.CoreCtrlComponents.ICoreStates>(++idx, coreState);
                    pkgs.Add(pkg);
                }
            }
            finally
            {
                locker.ExitReadLock();
            }

            VgcApis.Misc.Utils.RunInBackground(() =>
            {
                foreach (var pkg in pkgs)
                {
                    var coreState = pkg.Item2;
                    var idx = pkg.Item1;
                    coreState.SetIndex(idx);
                }
            });
        }

        public void ResetIndexQuiet()
        {
            List<Controllers.CoreServerCtrl> sortedServers = new List<Controllers.CoreServerCtrl>();
            locker.EnterReadLock();
            try
            {
                sortedServers = coreServList
                    .OrderBy(c => c.GetCoreStates().GetIndex())
                    .ToList();
            }
            finally
            {
                locker.ExitReadLock();
            }

            for (int i = 0; i < sortedServers.Count(); i++)
            {
                var index = i + 1.0; // closure
                sortedServers[i]
                    .GetCoreStates()
                    .SetIndexQuiet(index);
            }
        }
        #endregion

        #region private methods
        int ReverseIndexComparer(
           VgcApis.Interfaces.ICoreServCtrl a,
           VgcApis.Interfaces.ICoreServCtrl b)
        {
            var idxA = a.GetCoreStates().GetIndex();
            var idxB = b.GetCoreStates().GetIndex();
            return idxB.CompareTo(idxA);
        }

        int SpeedTestComparer(
            VgcApis.Interfaces.ICoreServCtrl a,
            VgcApis.Interfaces.ICoreServCtrl b)
        {
            var spa = a.GetCoreStates().GetSpeedTestResult();
            var spb = b.GetCoreStates().GetSpeedTestResult();
            return spa.CompareTo(spb);
        }

        int DownloadTotalDecComparer(
           VgcApis.Interfaces.ICoreServCtrl a,
           VgcApis.Interfaces.ICoreServCtrl b)
        {
            var ticksA = a.GetCoreStates().GetDownlinkTotalInBytes();
            var ticksB = b.GetCoreStates().GetDownlinkTotalInBytes();
            return ticksB.CompareTo(ticksA);
        }

        int UploadTotalDecComparer(
           VgcApis.Interfaces.ICoreServCtrl a,
           VgcApis.Interfaces.ICoreServCtrl b)
        {
            var ticksA = a.GetCoreStates().GetUplinkTotalInBytes();
            var ticksB = b.GetCoreStates().GetUplinkTotalInBytes();
            return ticksB.CompareTo(ticksA);
        }

        int UtcTicksDecComparer(
           VgcApis.Interfaces.ICoreServCtrl a,
           VgcApis.Interfaces.ICoreServCtrl b)
        {
            var ticksA = a.GetCoreStates().GetLastModifiedUtcTicks();
            var ticksB = b.GetCoreStates().GetLastModifiedUtcTicks();
            return ticksB.CompareTo(ticksA);
        }

        int SummaryComparer(
            VgcApis.Interfaces.ICoreServCtrl a,
            VgcApis.Interfaces.ICoreServCtrl b)
        {
            var sma = a.GetCoreStates().GetSummary();
            var smb = b.GetCoreStates().GetSummary();

            var rsma = VgcApis.Misc.Utils.ReverseSummary(sma);
            var rsmb = VgcApis.Misc.Utils.ReverseSummary(smb);

            return rsma.CompareTo(rsmb);
        }

        void SortServerItemList(
             ref List<VgcApis.Interfaces.ICoreServCtrl> selectedServers,
             Comparison<VgcApis.Interfaces.ICoreServCtrl> comparer)
        {
            if (selectedServers == null || selectedServers.Count() < 2)
            {
                return;
            }
            locker.EnterWriteLock();
            try
            {
                selectedServers.Sort(comparer);
                var minIndex = selectedServers.Select(s => s.GetCoreStates().GetIndex()).Min();
                var delta = 1.0 / 2 / selectedServers.Count;
                for (int i = 0; i < selectedServers.Count; i++)
                {
                    selectedServers[i].GetCoreStates()
                        .SetIndexQuiet(minIndex + delta * (i + 1));
                }
            }
            finally
            {
                locker.ExitWriteLock();
            }
            ResetIndexQuiet();
        }
        #endregion

        #region protected methods

        #endregion
    }
}
