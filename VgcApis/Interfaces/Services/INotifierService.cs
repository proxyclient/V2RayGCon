﻿using System;

namespace VgcApis.Interfaces.Services
{
    public interface INotifierService
    {
        string RegisterHotKey(Action hotKeyHandler,
            string keyName, bool hasAlt, bool hasCtrl, bool hasShift);

        bool UnregisterHotKey(string hotKeyHandle);

        void RefreshNotifyIconLater();

        void RunInUiThreadIgnoreError(Action updater);

    }
}
