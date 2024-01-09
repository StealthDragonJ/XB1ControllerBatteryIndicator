﻿using System.Linq;
using System.Threading;
using SharpDX.XInput;
using Windows.UI.Notifications;
using Windows.Data.Xml.Dom;
using System.Collections.Generic;
using System;
using System.Management;
using System.Collections.ObjectModel;
using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Media;
using XB1ControllerBatteryIndicator.ShellHelpers;
using MS.WindowsAPICodePack.Internal;
using Microsoft.WindowsAPICodePack.Shell.PropertySystem;
using XB1ControllerBatteryIndicator.Localization;
using XB1ControllerBatteryIndicator.Properties;
using System.Security.Principal;
using Microsoft.Win32;

namespace XB1ControllerBatteryIndicator
{
    public class SystemTrayViewModel : Caliburn.Micro.Screen
    {
        private string _activeIcon;
        private Controller _controller;
        private string _tooltipText;
        private const string APP_ID = "NiyaShy.XB1ControllerBatteryIndicator";
        //private bool[] toast_shown = new bool[5];
        private Dictionary<string, int> numdict = new Dictionary<string, int>();
        private const string ThemeRegKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string ThemeRegValueName = "SystemUsesLightTheme";

        private SoundPlayer _soundPlayer;

        private Dictionary<BatteryLevel, LowBatteryLevelData> lowBatteryLevelsData = new Dictionary<BatteryLevel, LowBatteryLevelData>()
        {
            { BatteryLevel.Empty, new LowBatteryLevelData("ControllerToast", Strings.Toast_Title, Strings.Toast_Text) },
            { BatteryLevel.Low, new LowBatteryLevelData("ControllerToast", Strings.Toast_LowBattery_Title, Strings.Toast_LowBattery_Text) }
        };

        // TODO** change to array instead since index is simply an int anyway
        // keeps track of the battery level for the currently shown notification
        private Dictionary<int, BatteryNotificationData> toast_shown = new Dictionary<int, BatteryNotificationData>()
        {
            { 0, new BatteryNotificationData() },
            { 1, new BatteryNotificationData() },
            { 2, new BatteryNotificationData() },
            { 3, new BatteryNotificationData() },
            { 4, new BatteryNotificationData() }
        };

        private class LowBatteryLevelData
        {
            public string group { get; }

            public string toastTitle { get; }
            public string toastText { get; }

            public LowBatteryLevelData(string group, string toastTitle, string toastText)
            {
                this.group = group;
                this.toastTitle = toastTitle;
                this.toastText = toastText;
            }
        }

        private class BatteryNotificationData
        {
            public bool enabled;

            public BatteryLevel batteryLevel;

            public BatteryNotificationData() : this(false, BatteryLevel.Full)
            {
                
            }

            public BatteryNotificationData(bool enabled, BatteryLevel batteryLevel)
            {
                this.enabled = enabled;
                this.batteryLevel = batteryLevel;
            }

            // returns true if showing a notificaiton for the given battery level
            public bool showingNotification(BatteryLevel batteryLevel)
            {
                return (this.batteryLevel == batteryLevel) && this.enabled;
            }

            public void Enable(BatteryLevel batteryLevel)
            {
                this.batteryLevel = batteryLevel;

                enabled = true;
            }
        }

        public SystemTrayViewModel()
        {
            GetAvailableLanguages();
            TranslationManager.CurrentLanguageChangedEvent += (sender, args) => GetAvailableLanguages();
            UpdateNotificationSound();

            ActiveIcon = $"Resources/battery_unknown{LightTheme()}.ico";
            numdict["One"] = 1;
            numdict["Two"] = 2;
            numdict["Three"] = 3;
            numdict["Four"] = 4;
            TryCreateShortcut();
            Thread th = new Thread(RefreshControllerState);
            th.IsBackground = true;
            th.Start();
        }

        BatteryLevel testBatteryLevel = BatteryLevel.Full + 1;

        private BatteryLevel ControllerTester(BatteryLevel currentLevel)
        {
            int seconds = 5;
            int delay = seconds * 1000;

            if (currentLevel > 0)
            {
                currentLevel -= 1;

                Thread.Sleep(delay);
            }

            return currentLevel;
        }

        public string ActiveIcon
        {
            get { return _activeIcon; }
            set { Set(ref _activeIcon, value); }
        }

        public string TooltipText
        {
            get { return _tooltipText; }
            set { Set(ref _tooltipText, value); }
        }

        public ObservableCollection<CultureInfo> AvailableLanguages { get; } = new ObservableCollection<CultureInfo>();

        private void RefreshControllerState()
        {
            bool lowBatteryWarningSoundPlayed = false;

            while(true)
            {
                try
                {
                    //Initialize controllers
                    var controllers = new[]
                    {
                    new Controller(UserIndex.One), new Controller(UserIndex.Two), new Controller(UserIndex.Three),
                    new Controller(UserIndex.Four)
                    };
                    //Check if at least one is present
                    _controller = controllers.FirstOrDefault(selectControler => selectControler.IsConnected);

                    if (_controller != null)
                    {
                        //cycle through all recognized controllers
                        foreach (var currentController in controllers)
                        {
                            var controllerIndexCaption = GetControllerIndexCaption(currentController.UserIndex);
                            if (currentController.IsConnected)
                            {
                                var batteryInfo = currentController.GetBatteryInformation(BatteryDeviceType.Gamepad);

                                // test code
                                //batteryInfo.BatteryLevel = BatteryLevel.Low;

                                testBatteryLevel = ControllerTester(testBatteryLevel);
                                batteryInfo.BatteryLevel = testBatteryLevel;

                                //check if toast was already triggered and battery is no longer empty...
                                //if (batteryInfo.BatteryLevel != BatteryLevel.Empty && batteryInfo.BatteryLevel != BatteryLevel.Low)
                                if (!lowBatteryLevelsData.ContainsKey(batteryInfo.BatteryLevel))
                                {
                                    //if (toast_shown[numdict[$"{currentController.UserIndex}"]] == true)
                                    if (toast_shown[numdict[$"{currentController.UserIndex}"]].enabled == true)
                                    {
                                        //...reset the notification
                                        //toast_shown[numdict[$"{currentController.UserIndex}"]] = false;
                                        toast_shown[numdict[$"{currentController.UserIndex}"]].enabled = false;

                                        //ToastNotificationManager.History.Remove($"Controller{currentController.UserIndex}", "ControllerToast", APP_ID);
                                        ToastNotificationManager.History.Remove($"Controller{currentController.UserIndex}", lowBatteryLevelsData[BatteryLevel.Empty].group, APP_ID);
                                        ToastNotificationManager.History.Remove($"Controller{currentController.UserIndex}", lowBatteryLevelsData[BatteryLevel.Low].group, APP_ID);
                                    }
                                }
                                //wired
                                if (batteryInfo.BatteryType == BatteryType.Wired)
                                {
                                    TooltipText = string.Format(Strings.ToolTip_Wired, controllerIndexCaption);
                                    ActiveIcon = $"Resources/battery_wired_{currentController.UserIndex.ToString().ToLower() + LightTheme()}.ico";
                                }
                                //"disconnected", a controller that was detected but hasn't sent battery data yet has this state
                                else if (batteryInfo.BatteryType == BatteryType.Disconnected)
                                {
                                    TooltipText = string.Format(Strings.ToolTip_WaitingForData, controllerIndexCaption);
                                    ActiveIcon = $"Resources/battery_disconnected_{currentController.UserIndex.ToString().ToLower() + LightTheme()}.ico";
                                }
                                //this state should never happen
                                else if (batteryInfo.BatteryType == BatteryType.Unknown)
                                {
                                    TooltipText = string.Format(Strings.ToolTip_Unknown, controllerIndexCaption);
                                    ActiveIcon = $"Resources/battery_disconnected_{currentController.UserIndex.ToString().ToLower() + LightTheme()}.ico";
                                }
                                //a battery level was detected
                                else
                                {
                                    var batteryLevelCaption = GetBatteryLevelCaption(batteryInfo.BatteryLevel);
                                    TooltipText = string.Format(Strings.ToolTip_Wireless, controllerIndexCaption, batteryLevelCaption);
                                    ActiveIcon = $"Resources/battery_{batteryInfo.BatteryLevel.ToString().ToLower()}_{currentController.UserIndex.ToString().ToLower() + LightTheme()}.ico";
                                    //when "empty" state is detected...
                                    //if (batteryInfo.BatteryLevel == BatteryLevel.Empty || batteryInfo.BatteryLevel == BatteryLevel.Low)
                                    if (lowBatteryLevelsData.ContainsKey(batteryInfo.BatteryLevel))
                                    {
                                        //check if toast (notification) for current controller was already triggered
                                        //if (toast_shown[numdict[$"{currentController.UserIndex}"]] == false)
                                        // here we also make sure the battery level hasn't changed even if the older toast is still there
                                        if (toast_shown[numdict[$"{currentController.UserIndex}"]].enabled == false || toast_shown[numdict[$"{currentController.UserIndex}"]].batteryLevel != batteryInfo.BatteryLevel)
                                        {
                                            //if not, trigger it
                                            //toast_shown[numdict[$"{currentController.UserIndex}"]].enabled = true;
                                            toast_shown[numdict[$"{currentController.UserIndex}"]].Enable(batteryInfo.BatteryLevel);

                                            ShowToast(currentController.UserIndex, lowBatteryLevelsData[batteryInfo.BatteryLevel]);
                                        }
                                        //check if notification sound is enabled
                                        if (Settings.Default.LowBatteryWarningSound_Enabled)
                                        {
                                            if (Settings.Default.LowBatteryWarningSound_Loop_Enabled || !lowBatteryWarningSoundPlayed)
                                            {
                                                //Necessary to avoid crashing if the .wav file is missing
                                                try
                                                {
                                                    _soundPlayer?.Play();
                                                }
                                                catch (Exception ex)
                                                {
                                                    Debug.WriteLine(ex);
                                                }
                                                lowBatteryWarningSoundPlayed = true;
                                            }
                                        }
                                    }

                                    //last_battery_level[numdict[$"{currentController.UserIndex}"]] = batteryInfo.BatteryLevel;
                                }
                                Thread.Sleep(5000);
                            }
                        }
                    }
                    else
                    {
                        TooltipText = Strings.ToolTip_NoController;
                        ActiveIcon = $"Resources/battery_unknown{LightTheme()}.ico";
                    }
                    Thread.Sleep(1000);
                }
                catch (Exception)
                {
                }
            }
        }

        //try to create a start menu shortcut (required for sending toasts)
        private bool TryCreateShortcut()
        {
            String shortcutPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\Microsoft\\Windows\\Start Menu\\Programs\\XB1ControllerBatteryIndicator.lnk";
            if (!File.Exists(shortcutPath))
            {
                InstallShortcut(shortcutPath);
                return true;
            }
            return false;
        }
        //create the shortcut
        private void InstallShortcut(String shortcutPath)
        {
            // Find the path to the current executable 
            String exePath = Process.GetCurrentProcess().MainModule.FileName;
            IShellLinkW newShortcut = (IShellLinkW)new CShellLink();

            // Create a shortcut to the exe 
            ErrorHelper.VerifySucceeded(newShortcut.SetPath(exePath));
            ErrorHelper.VerifySucceeded(newShortcut.SetArguments(""));

            // Open the shortcut property store, set the AppUserModelId property 
            IPropertyStore newShortcutProperties = (IPropertyStore)newShortcut;

            using (PropVariant appId = new PropVariant(APP_ID))
            {
                ErrorHelper.VerifySucceeded(newShortcutProperties.SetValue(SystemProperties.System.AppUserModel.ID, appId));
                ErrorHelper.VerifySucceeded(newShortcutProperties.Commit());
            }

            // Commit the shortcut to disk 
            IPersistFile newShortcutSave = (IPersistFile)newShortcut;

            ErrorHelper.VerifySucceeded(newShortcutSave.Save(shortcutPath, true));
        }

        // wrapper method for the below
        private void ShowToast(UserIndex controllerIndex, LowBatteryLevelData lowBatteryLevelData)
        {
            ShowToast(controllerIndex, lowBatteryLevelData.toastTitle, lowBatteryLevelData.toastText, lowBatteryLevelData.group);
        }

        //send a toast
        // modifed to allow any title and text
        private void ShowToast(UserIndex controllerIndex, string title, string text, string group)
        {
            int controllerId = numdict[$"{controllerIndex}"];
            var controllerIndexCaption = GetControllerIndexCaption(controllerIndex);
            string argsDismiss = $"dismissed";
            string argsLaunch = $"{controllerId}";
            //how the content gets arranged
            string toastVisual =
                $@"<visual>
                        <binding template='ToastGeneric'>
                            <text>{string.Format(title, controllerIndexCaption)}</text>
                            <text>{string.Format(text, controllerIndexCaption)}</text>
                            <text>{Strings.Toast_Text2}</text>
                        </binding>
                    </visual>";
            //Button on the toast
            string toastActions =
                $@"<actions>
                        <action content='{Strings.Toast_Dismiss}' arguments='{argsDismiss}'/>
                   </actions>";
            //combine content and button
            string toastXmlString =
                $@"<toast scenario='reminder' launch='{argsLaunch}'>
                        {toastVisual}
                        {toastActions}
                   </toast>";

            XmlDocument toastXml = new XmlDocument();
            toastXml.LoadXml(toastXmlString);
            //create the toast
            var toast = new ToastNotification(toastXml);
            toast.Activated += ToastActivated;
            toast.Dismissed += ToastDismissed;
            toast.Tag = $"Controller{controllerIndex}";
            //toast.Group = "ControllerToast";

            // use this to ensure the other toast doesn't get overriden
            toast.Group = group;

            //..and send it
            ToastNotificationManager.CreateToastNotifier(APP_ID).Show(toast);

        }

        //react to click on toast or button
        private void ToastActivated(ToastNotification sender, object e)
        {
            var toastArgs = e as ToastActivatedEventArgs;
            int controllerId = 0;
            //if the return value contains a controller ID
            if (Int32.TryParse(toastArgs.Arguments, out controllerId))
            {
                //reset the toast warning (it will trigger again if battery level is still empty)
                toast_shown[controllerId].enabled = false;
            }
            //otherwise, do nothing
        }
        private void ToastDismissed(ToastNotification sender, object e)
        {
            //do nothing
        }

        public void ExitApplication()
        {
            System.Windows.Application.Current.Shutdown();
        }

        private string GetBatteryLevelCaption(BatteryLevel batteryLevel)
        {
            switch (batteryLevel)
            {
                case BatteryLevel.Empty:
                    return Strings.BatteryLevel_Empty;
                case BatteryLevel.Low:
                    return Strings.BatteryLevel_Low;
                case BatteryLevel.Medium:
                    return Strings.BatteryLevel_Medium;
                case BatteryLevel.Full:
                    return Strings.BatteryLevel_Full;
                default:
                    throw new ArgumentOutOfRangeException(nameof(batteryLevel), batteryLevel, null);
            }
        }

        private string GetControllerIndexCaption(UserIndex index)
        {
            switch (index)
            {
                case UserIndex.One:
                    return Strings.ControllerIndex_One;
                case UserIndex.Two:
                    return Strings.ControllerIndex_Two;
                case UserIndex.Three:
                    return Strings.ControllerIndex_Three;
                case UserIndex.Four:
                    return Strings.ControllerIndex_Four;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index), index, null);
            }
        }

        private void GetAvailableLanguages()
        {
            AvailableLanguages.Clear();
            foreach (var language in TranslationManager.AvailableLanguages)
            {
                AvailableLanguages.Add(language);
            }
        }

        public void UpdateNotificationSound()
        {
            _soundPlayer = File.Exists(Settings.Default.wavFile) ? new SoundPlayer(Settings.Default.wavFile) : null;
        }
        public void WatchTheme()
        {
            var currentUser = WindowsIdentity.GetCurrent();
            string query = string.Format(
                CultureInfo.InvariantCulture,
                @"SELECT * FROM RegistryValueChangeEvent WHERE Hive = 'HKEY_USERS' AND KeyPath = '{0}\\{1}' AND ValueName = '{2}'",
                currentUser.User.Value,
                ThemeRegKeyPath.Replace(@"\", @"\\"),
                ThemeRegValueName);

            try
            {
                var watcher = new ManagementEventWatcher(query);
                watcher.EventArrived += (sender, args) =>
                {
                    LightTheme();
                    
                };

                // Start listening for events
                watcher.Start();
            }
            catch (Exception)
            {
                // This can fail on Windows 7
            }

            LightTheme();
        }

        private string LightTheme()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(ThemeRegKeyPath))
            {
                object registryValueObject = key?.GetValue(ThemeRegValueName);
                if (registryValueObject == null)
                {
                    return "";
                }

                int registryValue = (int)registryValueObject;

                return registryValue > 0 ? "-black" : "";
            }
        }
    }
}