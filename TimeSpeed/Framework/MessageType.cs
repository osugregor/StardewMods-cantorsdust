using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TimeSpeed.Framework
{
    internal static class MessageType
    {
        public const string ToggleFreeze = "ToggleFreeze";
        public const string IncreaseTickInterval = "IncreaseTickInterval";
        public const string DecreaseTickInterval = "DecreaseTickInterval";
        public const string QuickNotify = "QuickNotify";
        public const string ShortNotify = "ToggleFShortNotifyreeze";

        public static string TickInterval(bool increase)
        {
            return increase ? IncreaseTickInterval : DecreaseTickInterval;
        }
    }
}
