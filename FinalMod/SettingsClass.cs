using System;

namespace HKCustomSceneMod
{
    /// <summary>
    /// 只存"玩家是否到访过这个区域"。字段名 = PlayerData 里的 bool 名，
    /// 这样 Get/SetPlayerBoolHook 可以纯反射转发（见 CustomSceneMod）。
    /// </summary>
    public class SettingsClass
    {
        public bool HKCustomSceneMod_VisitedArea = false;
    }
}
