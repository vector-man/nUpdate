﻿// Author: Dominic Beger (Trade/ProgTrade)

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using MySql.Data.MySqlClient;
using nUpdate.Administration.Core;
using nUpdate.Administration.Core.Application;
using nUpdate.Administration.Core.Ftp;
using nUpdate.Administration.Core.Operations;
using nUpdate.Administration.Core.Operations.Panels;
using nUpdate.Administration.UI.Controls;
using nUpdate.Administration.UI.Popups;
using Newtonsoft.Json.Linq;
using OpenFileDialog = System.Windows.Forms.OpenFileDialog;

namespace nUpdate.Administration.UI.Dialogs
{
    public partial class PackageEditDialog : BaseDialog, IAsyncSupportable, IResettable
    {
        private bool _allowCancel = true;
        private bool _commandsExecuted;
        private bool _configurationUploaded;
        private string _existingVersionString;
        private string _newPackageDirectory;
        private string _oldPackageDirectoryPath;
        private UpdateConfiguration _packageConfiguration;
        private UpdateVersion _newVersion;

        /// <summary>
        ///     The FTP-password. Set as SecureString for deleting it out of the memory after runtime.
        /// </summary>
        public SecureString FtpPassword = new SecureString();

        /// <summary>
        ///     The proxy-password. Set as SecureString for deleting it out of the memory after runtime.
        /// </summary>
        public SecureString ProxyPassword = new SecureString();

        /// <summary>
        ///     The MySQL-password. Set as SecureString for deleting it out of the memory after runtime.
        /// </summary>
        public SecureString SqlPassword = new SecureString();

        private readonly TreeNode _createRegistrySubKeyNode = new TreeNode("Create registry subkey", 14, 14)
        {
            Tag = "CreateRegistrySubKey"
        };

        private readonly List<CultureInfo> _cultures = new List<CultureInfo>();
        private readonly TreeNode _deleteNode = new TreeNode("Delete file", 9, 9) {Tag = "DeleteFile"};

        private readonly TreeNode _deleteRegistrySubKeyNode = new TreeNode("Delete registry subkey", 12, 12)
        {
            Tag = "DeleteRegistrySubKey"
        };

        private readonly TreeNode _deleteRegistryValueNode = new TreeNode("Delete registry value", 12, 12)
        {
            Tag = "DeleteRegistryValue"
        };

        private readonly FtpManager _ftp = new FtpManager();
        private readonly TreeNode _renameNode = new TreeNode("Rename file", 10, 10) {Tag = "RenameFile"};

        private readonly TreeNode _setRegistryValueNode = new TreeNode("Set registry value", 13, 13)
        {
            Tag = "SetRegistryValue"
        };

        private readonly TreeNode _startProcessNode = new TreeNode("Start process", 8, 8) {Tag = "StartProcess"};
        private readonly TreeNode _startServiceNode = new TreeNode("Start service", 5, 5) {Tag = "StartService"};
        private readonly TreeNode _stopServiceNode = new TreeNode("Stop service", 6, 6) {Tag = "StopService"};

        private readonly TreeNode _terminateProcessNode = new TreeNode("Terminate process", 7, 7)
        {Tag = "StopProcess"};

        private readonly BindingList<string> _unsupportedVersionLiteralsBindingList = new BindingList<string>();

        public PackageEditDialog()
        {
            InitializeComponent();
        }

        /// <summary>
        ///     The configurations available in the file.
        /// </summary>
        public List<UpdateConfiguration> UpdateConfiguration { get; set; }

        /// <summary>
        ///     Gets or sets if the package is released.
        /// </summary>
        public bool IsReleased { get; set; }

        /// <summary>
        ///     The version of the package to edit.
        /// </summary>
        public UpdateVersion PackageVersion { get; set; }

        /// <summary>
        ///     The url of the configuration file.
        /// </summary>
        public Uri ConfigurationFileUrl { get; set; }

        /// <summary>
        ///     Enables or disables the UI controls.
        /// </summary>
        /// <param name="enabled">Sets the activation state.</param>
        public void SetUiState(bool enabled)
        {
            if (enabled)
                _allowCancel = true;

            Invoke(new Action(() =>
            {
                foreach (var c in from Control c in Controls where c.Visible select c)
                {
                    c.Enabled = enabled;
                }

                loadingPanel.Visible = !enabled;
            }));
        }

        public void Reset()
        {
            try
            {
                Directory.Move(_newPackageDirectory, _oldPackageDirectoryPath);
            }
            catch (Exception ex)
            {
                Invoke(new Action(() =>
                {
                    Popup.ShowPopup(this, SystemIcons.Error, "Error while renaming the package directory.", ex,
                        PopupButtons.Ok);
                    Close();
                }));
            }

            if (_commandsExecuted)
            {
                Invoke(new Action(() => loadingLabel.Text = "Connecting to SQL-server..."));
                var connectionString = String.Format("SERVER={0};" +
                                                     "DATABASE={1};" +
                                                     "UID={2};" +
                                                     "PASSWORD={3};",
                    Project.SqlWebUrl, Project.SqlDatabaseName,
                    Project.SqlUsername,
                    SqlPassword.ConvertToUnsecureString());

                var myConnection = new MySqlConnection(connectionString);
                try
                {
                    myConnection.Open();
                }
                catch (MySqlException ex)
                {
                    Invoke(
                        new Action(
                            () =>
                                Popup.ShowPopup(this, SystemIcons.Error, "An MySQL-exception occured.",
                                    ex, PopupButtons.Ok)));
                    myConnection.Close();
                    Invoke(new Action(Close));
                    return;
                }
                catch (Exception ex)
                {
                    Invoke(
                        new Action(
                            () =>
                                Popup.ShowPopup(this, SystemIcons.Error, "Error while connecting to the database.",
                                    ex, PopupButtons.Ok)));
                    myConnection.Close();
                    Invoke(new Action(Close));
                    return;
                }

                Invoke(new Action(() => loadingLabel.Text = "Executing SQL-commands..."));

                var command = myConnection.CreateCommand();
                command.CommandText =
                    String.Format("UPDATE Version SET `Version` = \"{0}\" WHERE `ID` = {1};",
                        PackageVersion, _packageConfiguration.VersionId);

                try
                {
                    command.ExecuteNonQuery();
                    _commandsExecuted = false;
                }
                catch (Exception ex)
                {
                    Invoke(
                        new Action(
                            () =>
                                Popup.ShowPopup(this, SystemIcons.Error, "Error while executing the commands.",
                                    ex, PopupButtons.Ok)));
                    Invoke(new Action(Close));
                    return;
                }
                finally
                {
                    myConnection.Close();
                    command.Dispose();
                }
            }

            if (!_configurationUploaded)
                return;

            try
            {
                var configurationFilePath = Path.Combine(_newPackageDirectory, "updates.json");
                File.WriteAllText(configurationFilePath, Serializer.Serialize(UpdateConfiguration));
                _ftp.UploadFile(configurationFilePath);
                _configurationUploaded = false;
            }
            catch (Exception ex)
            {
                Invoke(new Action(() =>
                {
                    Popup.ShowPopup(this, SystemIcons.Error, "Error while undoing the changes.", ex,
                        PopupButtons.Ok);
                    Close();
                }));
            }
        }

        private void PackageEditDialog_Load(object sender, EventArgs e)
        {
            Text = String.Format(Text, PackageVersion.FullText);

            _ftp.Host = Project.FtpHost;
            _ftp.Port = Project.FtpPort;
            _ftp.Username = Project.FtpUsername;
            _ftp.Password = FtpPassword;
            _ftp.Protocol = (FtpSecurityProtocol) Project.FtpProtocol;
            _ftp.UsePassiveMode = Project.FtpUsePassiveMode;
            _ftp.Directory = Project.FtpDirectory;

            if (UpdateConfiguration == null)
            {
                Popup.ShowPopup(this, SystemIcons.Error, "Error while loading the configuration.",
                    "There are no entries available in the configuration.",
                    PopupButtons.Ok);
                Close();
                return;
            }

            try
            {
                _packageConfiguration =
                    UpdateConfiguration.First(item => item.LiteralVersion == PackageVersion.ToString()).DeepCopy();
            }
            catch (InvalidOperationException)
            {
                Popup.ShowPopup(this, SystemIcons.Error, "Error while loading the configuration.",
                    "There are no entries available for the current package in the configuration.",
                    PopupButtons.Ok);
                Close();
                return;
            }

            var packageVersion = new UpdateVersion(_packageConfiguration.LiteralVersion);
            majorNumericUpDown.Maximum = Decimal.MaxValue;
            minorNumericUpDown.Maximum = Decimal.MaxValue;
            buildNumericUpDown.Maximum = Decimal.MaxValue;
            revisionNumericUpDown.Maximum = Decimal.MaxValue;

            majorNumericUpDown.Value = packageVersion.Major;
            minorNumericUpDown.Value = packageVersion.Minor;
            buildNumericUpDown.Value = packageVersion.Build;
            revisionNumericUpDown.Value = packageVersion.Revision;

            _existingVersionString = _packageConfiguration.LiteralVersion;

            var devStages = Enum.GetValues(typeof (DevelopmentalStage));
            Array.Reverse(devStages);
            developmentalStageComboBox.DataSource = devStages;
            developmentalStageComboBox.SelectedIndex =
                developmentalStageComboBox.FindStringExact(packageVersion.DevelopmentalStage.ToString());
            developmentBuildNumericUpDown.Value = (packageVersion.DevelopmentBuild > 0)
                ? packageVersion.DevelopmentBuild
                : 1;
            developmentBuildNumericUpDown.Enabled = (packageVersion.DevelopmentalStage != DevelopmentalStage.Release);
            architectureComboBox.SelectedIndex = (int) _packageConfiguration.Architecture;
            mustUpdateCheckBox.Checked = _packageConfiguration.MustUpdate;
            includeIntoStatisticsCheckBox.Checked = _packageConfiguration.UseStatistics;
            foreach (var package in Project.Packages.Where(package => package.Version == packageVersion))
            {
                descriptionTextBox.Text = package.Description;
            }

            unsupportedVersionsListBox.DataSource = _unsupportedVersionLiteralsBindingList;
            var cultureInfos = CultureInfo.GetCultures(CultureTypes.AllCultures).ToList();
            foreach (var info in cultureInfos)
            {
                changelogLanguageComboBox.Items.Add(String.Format("{0} - {1}", info.EnglishName, info.Name));
                _cultures.Add(info);
            }

            changelogContentTabControl.TabPages[0].Tag = _cultures.Where(x => x.Name == "en");
            changelogLanguageComboBox.SelectedIndex = changelogLanguageComboBox.FindStringExact("English - en");

            foreach (var changelogDictionaryEntry in _packageConfiguration.Changelog)
            {
                var culture = changelogDictionaryEntry.Key;
                if (culture.Name != "en")
                {
                    var page = new TabPage("Changelog")
                    {
                        BackColor = SystemColors.Window,
                        Tag = culture
                    };
                    page.Controls.Add(new ChangelogPanel {Changelog = changelogDictionaryEntry.Value});
                    changelogContentTabControl.TabPages.Add(page);
                }
                else
                {
                    englishChangelogTextBox.Text = changelogDictionaryEntry.Value;
                }
            }

            categoryTreeView.SelectedNode = categoryTreeView.Nodes[0];
            if (_packageConfiguration.UnsupportedVersions != null &&
                _packageConfiguration.UnsupportedVersions.Count() != 0)
            {
                someVersionsRadioButton.Checked = true;
                unsupportedVersionsPanel.Enabled = true;
                foreach (var unsupportedVersionLiteral in _packageConfiguration.UnsupportedVersions)
                {
                    _unsupportedVersionLiteralsBindingList.Add(unsupportedVersionLiteral);
                }
            }
            else
            {
                unsupportedVersionsPanel.Enabled = false;
            }

            foreach (var operation in _packageConfiguration.Operations)
            {
                switch (Operation.GetOperationTag(operation))
                {
                    case "DeleteFile":
                        categoryTreeView.Nodes[3].Nodes.Add((TreeNode) _deleteNode.Clone());

                        var deletePage = new TabPage("Delete file") {BackColor = SystemColors.Window};
                        deletePage.Controls.Add(new FileDeleteOperationPanel
                        {
                            Path = operation.Value,
                            ItemList = new BindingList<string>(((JArray) operation.Value2).ToObject<BindingList<string>>())
                        });
                        categoryTabControl.TabPages.Add(deletePage);
                        break;

                    case "RenameFile":
                        categoryTreeView.Nodes[3].Nodes.Add((TreeNode) _renameNode.Clone());

                        var renamePage = new TabPage("Rename file") {BackColor = SystemColors.Window};
                        renamePage.Controls.Add(new FileRenameOperationPanel
                        {
                            Path = operation.Value,
                            NewName = operation.Value2.ToString()
                        });
                        categoryTabControl.TabPages.Add(renamePage);
                        break;

                    case "CreateRegistrySubKey":
                        categoryTreeView.Nodes[3].Nodes.Add((TreeNode) _createRegistrySubKeyNode.Clone());

                        var createRegistrySubKeyPage = new TabPage("Create registry subkey")
                        {
                            BackColor = SystemColors.Window
                        };
                        createRegistrySubKeyPage.Controls.Add(new RegistrySubKeyCreateOperationPanel
                        {
                            KeyPath = operation.Value,
                            ItemList = new BindingList<string>(((JArray) operation.Value2).ToObject<BindingList<string>>())
                        });
                        categoryTabControl.TabPages.Add(createRegistrySubKeyPage);
                        break;

                    case "DeleteRegistrySubKey":
                        categoryTreeView.Nodes[3].Nodes.Add((TreeNode) _deleteRegistrySubKeyNode.Clone());

                        var deleteRegistrySubKeyPage = new TabPage("Delete registry subkey")
                        {
                            BackColor = SystemColors.Window
                        };
                        deleteRegistrySubKeyPage.Controls.Add(new RegistrySubKeyDeleteOperationPanel
                        {
                            KeyPath = operation.Value,
                            ItemList = new BindingList<string>(((JArray) operation.Value2).ToObject<BindingList<string>>())
                        });
                        categoryTabControl.TabPages.Add(deleteRegistrySubKeyPage);
                        break;

                    case "SetRegistryValue":
                        categoryTreeView.Nodes[3].Nodes.Add((TreeNode) _setRegistryValueNode.Clone());

                        var setRegistryValuePage = new TabPage("Set registry value")
                        {
                            BackColor = SystemColors.Window
                        };
                        setRegistryValuePage.Controls.Add(new RegistrySetValueOperationPanel
                        {
                            KeyPath = operation.Value,
                            NameValuePairs =
                                ((JArray) operation.Value2).ToObject<List<Tuple<string, object, RegistryValueKind>>>()
                        });
                        categoryTabControl.TabPages.Add(setRegistryValuePage);
                        break;

                    case "DeleteRegistryValue":
                        categoryTreeView.Nodes[3].Nodes.Add((TreeNode) _deleteRegistryValueNode.Clone());

                        var deleteRegistryValuePage = new TabPage("Delete registry value")
                        {
                            BackColor = SystemColors.Window
                        };
                        deleteRegistryValuePage.Controls.Add(new RegistryDeleteValueOperationPanel
                        {
                            KeyPath = operation.Value,
                            ItemList = ((JObject) operation.Value2).ToObject<BindingList<string>>()
                        });
                        categoryTabControl.TabPages.Add(deleteRegistryValuePage);
                        break;

                    case "StartProcess":
                        categoryTreeView.Nodes[3].Nodes.Add((TreeNode) _startProcessNode.Clone());

                        var startProcessPage = new TabPage("Start process") {BackColor = SystemColors.Window};
                        startProcessPage.Controls.Add(new ProcessStartOperationPanel
                        {
                            Path = operation.Value,
                            Arguments = ((JArray) operation.Value2).ToObject<BindingList<string>>()
                        });
                        categoryTabControl.TabPages.Add(startProcessPage);
                        break;

                    case "TerminateProcess":
                        categoryTreeView.Nodes[3].Nodes.Add((TreeNode) _terminateProcessNode.Clone());

                        var terminateProcessPage = new TabPage("Terminate process") {BackColor = SystemColors.Window};
                        terminateProcessPage.Controls.Add(new ProcessStopOperationPanel
                        {
                            ProcessName = operation.Value
                        });
                        categoryTabControl.TabPages.Add(terminateProcessPage);
                        break;

                    case "StartService":
                        categoryTreeView.Nodes[3].Nodes.Add((TreeNode) _startServiceNode.Clone());

                        var startServicePage = new TabPage("Start service") {BackColor = SystemColors.Window};
                        startServicePage.Controls.Add(new ServiceStartOperationPanel
                        {
                            ServiceName = operation.Value
                        });
                        categoryTabControl.TabPages.Add(startServicePage);
                        break;

                    case "StopService":
                        categoryTreeView.Nodes[3].Nodes.Add((TreeNode) _stopServiceNode.Clone());

                        var stopServicePage = new TabPage("Stop service") {BackColor = SystemColors.Window};
                        stopServicePage.Controls.Add(new ServiceStopOperationPanel
                        {
                            ServiceName = operation.Value
                        });
                        categoryTabControl.TabPages.Add(stopServicePage);
                        break;
                }
            }
        }

        private void PackageEditDialog_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!_allowCancel)
                e.Cancel = true;
        }

        private void categoryTreeView_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (categoryTreeView.SelectedNode.Parent == null) // Check whether the selected node is an operation or not
            {
                switch (categoryTreeView.SelectedNode.Index)
                {
                    case 0:
                        categoryTabControl.SelectedTab = generalTabPage;
                        break;
                    case 1:
                        categoryTabControl.SelectedTab = changelogTabPage;
                        break;
                    case 2:
                        categoryTabControl.SelectedTab = availabilityTabPage;
                        break;
                    case 3:
                        categoryTabControl.SelectedTab = operationsTabPage;
                        break;
                }
            }
            else
            {
                categoryTabControl.SelectedTab =
                    categoryTabControl.TabPages[4 + categoryTreeView.SelectedNode.Index];
            }
        }

        private void someVersionsRadioButton_CheckedChanged(object sender, EventArgs e)
        {
            unsupportedVersionsPanel.Enabled = true;
        }

        private void allVersionsRadioButton_CheckedChanged(object sender, EventArgs e)
        {
            unsupportedVersionsPanel.Enabled = false;
        }

        private void saveButton_Click(object sender, EventArgs e)
        {
            _newVersion = new UpdateVersion((int) majorNumericUpDown.Value, (int) minorNumericUpDown.Value,
                (int) buildNumericUpDown.Value, (int) revisionNumericUpDown.Value, (DevelopmentalStage)
                    Enum.Parse(typeof (DevelopmentalStage),
                        developmentalStageComboBox.GetItemText(developmentalStageComboBox.SelectedItem)),
                (int) developmentBuildNumericUpDown.Value);
            if (_newVersion.BasicVersion.ToString() == "0.0.0.0")
            {
                Popup.ShowPopup(this, SystemIcons.Error, "Invalid version set.",
                    "Version \"0.0.0.0\" is not a valid version.", PopupButtons.Ok);
                generalPanel.BringToFront();
                categoryTreeView.SelectedNode = categoryTreeView.Nodes[0];
                return;
            }

            if (Project.Packages != null && Project.Packages.Count != 0)
            {
                if (PackageVersion != _newVersion && Project.Packages.Any(item => item.Version == _newVersion))
                {
                    Popup.ShowPopup(this, SystemIcons.Error, "Invalid version set.",
                        String.Format(
                            "Version \"{0}\" is already existing.",
                            _newVersion.FullText), PopupButtons.Ok);
                    generalPanel.BringToFront();
                    categoryTreeView.SelectedNode = categoryTreeView.Nodes[0];
                    return;
                }
            }

            if (String.IsNullOrEmpty(englishChangelogTextBox.Text))
            {
                Popup.ShowPopup(this, SystemIcons.Error, "No changelog set.",
                    "Please specify a changelog for the package. If you have already set a changelog in another language, you still need to specify one for \"English - en\" to support client's that don't use your specified culture on their computer.",
                    PopupButtons.Ok);
                changelogPanel.BringToFront();
                categoryTreeView.SelectedNode = categoryTreeView.Nodes[1];
                return;
            }

            foreach (
                var tabPage in
                    from tabPage in categoryTabControl.TabPages.Cast<TabPage>().Where(item => item.TabIndex > 3)
                    let operationPanel = tabPage.Controls[0] as IOperationPanel
                    where operationPanel != null && !operationPanel.IsValid
                    select tabPage)
            {
                Popup.ShowPopup(this, SystemIcons.Error, "An added operation isn't valid.",
                    "Please make sure to fill out all required fields correctly.",
                    PopupButtons.Ok);
                categoryTreeView.SelectedNode =
                    categoryTreeView.Nodes[3].Nodes.Cast<TreeNode>()
                        .First(item => item.Index == tabPage.TabIndex - 4);
                return;
            }

            var changelog = new Dictionary<CultureInfo, string>
            {
                {new CultureInfo("en"), englishChangelogTextBox.Text}
            };
            foreach (
                var tabPage in
                    changelogContentTabControl.TabPages.Cast<TabPage>().Where(tabPage => tabPage.Text != "English"))
            {
                var panel = (ChangelogPanel) tabPage.Controls[0];
                if (String.IsNullOrEmpty(panel.Changelog)) continue;
                changelog.Add((CultureInfo) tabPage.Tag, panel.Changelog);
            }

            _packageConfiguration.MustUpdate = mustUpdateCheckBox.Checked;
            _packageConfiguration.Architecture = (Architecture) architectureComboBox.SelectedIndex;
            _packageConfiguration.Changelog = changelog;

            if (unsupportedVersionsListBox.Items.Count == 0)
                allVersionsRadioButton.Checked = true;
            else if (unsupportedVersionsListBox.Items.Count > 0 && someVersionsRadioButton.Checked)
            {
                _packageConfiguration.UnsupportedVersions =
                    unsupportedVersionsListBox.Items.Cast<string>().ToArray();
            }

            _packageConfiguration.Operations.Clear();
            foreach (var operationPanel in from TreeNode node in categoryTreeView.Nodes[3].Nodes
                select (IOperationPanel) categoryTabControl.TabPages[4 + node.Index].Controls[0])
            {
                _packageConfiguration.Operations.Add(operationPanel.Operation);
            }

            _packageConfiguration.UseStatistics = includeIntoStatisticsCheckBox.Checked;

            string[] unsupportedVersionLiterals = null;

            if (unsupportedVersionsListBox.Items.Count == 0)
                allVersionsRadioButton.Checked = true;
            else if (unsupportedVersionsListBox.Items.Count > 0 && someVersionsRadioButton.Checked)
            {
                unsupportedVersionLiterals = _unsupportedVersionLiteralsBindingList.ToArray();
            }

            _packageConfiguration.UnsupportedVersions = unsupportedVersionLiterals;
            _packageConfiguration.LiteralVersion = _newVersion.ToString();
            _packageConfiguration.UpdatePackageUri = new Uri(String.Format("{0}/{1}.zip",
                UriConnector.ConnectUri(Project.UpdateUrl, _packageConfiguration.LiteralVersion), Project.Guid));

            _newPackageDirectory = Path.Combine(Program.Path, "Projects", Project.Name,
                _newVersion.ToString());

            if (_existingVersionString != _newVersion.ToString())
            {
                _oldPackageDirectoryPath = Path.Combine(Program.Path, "Projects", Project.Name,
                    _existingVersionString);
                try
                {
                    Directory.Move(_oldPackageDirectoryPath, _newPackageDirectory);
                }
                catch (Exception ex)
                {
                    Popup.ShowPopup(this, SystemIcons.Error,
                        "Error while changing the version of the package directory.", ex,
                        PopupButtons.Ok);
                    return;
                }
            }

            UpdateConfiguration[
                UpdateConfiguration.IndexOf(
                    UpdateConfiguration.First(item => item.LiteralVersion == PackageVersion.ToString()))] =
                _packageConfiguration;
            var configurationFilePath = Path.Combine(_newPackageDirectory, "updates.json");
            try
            {
                File.WriteAllText(configurationFilePath, Serializer.Serialize(UpdateConfiguration));
            }
            catch (Exception ex)
            {
                Popup.ShowPopup(this, SystemIcons.Error, "Error while saving the new configuration.", ex,
                    PopupButtons.Ok);
                return;
            }

            loadingPanel.Location = new Point(180, 91);
            loadingPanel.BringToFront();

            if (IsReleased)
                ThreadPool.QueueUserWorkItem(arg => InitializePackage());
            else
                DialogResult = DialogResult.OK;
        }

        private void InitializePackage()
        {
            SetUiState(false);

            if (Project.UseStatistics)
            {
                Invoke(new Action(() => loadingLabel.Text = "Connecting to SQL-server..."));

                var connectionString = String.Format("SERVER={0};" +
                                                     "DATABASE={1};" +
                                                     "UID={2};" +
                                                     "PASSWORD={3};",
                    Project.SqlWebUrl, Project.SqlDatabaseName,
                    Project.SqlUsername,
                    SqlPassword.ConvertToUnsecureString());

                var myConnection = new MySqlConnection(connectionString);
                try
                {
                    myConnection.Open();
                }
                catch (MySqlException ex)
                {
                    Invoke(
                        new Action(
                            () =>
                                Popup.ShowPopup(this, SystemIcons.Error, "An MySQL-exception occured.",
                                    ex, PopupButtons.Ok)));
                    myConnection.Close();
                    Reset();
                    return;
                }
                catch (Exception ex)
                {
                    Invoke(
                        new Action(
                            () =>
                                Popup.ShowPopup(this, SystemIcons.Error, "Error while connecting to the database.",
                                    ex, PopupButtons.Ok)));
                    myConnection.Close();
                    Reset();
                    return;
                }

                Invoke(new Action(() => loadingLabel.Text = "Executing SQL-commands..."));

                var command = myConnection.CreateCommand();
                command.CommandText =
                    String.Format("UPDATE Version SET `Version` = \"{0}\" WHERE `ID` = {1};",
                        _newVersion, _packageConfiguration.VersionId);

                try
                {
                    command.ExecuteNonQuery();
                    _commandsExecuted = false;
                }
                catch (Exception ex)
                {
                    Invoke(
                        new Action(
                            () =>
                                Popup.ShowPopup(this, SystemIcons.Error, "Error while executing the commands.",
                                    ex, PopupButtons.Ok)));
                    Reset();
                    return;
                }
                finally
                {
                    myConnection.Close();
                    command.Dispose();
                }
            }

            Invoke(new Action(() => loadingLabel.Text = "Uploading new configuration..."));

            try
            {
                _ftp.UploadFile(Path.Combine(_newPackageDirectory, "updates.json"));
                if (_newVersion != new UpdateVersion(_existingVersionString))
                    _ftp.RenameDirectory(_existingVersionString, _packageConfiguration.LiteralVersion);
                _configurationUploaded = true;
            }
            catch (Exception ex)
            {
                Invoke(
                    new Action(
                        () =>
                            Popup.ShowPopup(this, SystemIcons.Error, "Error while uploading the new configuration.",
                                ex, PopupButtons.Ok)));
                Reset();
                return;
            }

            try
            {
                string description = null;
                Invoke(new Action(() => description = descriptionTextBox.Text));
                Project.Packages.First(item => item.Version == new UpdateVersion(_existingVersionString)).
                    Description = description;
                if (_newVersion != new UpdateVersion(_existingVersionString))
                {
                    Project.Packages.First(item => item.Version == new UpdateVersion(_existingVersionString))
                        .LocalPackagePath
                        = String.Format("{0}\\{1}.zip", _newPackageDirectory, Project.Guid);
                    Project.Packages.First(item => item.Version == new UpdateVersion(_existingVersionString)).Version =
                        new UpdateVersion(_packageConfiguration.LiteralVersion);
                }

                UpdateProject.SaveProject(Project.Path, Project);
            }
            catch (Exception ex)
            {
                Invoke(
                    new Action(
                        () =>
                            Popup.ShowPopup(this, SystemIcons.Error, "Error while saving the project.",
                                ex, PopupButtons.Ok)));
                Reset();
                return;
            }

            SetUiState(true);
            DialogResult = DialogResult.OK;
        }

        private void categoryTreeView_DragDrop(object sender, DragEventArgs e)
        {
            var nodeToDropIn = categoryTreeView.GetNodeAt(categoryTreeView.PointToClient(new Point(e.X, e.Y)));
            if (nodeToDropIn == null || nodeToDropIn.Index != 3) // Operations-node
                return;

            var data = e.Data.GetData(typeof (string));
            if (data == null)
                return;

            switch (data.ToString())
            {
                case "DeleteFile":
                    categoryTreeView.Nodes[3].Nodes.Add((TreeNode) _deleteNode.Clone());

                    var deletePage = new TabPage("Delete file") {BackColor = SystemColors.Window};
                    deletePage.Controls.Add(new FileDeleteOperationPanel());
                    categoryTabControl.TabPages.Add(deletePage);
                    break;

                case "RenameFile":
                    categoryTreeView.Nodes[3].Nodes.Add((TreeNode) _renameNode.Clone());

                    var renamePage = new TabPage("Rename file") {BackColor = SystemColors.Window};
                    renamePage.Controls.Add(new FileRenameOperationPanel());
                    categoryTabControl.TabPages.Add(renamePage);
                    break;

                case "CreateRegistrySubKey":
                    categoryTreeView.Nodes[3].Nodes.Add((TreeNode) _createRegistrySubKeyNode.Clone());

                    var createRegistryEntryPage = new TabPage("Create registry entry") {BackColor = SystemColors.Window};
                    createRegistryEntryPage.Controls.Add(new RegistrySubKeyCreateOperationPanel());
                    categoryTabControl.TabPages.Add(createRegistryEntryPage);
                    break;

                case "DeleteRegistrySubKey":
                    categoryTreeView.Nodes[3].Nodes.Add((TreeNode) _deleteRegistrySubKeyNode.Clone());

                    var deleteRegistryEntryPage = new TabPage("Delete registry entry") {BackColor = SystemColors.Window};
                    deleteRegistryEntryPage.Controls.Add(new RegistrySubKeyDeleteOperationPanel());
                    categoryTabControl.TabPages.Add(deleteRegistryEntryPage);
                    break;

                case "SetRegistryValue":
                    categoryTreeView.Nodes[3].Nodes.Add((TreeNode) _setRegistryValueNode.Clone());

                    var setRegistryEntryValuePage = new TabPage("Set registry entry value")
                    {
                        BackColor = SystemColors.Window
                    };
                    setRegistryEntryValuePage.Controls.Add(new RegistrySetValueOperationPanel());
                    categoryTabControl.TabPages.Add(setRegistryEntryValuePage);
                    break;
                case "DeleteRegistryValue":
                    categoryTreeView.Nodes[3].Nodes.Add((TreeNode) _deleteRegistryValueNode.Clone());

                    var deleteRegistryEntryValuePage = new TabPage("Delete registry entry value")
                    {
                        BackColor = SystemColors.Window
                    };
                    deleteRegistryEntryValuePage.Controls.Add(new RegistryDeleteValueOperationPanel());
                    categoryTabControl.TabPages.Add(deleteRegistryEntryValuePage);
                    break;
                case "StartProcess":
                    categoryTreeView.Nodes[3].Nodes.Add((TreeNode) _startProcessNode.Clone());

                    var startProcessPage = new TabPage("Start process") {BackColor = SystemColors.Window};
                    startProcessPage.Controls.Add(new ProcessStartOperationPanel());
                    categoryTabControl.TabPages.Add(startProcessPage);
                    break;
                case "TerminateProcess":
                    categoryTreeView.Nodes[3].Nodes.Add((TreeNode) _terminateProcessNode.Clone());

                    var terminateProcessPage = new TabPage("Terminate process") {BackColor = SystemColors.Window};
                    terminateProcessPage.Controls.Add(new ProcessStopOperationPanel());
                    categoryTabControl.TabPages.Add(terminateProcessPage);
                    break;
                case "StartService":
                    categoryTreeView.Nodes[3].Nodes.Add((TreeNode) _startServiceNode.Clone());

                    var startServicePage = new TabPage("Start service") {BackColor = SystemColors.Window};
                    startServicePage.Controls.Add(new ServiceStartOperationPanel());
                    categoryTabControl.TabPages.Add(startServicePage);
                    break;
                case "StopService":
                    categoryTreeView.Nodes[3].Nodes.Add((TreeNode) _stopServiceNode.Clone());

                    var stopServicePage = new TabPage("Stop service") {BackColor = SystemColors.Window};
                    stopServicePage.Controls.Add(new ServiceStopOperationPanel());
                    categoryTabControl.TabPages.Add(stopServicePage);
                    break;
            }

            categoryTreeView.Nodes[0].Toggle();
        }

        private void categoryTreeView_DragEnter(object sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.Move;
        }

        private void operationsListView_MouseDown(object sender, MouseEventArgs e)
        {
            if (operationsListView.SelectedItems.Count > 0)
                operationsListView.DoDragDrop(operationsListView.SelectedItems[0].Tag, DragDropEffects.Move);
        }

        private void operationsListView_DragOver(object sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.Move;
        }

        private void developmentalStageComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            var developmentalStage = (DevelopmentalStage) Enum.Parse(typeof (DevelopmentalStage),
                developmentalStageComboBox.GetItemText(developmentalStageComboBox.SelectedItem));

            if (developmentalStage == DevelopmentalStage.Alpha || developmentalStage == DevelopmentalStage.Beta)
                developmentBuildNumericUpDown.Enabled = true;
            else
                developmentBuildNumericUpDown.Enabled = false;
        }

        private void changelogLanguageComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (changelogLanguageComboBox.SelectedIndex == changelogLanguageComboBox.FindStringExact("English - en"))
            {
                changelogContentTabControl.SelectTab(changelogContentTabControl.TabPages[0]);
                return;
            }

            if (
                changelogContentTabControl.TabPages.Cast<TabPage>()
                    .Any(item => item.Tag.Equals(_cultures[changelogLanguageComboBox.SelectedIndex])))
            {
                var aimPage = changelogContentTabControl.TabPages.Cast<TabPage>()
                    .First(item => item.Tag.Equals(_cultures[changelogLanguageComboBox.SelectedIndex]));
                changelogContentTabControl.SelectTab(aimPage);
            }
            else
            {
                var page = new TabPage("Changelog")
                {
                    BackColor = SystemColors.Window,
                    Tag = _cultures[changelogLanguageComboBox.SelectedIndex]
                };
                page.Controls.Add(new ChangelogPanel());
                changelogContentTabControl.TabPages.Add(page);
                changelogContentTabControl.SelectTab(page);
            }
        }

        private void changelogLoadButton_Click(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.SupportMultiDottedExtensions = false;
                ofd.Multiselect = false;

                ofd.Filter = "Textdocument (*.txt)|*.txt|RTF-Document (*.rtf)|*.rtf";

                if (ofd.ShowDialog() == DialogResult.OK)
                    englishChangelogTextBox.Text = File.ReadAllText(ofd.FileName, Encoding.Default);
            }
        }

        private void changelogClearButton_Click(object sender, EventArgs e)
        {
            if (changelogLanguageComboBox.SelectedIndex == changelogLanguageComboBox.FindStringExact("English - en"))
            {
                ((TextBox) changelogContentTabControl.SelectedTab.Controls[0]).Clear();
            }
            else
            {
                var currentChangelogPanel = (ChangelogPanel) changelogContentTabControl.SelectedTab.Controls[0];
                ((TextBox) currentChangelogPanel.Controls[0]).Clear();
            }
        }

        private void addVersionButton_Click(object sender, EventArgs e)
        {
            if (
                unsupportedMajorNumericUpDown.Value == 0 && unsupportedMinorNumericUpDown.Value == 0 &&
                unsupportedBuildNumericUpDown.Value == 0 && unsupportedRevisionNumericUpDown.Value == 0)
            {
                Popup.ShowPopup(this, SystemIcons.Warning, "Invalid version.",
                    "You can't add version \"0.0.0.0\" to the unsupported versions. Please specify a minimum version of \"0.1.0.0\"",
                    PopupButtons.Ok);
                return;
            }

            var version = new UpdateVersion((int) unsupportedMajorNumericUpDown.Value,
                (int) unsupportedMinorNumericUpDown.Value, (int) unsupportedBuildNumericUpDown.Value,
                (int) unsupportedRevisionNumericUpDown.Value);
            _unsupportedVersionLiteralsBindingList.Add(version.ToString());
        }

        private void removeVersionButton_Click(object sender, EventArgs e)
        {
            _unsupportedVersionLiteralsBindingList.RemoveAt(unsupportedVersionsListBox.SelectedIndex);
        }

        private void categoryTreeView_KeyDown(object sender, KeyEventArgs e)
        {
            if (categoryTreeView.SelectedNode == null)
                return;

            if (e.Control && e.KeyCode == Keys.Up)
                categoryTreeView.SelectedNode.MoveUp();
            else if (e.Control && e.KeyCode == Keys.Down)
                categoryTreeView.SelectedNode.MoveDown();

            if ((e.KeyCode != Keys.Delete && e.KeyCode != Keys.Back) || categoryTreeView.SelectedNode.Parent == null)
                return;

            categoryTabControl.TabPages.Remove(
                categoryTabControl.TabPages[4 + categoryTreeView.SelectedNode.Index]);
            categoryTreeView.SelectedNode.Remove();
        }

        private void bulletToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var page = changelogContentTabControl.SelectedTab;
            if (page.Text != "English")
            {
                var panel = (ChangelogPanel) page.Controls[0];
                panel.Paste("•");
            }
            else
            {
                englishChangelogTextBox.Paste("•");
            }
        }

        private void insideQuotationMarkToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var page = changelogContentTabControl.SelectedTab;
            if (page.Text != "English")
            {
                var panel = (ChangelogPanel) page.Controls[0];
                panel.Paste("» «");
            }
            else
            {
                englishChangelogTextBox.Paste("» «");
            }
        }

        private void classicQuotationMarkToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var page = changelogContentTabControl.SelectedTab;
            if (page.Text != "English")
            {
                var panel = (ChangelogPanel) page.Controls[0];
                panel.Paste("„ “");
            }
            else
            {
                englishChangelogTextBox.Paste("„  “");
            }
        }

        private void outsideQuotationMarkToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var page = changelogContentTabControl.SelectedTab;
            if (page.Text != "English")
            {
                var panel = (ChangelogPanel) page.Controls[0];
                panel.Paste("« »");
            }
            else
            {
                englishChangelogTextBox.Paste("« »");
            }
        }

        private void apostropheToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var page = changelogContentTabControl.SelectedTab;
            if (page.Text != "English")
            {
                var panel = (ChangelogPanel) page.Controls[0];
                panel.Paste("'");
            }
            else
            {
                englishChangelogTextBox.Paste("'");
            }
        }

        private void copyrightToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var page = changelogContentTabControl.SelectedTab;
            if (page.Text != "English")
            {
                var panel = (ChangelogPanel) page.Controls[0];
                panel.Paste("©");
            }
            else
            {
                englishChangelogTextBox.Paste("©");
            }
        }

        private void allRightsReservedToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var page = changelogContentTabControl.SelectedTab;
            if (page.Text != "English")
            {
                var panel = (ChangelogPanel) page.Controls[0];
                panel.Paste("®");
            }
            else
            {
                englishChangelogTextBox.Paste("®");
            }
        }

        private void soundRecordingCopyrightToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var page = changelogContentTabControl.SelectedTab;
            if (page.Text != "English")
            {
                var panel = (ChangelogPanel) page.Controls[0];
                panel.Paste("℗");
            }
            else
            {
                englishChangelogTextBox.Paste("℗");
            }
        }

        private void unregisteredTrademarkToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var page = changelogContentTabControl.SelectedTab;
            if (page.Text != "English")
            {
                var panel = (ChangelogPanel) page.Controls[0];
                panel.Paste("™");
            }
            else
            {
                englishChangelogTextBox.Paste("™");
            }
        }

        private void serviceMarkToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var page = changelogContentTabControl.SelectedTab;
            if (page.Text != "English")
            {
                var panel = (ChangelogPanel) page.Controls[0];
                panel.Paste("℠");
            }
            else
            {
                englishChangelogTextBox.Paste("℠");
            }
        }

        private void englishChangelogTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control & e.KeyCode == Keys.A)
                englishChangelogTextBox.SelectAll();
            else if (e.Control & e.KeyCode == Keys.Back)
                SendKeys.SendWait("^+{LEFT}{BACKSPACE}");
        }
    }
}