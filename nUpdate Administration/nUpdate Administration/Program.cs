﻿// Author: Dominic Beger (Trade/ProgTrade)
// License: Creative Commons Attribution NoDerivs (CC-ND)
// Created: 01-08-2014 12:11
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Security;
using System.Threading;
using System.Windows.Forms;
using nUpdate.Administration.UI.Dialogs;
using nUpdate.Administration.UI.Popups;

namespace nUpdate.Administration
{
    public static class Program
    {
        /// <summary>
        ///     The root path of nUpdate Administration.
        /// </summary>
        public static string Path =
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "nUpdate Administration");

        /// <summary>
        ///     The path of the config file for all projects.
        /// </summary>
        public static string ProjectsConfigFilePath = System.IO.Path.Combine(Path, "projconf.txt");

        /// <summary>
        ///     The path of the statistic server file.
        /// </summary>
        public static string StatisticServersFilePath = System.IO.Path.Combine(Path, "statservers.txt");

        /// <summary>
        ///     The currently existing projects.
        /// </summary>
        public static Dictionary<string, string> ExisitingProjects = new Dictionary<string, string>();

        /// <summary>
        ///     The path of the languages directory.
        /// </summary>
        public static string LanguagesDirectory { get; set; }

        /// <summary>
        ///     Der Haupteinstiegspunkt für die Anwendung.
        /// </summary>
        [STAThread]
        private static void Main(string[] args)
        {
            bool firstInstance;
            new Mutex(true, "MainForm", out firstInstance);

            if (!firstInstance) return;

            AppDomain currentDomain = AppDomain.CurrentDomain;
            //currentDomain.UnhandledException += UnhandledException;
            //Application.ThreadException += UnhandledThreadException;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var dialog = new MainDialog();
            if (args.Length > 1)
            {
                var file = new FileInfo(args[0]);
                if (file.Exists)
                    dialog.ProjectPath = file.FullName;
            }

            Application.Run(dialog);
        }

        private static void UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Popup.ShowPopup(SystemIcons.Error, "nUpdate has just noticed an unhandled error.",
                ((Exception) e.ExceptionObject), PopupButtons.Ok);
            Application.Exit();
        }

        private static void UnhandledThreadException(object sender, ThreadExceptionEventArgs e)
        {
            Popup.ShowPopup(SystemIcons.Error, "nUpdate has just noticed an unhandled error.",
                    e.Exception, PopupButtons.Ok);
            Application.Exit();
        }
    }
}