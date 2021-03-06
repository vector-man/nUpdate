﻿// Author: Dominic Beger (Trade/ProgTrade)

using nUpdate.Administration.Core.Win32;

namespace nUpdate.Administration.Core
{
    public class ConnectionChecker
    {
        /// <summary>
        ///     Checks if an internet connection is available.
        /// </summary>
        /// <returns>Returns 'true' if an internet connection is available, otherwise false.</returns>
        public static bool IsConnectionAvailable()
        {
            int desc;
            return NativeMethods.InternetGetConnectedState(out desc, 0);
        }
    }
}