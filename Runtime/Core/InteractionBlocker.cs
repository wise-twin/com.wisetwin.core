using UnityEngine;
using System;

namespace WiseTwin
{
    /// <summary>
    /// Static class to globally block/unblock interactions.
    /// Used by FocusModeController to prevent interactions during focus mode.
    /// </summary>
    public static class InteractionBlocker
    {
        private static int blockCount = 0;

        /// <summary>
        /// Whether interactions are currently blocked.
        /// </summary>
        public static bool IsBlocked => blockCount > 0;

        /// <summary>
        /// Event fired when block state changes.
        /// </summary>
        public static event Action<bool> OnBlockStateChanged;

        /// <summary>
        /// Block interactions. Can be called multiple times (uses reference counting).
        /// </summary>
        public static void Block()
        {
            blockCount++;
            if (blockCount == 1)
            {
                OnBlockStateChanged?.Invoke(true);
            }
        }

        /// <summary>
        /// Unblock interactions. Must be called same number of times as Block().
        /// </summary>
        public static void Unblock()
        {
            if (blockCount > 0)
            {
                blockCount--;
                if (blockCount == 0)
                {
                    OnBlockStateChanged?.Invoke(false);
                }
            }
        }

        /// <summary>
        /// Force reset block count to 0 (use with caution).
        /// </summary>
        public static void Reset()
        {
            if (blockCount > 0)
            {
                blockCount = 0;
                OnBlockStateChanged?.Invoke(false);
            }
        }
    }
}
