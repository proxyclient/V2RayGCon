﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using V2RayGCon.Resources.Resx;

namespace V2RayGCon.Libs.V2Ray
{
    public class Core
    {
        readonly Encoding ioEncoding = Encoding.UTF8;

        public event EventHandler<VgcApis.Models.Datas.StrEvent> OnLog;
        public event EventHandler OnCoreStatusChanged;

        Services.Settings setting;

        AutoResetEvent coreStartStopLocker = new AutoResetEvent(true);
        Process v2rayCore;
        static int curConcurrentV2RayCoreNum = 0;
        bool isForcedExit = false;

        public Core(Services.Settings setting)
        {
            isReady = false;
            v2rayCore = null;
            this.setting = setting;
        }

        #region property
        string _v2ctl = "";
        string v2ctl
        {
            get
            {
                if (string.IsNullOrEmpty(_v2ctl))
                {
                    _v2ctl = GetExecutablePath(VgcApis.Models.Consts.Core.V2RayCtlExeFileName);
                }
                return _v2ctl;
            }
        }

        string _title;
        public string title
        {
            get
            {
                return string.IsNullOrEmpty(_title) ?
                    string.Empty :
                    VgcApis.Misc.Utils.AutoEllipsis(_title, VgcApis.Models.Consts.AutoEllipsis.V2rayCoreTitleMaxLength);
            }
            set
            {
                _title = value;
            }
        }

        public bool isReady
        {
            get;
            private set;
        }

        public bool isRunning
        {
            get => IsProcRunning(v2rayCore);
        }

        #endregion

        #region public method

        public VgcApis.Models.Datas.StatsSample QueryStatsApi(int port)
        {
            if (setting.IsClosing() || string.IsNullOrEmpty(v2ctl))
            {
                return null;
            }

            var queryParam = string.Format(VgcApis.Models.Consts.Core.StatsQueryParamTpl, port.ToString());
            try
            {
                var output = Misc.Utils.GetOutputFromExecutable(
                    v2ctl,
                    queryParam,
                    VgcApis.Models.Consts.Core.GetStatisticsTimeout);

                return Misc.Utils.ParseStatApiResult(output);
            }
            catch { }
            return null;
        }

        public string GetCoreVersion()
        {
            if (!IsExecutableExist())
            {
                return string.Empty;
            }

            var exe = GetExecutablePath(VgcApis.Models.Consts.Core.XrayCoreExeFileName);
            if (string.IsNullOrEmpty(exe))
            {
                exe = GetExecutablePath(VgcApis.Models.Consts.Core.V2RayCoreExeFileName);
            }

            var timeout = VgcApis.Models.Consts.Core.GetVersionTimeout;
            var output = Misc.Utils.GetOutputFromExecutable(
                exe,
                "-version",
                timeout);

            // since 3.46.* v is deleted
            // Regex pattern = new Regex(@"(?<version>(\d+\.)+\d+)");
            // Regex pattern = new Regex(@"v(?<version>[\d\.]+)");
            var ver = VgcApis.Misc.Utils.ExtractStringWithPattern("version", @"(\d+\.)+\d+", output);
            return ver;
        }

        public bool IsExecutableExist()
        {
            var cores = new string[]{
                VgcApis.Models.Consts.Core.XrayCoreExeFileName,
                VgcApis.Models.Consts.Core.V2RayCoreExeFileName
            };

            foreach (var core in cores)
            {
                if (!string.IsNullOrEmpty(GetExecutablePath(core)))
                {
                    return true;
                }
            }
            return false;
        }

        public string GetExecutablePath(string fileName)
        {
            List<string> folders = GenV2RayCoreSearchPaths(setting.isPortable);
            for (var i = 0; i < folders.Count; i++)
            {
                var p = Path.Combine(folders[i], fileName);
                if (File.Exists(p))
                {
                    return p;
                }
            }
            return string.Empty;
        }

        // blocking
        public void RestartCore(string config, Dictionary<string, string> env = null) =>
            RestartCoreWorker(config, env, false);

        public void RestartCoreIgnoreError(string config) =>
            RestartCoreWorker(config, null, true);

        // blocking
        public void StopCore()
        {
            coreStartStopLocker.WaitOne();
            StopCoreIgnoreError(v2rayCore);
            coreStartStopLocker.Set();
        }

        #endregion

        #region private method
        void RestartCoreWorker(string config, Dictionary<string, string> env, bool quiet)
        {
            if (!IsExecutableExist())
            {
                if (quiet)
                {
                    SendLog(I18N.ExeNotFound);
                }
                else
                {
                    VgcApis.Misc.UI.MsgBoxAsync(I18N.ExeNotFound);
                }
                InvokeEventOnCoreStatusChanged();
                return;
            }

            coreStartStopLocker.WaitOne();
            StopCoreIgnoreError(this.v2rayCore);

            try
            {
                if (!setting.IsClosing())
                {
                    StartCore(config, env, quiet);
                }
            }
            catch
            {
                StopCoreIgnoreError(this.v2rayCore);
            }
            finally
            {
                coreStartStopLocker.Set();
            }

            // do not run in background
            InvokeEventOnCoreStatusChanged();
        }

        void StopCoreIgnoreError(Process core)
        {
            this.v2rayCore = null;

            if (!IsProcRunning(core))
            {
                return;
            }

            try
            {
                isForcedExit = true;
                core?.Kill();
                // VgcApis.Misc.Utils.KillProcessAndChildrens(core.Id);
                // core.WaitForExit(VgcApis.Models.Consts.Core.KillCoreTimeout);
                core?.WaitForExit();
            }
            catch { }
            VgcApis.Misc.Utils.Sleep(500);
        }

        bool IsProcRunning(Process proc)
        {
            try
            {
                if (proc != null && !proc.HasExited)
                {
                    return true;
                }
            }
            catch { }
            return false;
        }
        static List<string> GenV2RayCoreSearchPaths(bool isPortable)
        {
            var folders = new List<string>{
                Misc.Utils.GetSysAppDataFolder(), // %appdata%
                VgcApis.Misc.Utils.GetAppDir(),
                VgcApis.Misc.Utils.GetCoreFolderFullPath(),
            };

            if (isPortable)
            {
                folders.Reverse();
            }

            return folders;
        }


        void InvokeEventOnCoreStatusChanged()
        {
            try
            {
                OnCoreStatusChanged?.Invoke(this, EventArgs.Empty);
            }
            catch { }
        }

        Process CreateV2RayCoreProcess(string config)
        {
            var exe = GetExecutablePath(VgcApis.Models.Consts.Core.XrayCoreExeFileName);
            // var args = string.Empty;
            var args = Misc.Utils.GenCmdArgFromConfig(config);
            if (string.IsNullOrEmpty(exe))
            {
                exe = GetExecutablePath(VgcApis.Models.Consts.Core.V2RayCoreExeFileName);
            }

            var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,

                    // 定时炸弹
                    StandardOutputEncoding = ioEncoding,
                    StandardErrorEncoding = ioEncoding,
                }
            };
            p.EnableRaisingEvents = true;
            return p;
        }

        string TranslateErrorCode(int exitCode)
        {

            if (exitCode == 0)
            {
                return null;
            }

            // exitCode = 1 means Killed forcibly.
            // src https://stackoverflow.com/questions/4344923/process-exit-code-when-process-is-killed-forcibly

            // ctrl + c not working
            if (isForcedExit)
            {
                isForcedExit = false;
                return null;
            }

            /*
             * v2ray-core/main/main.go
             * 23: Configuration error.
             * -1: Failed to start.
             */
            string msg = string.Format(I18N.V2rayCoreExitAbnormally, title, exitCode);
            switch (exitCode)
            {
                case 1:
                    msg = title + @" " + I18N.KilledByUserOrOtherApp;
                    break;
                case 23:
                    msg = title + @" " + I18N.HasFaultyConfig;
                    break;
                case -1:
                    msg = title + @" " + I18N.CanNotStartPlsCheckLogs;
                    break;
                default:
                    break;
            }

            return msg;
        }

        void OnCoreExitedQuiet(object sender, EventArgs args) =>
            OnCoreExitedHandler(sender, true);

        void OnCoreExited(object sender, EventArgs args) =>
            OnCoreExitedHandler(sender, false);

        void OnCoreExitedHandler(object sender, bool quiet)
        {
            isReady = false;

            var core = sender as Process;

            Interlocked.Decrement(ref curConcurrentV2RayCoreNum);

            try
            {
                if (quiet)
                {
                    core.Exited -= OnCoreExitedQuiet;
                }
                else
                {
                    core.Exited -= OnCoreExited;
                }
            }
            catch { }

            string msg = null;
            try
            {
                // Process.ExitCode may throw exceptions
                msg = TranslateErrorCode(core.ExitCode);

                // Close() could invoke CoreExit event
                core.Close();
            }
            catch { }

            SendLog($"{I18N.ConcurrentV2RayCoreNum}{curConcurrentV2RayCoreNum}");
            SendLog(I18N.CoreExit);

            // do not run in background
            // VgcApis.Misc.Utils.RunInBackground(() => InvokeEventOnCoreStatusChanged());
            InvokeEventOnCoreStatusChanged();

            if (!quiet && !string.IsNullOrEmpty(msg))
            {
                VgcApis.Misc.UI.MsgBoxAsync(msg);
            }
        }

        void BindEvents(Process proc, bool quiet)
        {
            try
            {
                if (quiet)
                {
                    proc.Exited += OnCoreExitedQuiet;
                }
                else
                {
                    proc.Exited += OnCoreExited;
                }

                proc.ErrorDataReceived += SendLogHandler;
                proc.OutputDataReceived += SendLogHandler;
            }
            catch { }
        }

        void StartCore(string config, Dictionary<string, string> envs, bool quiet)
        {
            isReady = false;
            var core = CreateV2RayCoreProcess(config);
            VgcApis.Misc.Utils.SetProcessEnvs(core, envs);

            BindEvents(core, quiet);
            Interlocked.Increment(ref curConcurrentV2RayCoreNum);

            core.Start();
            this.v2rayCore = core;

            // Add to JOB object require win8+.
            VgcApis.Libs.Sys.ChildProcessTracker.AddProcess(core);

            WriteConfigToStandardInput(core, config);

            core.PriorityClass = ProcessPriorityClass.AboveNormal;
            core.BeginErrorReadLine();
            core.BeginOutputReadLine();

            SendLog($"{I18N.ConcurrentV2RayCoreNum}{curConcurrentV2RayCoreNum}");
        }

        private void WriteConfigToStandardInput(Process core, string config)
        {
            var input = core.StandardInput;
            var buff = ioEncoding.GetBytes(config);
            input.BaseStream.Write(buff, 0, buff.Length);
            input.WriteLine();
            input.Close();
        }

        void SendLogHandler(object sender, DataReceivedEventArgs args)
        {
            var msg = args.Data;

            if (string.IsNullOrEmpty(msg))
            {
                return;
            }

            if (!isReady && MatchAllReadyMarks(msg))
            {
                isReady = true;
            }

            SendLog(msg);
        }

        bool MatchAllReadyMarks(string message)
        {
            var lower = message.ToLower();
            foreach (var mark in VgcApis.Models.Consts.Core.ReadyLogMarks)
            {
                if (!lower.Contains(mark))
                {
                    return false;
                }
            }
            return true;
        }

        void SendLog(string log)
        {
            var arg = new VgcApis.Models.Datas.StrEvent(log);
            try
            {
                OnLog?.Invoke(this, arg);
            }
            catch { }
        }

        #endregion
    }
}
