﻿using System;
using System.Collections.Generic;
using System.Text;

namespace Flow.Launcher.Plugin.PluginsManager
{
    internal class Settings
    {
        internal const string ListCommand = "list";

        internal const string InstallCommand = "install";

        internal const string UninstallCommand = "uninstall";

        internal const string UpdateCommand = "update";

        public bool WarnFromUnknownSource { get; set; } = true;
    }
}
