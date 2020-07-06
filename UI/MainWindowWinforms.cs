//
// MainWindowWinforms.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2016–2020 M.A.
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using MKAh;
using MKAh.Synchronize;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Taskmaster.UI
{
	using System.Globalization;
	using System.Text;
	using static Application;

	// public class MainWindow : System.Windows.Window; // TODO: WPF
	public class MainWindow : UniForm
	{
		readonly ToolTip tooltip = new ToolTip();

		System.Drawing.Color WarningColor;
		System.Drawing.Color AlterColor = System.Drawing.Color.FromArgb(245, 245, 245); // ignores user styles

		readonly System.Drawing.Color DefaultLIBGColor = new ListViewItem().BackColor; // HACK

		bool AlternateRowColorsLog { get; set; } = true;
		bool AlternateRowColorsWatchlist { get; set; } = true;
		bool AlternateRowColorsDevices { get; set; } = true;

		bool AutoOpenMenus { get; set; }

		// UI elements for non-lambda/constructor access.
		ToolStripMenuItem menu_view_loaders,
			menu_debug_network;

		ModuleManager modules;

		// constructor
		public MainWindow(ModuleManager modules)
		{
			_ = Handle; // HACK

			this.modules = modules;

			SuspendLayout();
			Visible = false;

			// InitializeComponent(); // TODO: WPF
			FormClosing += WindowClose;

			FormClosed += SelfDispose;

			ShowInTaskbar = true;

			#region Load Configuration
			using var uicfg = Application.Config.Load(UIConfigFilename);
			var qol = uicfg.Config[HumanReadable.Generic.QualityOfLife];
			ShowInTaskbar = qol.GetOrSet("Show in taskbar", ShowInTaskbar).Bool;
			AutoOpenMenus = qol.GetOrSet("Auto-open menus", true).Bool;
			#endregion // Load Configuration

			#region Build UI
			Text = Application.Name
				/*
				+ " " + Version
#if DEBUG
				+ " DEBUG"
#endif
				*/
				;

			// Padding = new Padding(6);
			// margin

			// CORE LAYOUT ITEMS

			TabPages = new Extensions.TabControl()
			{
				Parent = this,
				//Height = 300,
				Padding = new System.Drawing.Point(6, 3),
				Dock = DockStyle.Top,
				MinimumSize = new System.Drawing.Size(-2, 360),
				SizeMode = TabSizeMode.Normal,
			};

			LogPanel = new Panel()
			{
				Parent = this,
				Dock = DockStyle.Bottom,
				AutoSize = true,
				MinimumSize = new System.Drawing.Size(-2, 140),
			};

			EventLogList = new Extensions.ListViewEx
			{
				//Parent = this,
				//Dock = DockStyle.Bottom,
				Dock = DockStyle.Fill,
				AutoSize = true,
				View = View.Details,
				FullRowSelect = true,
				HeaderStyle = ColumnHeaderStyle.Nonclickable,
				Scrollable = true,
				//MinimumSize = new System.Drawing.Size(-2, 140),
				//MinimumSize = new System.Drawing.Size(-2, -2), // doesn't work
				VirtualMode = true,
				VirtualListSize = 0,
			};

			EventLogList.RetrieveVirtualItem += LogListRetrieveItem;
			EventLogList.CacheVirtualItems += LogListCacheItem;
			EventLogList.SearchForVirtualItem += VirtualModeNoAction;
			EventLogList.VirtualItemsSelectionRangeChanged += VirtualModeNoAction;

			/*
			LogSearch = new UI.Extensions.LabeledBox
			{
				Parent = EventLogList,
				Top = 3,
				AutoSizeMode = AutoSizeMode.GrowAndShrink,
				AutoSize = true,
				Anchor = AnchorStyles.Top | AnchorStyles.Right,
				Dock = DockStyle.Right,
				BackColor = System.Drawing.Color.Transparent,
			};
			*/

			LogPanel.Controls.Add(EventLogList);
			//LogPanel.Controls.Add(LogSearch);

			EventLogList.SendToBack();
			//LogSearch.BringToFront();

			ImgList = new ImageList();
			ImgList.Images.AddRange(new[] { Properties.Resources.OkayIcon, Properties.Resources.InfoIcon, Properties.Resources.ErrorIcon });

			EventLogList.SmallImageList = ImgList;

			MenuToolbar = new MenuStrip() { Dock = DockStyle.Top, Parent = this };

			BuildStatusbar();

			// LAYOUT ITEM CONFIGURATION

			#region Actions toolstrip menu
			var menu_action = new ToolStripMenuItem("Actions");
			// Sub Items
			var menu_action_rescan = new ToolStripMenuItem(HumanReadable.System.Process.Rescan, null, RescanRequestEvent)
			{
				Enabled = ProcessMonitorEnabled,
			};
			var menu_action_memoryfocus = new ToolStripMenuItem("Free memory for...", null, FreeMemoryRequest)
			{
				Enabled = PagingEnabled,
			};
			var menu_action_restart = new ToolStripMenuItem(HumanReadable.System.Process.Restart, null, RestartRequestEvent);
			menu_action_restartadmin = new ToolStripMenuItem("Restart as admin", null, RestartRequestEvent)
			{
				Enabled = !MKAh.Execution.IsAdministrator
			};

			var menu_action_exit = new ToolStripMenuItem("Exit", null, ExitRequest);
			menu_action.DropDownItems.AddRange(new ToolStripItem[] {
				menu_action_rescan,
				menu_action_memoryfocus,
				new ToolStripSeparator(),
				menu_action_restart,
				menu_action_restartadmin,
				menu_action_exit,
			});
			#endregion // Actions toolstrip menu

			#region Miscellaneous toolstrip menu
			var menu_view = new ToolStripMenuItem("View");
			var menu_view_volume = new ToolStripMenuItem(HumanReadable.Hardware.Audio.Volume, null, ShowVolumeBox)
			{
				Enabled = AudioManagerEnabled,
			};
			menu_view_loaders = new ToolStripMenuItem(Constants.Loaders, null, ShowLoaderBox)
			{
				Enabled = processmanager?.LoaderTracking ?? false,
			};

			menu_view.DropDownItems.AddRange(new ToolStripItem[] {
				menu_view_volume,
				new ToolStripSeparator(),
				menu_view_loaders,
			});
			#endregion

			// POWER menu item

			var menu_power = new ToolStripMenuItem("Power")
			{
				Enabled = PowerManagerEnabled,
			};

			if (PowerManagerEnabled)
			{
				power_auto = new ToolStripMenuItem(HumanReadable.Hardware.Power.AutoAdjust, null, SetAutoPower) { Checked = false, CheckOnClick = true, Enabled = false };
				power_highperf = new ToolStripMenuItem(Power.Utility.GetModeName(Power.Mode.HighPerformance), null, (_, _2) => SetPower(Power.Mode.HighPerformance));
				power_balanced = new ToolStripMenuItem(Power.Utility.GetModeName(Power.Mode.Balanced), null, (_, _2) => SetPower(Power.Mode.Balanced));
				power_saving = new ToolStripMenuItem(Power.Utility.GetModeName(Power.Mode.PowerSaver), null, (_, _2) => SetPower(Power.Mode.PowerSaver));
				power_manual = new ToolStripMenuItem("Manual override", null, SetManualPower) { CheckOnClick = true };

				menu_power.DropDownItems.AddRange(new ToolStripItem[] {
					power_auto,
					new ToolStripSeparator(),
					power_highperf,
					power_balanced,
					power_saving,
					new ToolStripSeparator(),
					power_manual,
				});

				if (powermanager != null)
					UpdatePowerBehaviourHighlight(powermanager.Behaviour);
			}

			// CONFIG menu item
			#region Config toolstrip menu
			var menu_config = new ToolStripMenuItem("Configuration");
			// Sub Items
			var menu_config_behaviour = new ToolStripMenuItem("Behaviour");
			var menu_config_visual = new ToolStripMenuItem("Visuals");
			var menu_config_logging = new ToolStripMenuItem(HumanReadable.Generic.Logging);
			var menu_config_bitmaskstyle = new ToolStripMenuItem("Bitmask style");
			//var menu_config_power = new ToolStripMenuItem("Power");// this submenu is no longer used

			// Sub Sub Items
			var menu_config_behaviour_autoopen = new ToolStripMenuItem("Auto-open menus")
			{
				Checked = AutoOpenMenus,
				CheckOnClick = true,
			};
			menu_config_behaviour_autoopen.Click += (_, _2) =>
			{
				AutoOpenMenus = menu_config_behaviour_autoopen.Checked;

				using var corecfg = Application.Config.Load(CoreConfigFilename);
				corecfg.Config[HumanReadable.Generic.QualityOfLife]["Auto-open menus"].Bool = AutoOpenMenus;
			};

			var menu_config_behaviour_taskbar = new ToolStripMenuItem("Show in taskbar")
			{
				Checked = ShowInTaskbar,
				CheckOnClick = true,
			};
			menu_config_behaviour_taskbar.Click += (_, _2) =>
			{
				ShowInTaskbar = menu_config_behaviour_taskbar.Checked;

				using var corecfg = Application.Config.Load(CoreConfigFilename);
				corecfg.Config[HumanReadable.Generic.QualityOfLife]["Show in taskbar"].Bool = ShowInTaskbar;
			};

			var menu_config_behaviour_exitconfirm = new ToolStripMenuItem("Exit confirmation")
			{
				Checked = ExitConfirmation,
				CheckOnClick = true,
			};
			menu_config_behaviour_exitconfirm.Click += (_, _2) =>
			{
				ExitConfirmation = menu_config_behaviour_exitconfirm.Checked;

				using var corecfg = Application.Config.Load(CoreConfigFilename);
				corecfg.Config[HumanReadable.Generic.QualityOfLife]["Exit confirmation"].Bool = ExitConfirmation;
			};

			menu_config_behaviour.DropDownItems.AddRange(new ToolStripItem[] {
				menu_config_behaviour_autoopen,
				menu_config_behaviour_taskbar,
				menu_config_behaviour_exitconfirm,
			});

			// CONFIG -> VISUALS
			var menu_config_visuals_rowalternate = new ToolStripMenuItem("Alternate row colors");

			var menu_config_visuals_rowalternate_log = new ToolStripMenuItem("Log entries")
			{
				Checked = AlternateRowColorsLog,
				CheckOnClick = true,
			};
			var menu_config_visuals_rowalternate_watchlist = new ToolStripMenuItem(WatchlistName)
			{
				Checked = AlternateRowColorsWatchlist,
				CheckOnClick = true,
			};
			var menu_config_visuals_rowalternate_devices = new ToolStripMenuItem("Devices")
			{
				Checked = AlternateRowColorsDevices,
				CheckOnClick = true,
			};

			menu_config_visuals_rowalternate.DropDownItems.AddRange(new ToolStripItem[] {
				menu_config_visuals_rowalternate_log,
				menu_config_visuals_rowalternate_watchlist,
				menu_config_visuals_rowalternate_devices,
			});

			menu_config_visuals_rowalternate_log.Click += (_, _2) =>
			{
				AlternateRowColorsLog = menu_config_visuals_rowalternate_log.Checked;

				using var uicfg = Application.Config.Load(UIConfigFilename);
				uicfg.Config[Constants.Visuals]["Alternate log row colors"].Bool = AlternateRowColorsLog;

				AlternateListviewRowColors(EventLogList, AlternateRowColorsLog);
			};
			menu_config_visuals_rowalternate_watchlist.Click += (_, _2) =>
			{
				AlternateRowColorsWatchlist = menu_config_visuals_rowalternate_watchlist.Checked;

				using var uicfg = Application.Config.Load(UIConfigFilename);
				uicfg.Config[Constants.Visuals]["Alternate watchlist row colors"].Bool = AlternateRowColorsWatchlist;

				WatchlistColor();
			};
			menu_config_visuals_rowalternate_devices.Click += (_, _2) =>
			{
				AlternateRowColorsDevices = menu_config_visuals_rowalternate_devices.Checked;

				using var uicfg = Application.Config.Load(UIConfigFilename);
				uicfg.Config[Constants.Visuals]["Alternate device row colors"].Bool = AlternateRowColorsDevices;

				if (AudioInputs != null)
					AlternateListviewRowColors(AudioInputs, AlternateRowColorsDevices);
				if (NetworkDevices != null)
					AlternateListviewRowColors(NetworkDevices, AlternateRowColorsDevices);
			};

			//
			var menu_config_visuals_topmost = new ToolStripMenuItem(Constants.StayOnTop);

			var menu_config_visuals_topmost_volume = new ToolStripMenuItem(Constants.VolumeMeter)
			{
				Checked = false,
				CheckOnClick = true,
			};
			menu_config_visuals_topmost_volume.Click += (_, _2) =>
			{
				using var corecfg = Application.Config.Load(CoreConfigFilename);
				corecfg.Config[Constants.VolumeMeter][TopmostName].Bool = menu_config_visuals_topmost_volume.Checked;

				if (modules.volumemeter != null)
					modules.volumemeter.TopMost = menu_config_visuals_topmost_volume.Checked;
			};
			/*
			var menu_config_visuals_topmost_main = new ToolStripMenuItem("Main window")
			{
				Checked = TopMost,
				CheckOnClick = true,
			};
			*/

			menu_config_visuals_topmost.DropDownItems.Add(menu_config_visuals_topmost_volume);

			var menu_config_visuals_styling = new ToolStripMenuItem("Styling")
			{
				Checked = VisualStyling,
				CheckOnClick = true,
			};
			menu_config_visuals_styling.Click += (_, _2) =>
			{
				using var uicfg = Application.Config.Load(UIConfigFilename);
				uicfg.Config[Constants.Windows]["Styling"].Bool = menu_config_visuals_styling.Checked;

				VisualStyling = menu_config_visuals_styling.Checked;
				UpdateStyling();
			};

			menu_config_visual.DropDownItems.AddRange(new ToolStripItem[] {
				menu_config_visuals_styling,
				menu_config_visuals_rowalternate,
				menu_config_visuals_topmost,
			});

			// CONFIG -> LOGGING
			var menu_config_logging_adjusts = new ToolStripMenuItem("Process adjusts")
			{
				Checked = ShowProcessAdjusts,
				CheckOnClick = true,
			};
			menu_config_logging_adjusts.Click += (_, _2) =>
			{
				ShowProcessAdjusts = menu_config_logging_adjusts.Checked;

				using var corecfg = Application.Config.Load(CoreConfigFilename);
				corecfg.Config[HumanReadable.Generic.Logging]["Show process adjusts"].Bool = ShowProcessAdjusts;
			};

			var menu_config_logging_session = new ToolStripMenuItem("Session actions")
			{
				Checked = ShowSessionActions,
				CheckOnClick = true,
			};
			menu_config_logging_session.Click += (_, _2) =>
			{
				ShowSessionActions = menu_config_logging_session.Checked;

				using var corecfg = Application.Config.Load(CoreConfigFilename);
				corecfg.Config[HumanReadable.Generic.Logging]["Show session actions"].Bool = ShowSessionActions;
			};

			var menu_config_logging_showunmodified = new ToolStripMenuItem(ShowUnmodifiedPortionsName)
			{
				Checked = Process.Manager.ShowUnmodifiedPortions,
				CheckOnClick = true,
			};
			menu_config_logging_showunmodified.Click += (_, _2) =>
			{
				Process.Manager.ShowUnmodifiedPortions = menu_config_logging_showunmodified.Checked;

				using var corecfg = Application.Config.Load(CoreConfigFilename);
				corecfg.Config[HumanReadable.Generic.Logging][ShowUnmodifiedPortionsName].Bool = Process.Manager.ShowUnmodifiedPortions;
			};

			var menu_config_logging_showonlyfinal = new ToolStripMenuItem("Final state only")
			{
				Checked = Process.Manager.ShowOnlyFinalState,
				CheckOnClick = true,
			};
			menu_config_logging_showonlyfinal.Click += (_, _2) =>
			{
				Process.Manager.ShowOnlyFinalState = menu_config_logging_showonlyfinal.Checked;
				using var corecfg = Application.Config.Load(CoreConfigFilename);
				corecfg.Config[HumanReadable.Generic.Logging]["Final state only"].Bool = Process.Manager.ShowOnlyFinalState;
			};

			var menu_config_logging_neterrors = new ToolStripMenuItem("Network errors")
			{
				Checked = Network.Manager.ShowNetworkErrors,
				CheckOnClick = true,
			};

			menu_config_logging_neterrors.Click += (_, _2) =>
			{
				Network.Manager.ShowNetworkErrors = menu_config_logging_neterrors.Checked;

				using var corecfg = Application.Config.Load(CoreConfigFilename);
				corecfg.Config[HumanReadable.Generic.Logging]["Show network errors"].Bool = Network.Manager.ShowNetworkErrors;
			};

			var menu_config_logging_info = new ToolStripMenuItem("Information")
			{
				Checked = loglevelswitch.MinimumLevel == Serilog.Events.LogEventLevel.Information,
				CheckOnClick = true,
			};
			var menu_config_logging_debug = new ToolStripMenuItem(HumanReadable.Generic.Debug)
			{
				Checked = loglevelswitch.MinimumLevel == Serilog.Events.LogEventLevel.Debug,
				CheckOnClick = true,
			};
			var menu_config_logging_trace = new ToolStripMenuItem(TraceName)
			{
				Checked = loglevelswitch.MinimumLevel == Serilog.Events.LogEventLevel.Verbose,
				CheckOnClick = true,
			};

			void SetLogLevel(Serilog.Events.LogEventLevel level)
			{
				menu_config_logging_debug.Checked = level == Serilog.Events.LogEventLevel.Debug;
				menu_config_logging_trace.Checked = level == Serilog.Events.LogEventLevel.Verbose;
				menu_config_logging_info.Checked = level == Serilog.Events.LogEventLevel.Information;
				loglevelswitch.MinimumLevel = level;
			}

			menu_config_logging_info.Click += (_, _2) => SetLogLevel(Serilog.Events.LogEventLevel.Information);
			menu_config_logging_debug.Click += (_, _2) => SetLogLevel(Serilog.Events.LogEventLevel.Debug);
			menu_config_logging_trace.Click += (_, _2) => SetLogLevel(Serilog.Events.LogEventLevel.Verbose);

			var menu_config_logging_bitmask = new ToolStripMenuItem("Bitmask");

			var menu_config_logging_bitmask_decimal = new ToolStripMenuItem("Decimal") { Checked = LogBitmask == BitmaskStyle.Decimal };
			var menu_config_logging_bitmask_bits = new ToolStripMenuItem("Bits") { Checked = LogBitmask == BitmaskStyle.Bits };
			var menu_config_logging_bitmask_mixed = new ToolStripMenuItem("Mixed") { Checked = LogBitmask == BitmaskStyle.Mixed };

			void SetLogBitmask(BitmaskStyle style)
			{
				menu_config_logging_bitmask_decimal.Checked = style == BitmaskStyle.Decimal;
				menu_config_logging_bitmask_bits.Checked = style == BitmaskStyle.Bits;
				menu_config_logging_bitmask_mixed.Checked = style == BitmaskStyle.Mixed;
				LogBitmask = style;

				// TODO: Move to on exit logic
				using var corecfg = Application.Config.Load(CoreConfigFilename);
				corecfg.Config[HumanReadable.Generic.Logging]["Bitmask style"].Int = (int)LogBitmask;
			}

			menu_config_logging_bitmask_decimal.Click += (_, _2) => SetLogBitmask(BitmaskStyle.Decimal);
			menu_config_logging_bitmask_bits.Click += (_, _2) => SetLogBitmask(BitmaskStyle.Bits);
			menu_config_logging_bitmask_mixed.Click += (_, _2) => SetLogBitmask(BitmaskStyle.Mixed);

			menu_config_logging_bitmask.DropDownItems.AddRange(new ToolStripItem[] {
				menu_config_logging_bitmask_decimal,
				menu_config_logging_bitmask_bits,
				menu_config_logging_bitmask_mixed,
			});

			menu_config_logging_adjusts.DropDownItems.AddRange(new ToolStripItem[] {
				menu_config_logging_showunmodified,
				menu_config_logging_showonlyfinal,
			});

			menu_config_logging.DropDownItems.AddRange(new ToolStripItem[] {
				menu_config_logging_adjusts,
				menu_config_logging_bitmask,
				menu_config_logging_session,
				menu_config_logging_neterrors,
				new ToolStripSeparator(),
				new ToolStripLabel("– NVM log Level –") { ForeColor = System.Drawing.SystemColors.GrayText },
				menu_config_logging_info,
				menu_config_logging_debug,
				menu_config_logging_trace,
			});

			var menu_config_bitmaskstyle_bitmask = new ToolStripMenuItem("Bitmask")
			{
				Checked = AffinityStyle == 0,
			};
			var menu_config_bitmaskstyle_decimal = new ToolStripMenuItem("Decimal")
			{
				Checked = (AffinityStyle == BitmaskStyle.Decimal),
			};
			menu_config_bitmaskstyle_bitmask.Click += (_, _2) =>
			{
				AffinityStyle = BitmaskStyle.Bits;
				menu_config_bitmaskstyle_bitmask.Checked = true;
				menu_config_bitmaskstyle_decimal.Checked = false;
				// TODO: re-render watchlistRules

				using var corecfg = Application.Config.Load(CoreConfigFilename);
				corecfg.Config[HumanReadable.Generic.QualityOfLife][HumanReadable.Hardware.CPU.Settings.AffinityStyle].Int = 0;
			};
			menu_config_bitmaskstyle_decimal.Click += (_, _2) =>
			{
				AffinityStyle = BitmaskStyle.Decimal;
				menu_config_bitmaskstyle_bitmask.Checked = false;
				menu_config_bitmaskstyle_decimal.Checked = true;
				// TODO: re-render watchlistRules

				using var corecfg = Application.Config.Load(CoreConfigFilename);
				corecfg.Config[HumanReadable.Generic.QualityOfLife][HumanReadable.Hardware.CPU.Settings.AffinityStyle].Int = 1;
			};
			//var menu_config_bitmaskstyle_both = new ToolStripMenuItem("Decimal [Bitmask]");

			menu_config_bitmaskstyle.DropDownItems.Add(menu_config_bitmaskstyle_bitmask);
			menu_config_bitmaskstyle.DropDownItems.Add(menu_config_bitmaskstyle_decimal);
			//menu_config_bitmaskstyle.DropDownItems.Add(menu_config_bitmaskstyle_both);

			var menu_config_advanced = new ToolStripMenuItem("Advanced", null, (_, _2) => Config.AdvancedConfig.Reveal());

			var menu_config_powermanagement = new ToolStripMenuItem("Power management", null, (_, _2) => Config.PowerConfigWindow.Reveal(modules))
			{
				Enabled = PowerManagerEnabled
			};
			//menu_config_power.DropDownItems.Add(menu_config_power_autoadjust); // sub-menu removed

			//

			var menu_config_log = new ToolStripMenuItem(HumanReadable.Generic.Logging);
			var menu_config_log_power = new ToolStripMenuItem("Power mode changes", null, (_, _2) => { });
			menu_config_log.DropDownItems.Add(menu_config_log_power);

			var menu_config_components = new ToolStripMenuItem("Components", null, (_, _2) => Config.ComponentConfigurationWindow.Reveal(modules)); // MODAL
			var menu_config_experiments = new ToolStripMenuItem("Experiments", null, (_, _2) => Config.ExperimentConfig.Reveal()); // MODAL

			var menu_config_folder = new ToolStripMenuItem("Open in file manager", null, (_, _2) => System.Diagnostics.Process.Start(DataPath));
			// menu_config.DropDownItems.Add(menu_config_log);

			menu_config.DropDownItems.AddRange(new ToolStripItem[] {
				menu_config_behaviour,
				menu_config_visual,
				menu_config_logging,
				menu_config_bitmaskstyle,
				new ToolStripSeparator(),
				menu_config_advanced,
				menu_config_powermanagement,
				menu_config_components,
				new ToolStripSeparator(),
				menu_config_experiments,
				new ToolStripSeparator(),
				menu_config_folder,
			});
			#endregion // Config toolstrip menu

			// DEBUG menu item
			#region Debug toolstrip menu
			menu_debug = new ToolStripMenuItem(HumanReadable.Generic.Debug);
			// Sub Items
			menu_debug_loglevel = new ToolStripMenuItem("UI log level");

			LogIncludeLevel = MemoryLog.MemorySink.LevelSwitch; // HACK

			menu_debug_loglevel_info = new ToolStripMenuItem(InfoName, null,
			(_, _2) =>
			{
				LogIncludeLevel.MinimumLevel = Serilog.Events.LogEventLevel.Information;
				Trace = false;
				UpdateLogLevelSelection();
			})
			{
				CheckOnClick = true,
				Checked = (LogIncludeLevel.MinimumLevel == Serilog.Events.LogEventLevel.Information),
			};
			menu_debug_loglevel_debug = new ToolStripMenuItem(HumanReadable.Generic.Debug, null,
			(_, _2) =>
			{
				LogIncludeLevel.MinimumLevel = Serilog.Events.LogEventLevel.Debug;
				Trace = false;
				UpdateLogLevelSelection();
			})
			{
				CheckOnClick = true,
				Checked = (LogIncludeLevel.MinimumLevel == Serilog.Events.LogEventLevel.Debug),
			};
#if DEBUG
			menu_debug_loglevel_trace = new ToolStripMenuItem(TraceName, null,
			(_, _2) =>
			{
				LogIncludeLevel.MinimumLevel = Serilog.Events.LogEventLevel.Verbose;
				Trace = true;
				UpdateLogLevelSelection();
				Log.Warning("Trace events enabled. UI may become unresponsive due to their volume.");
			})
			{
				CheckOnClick = true,
				Checked = (LogIncludeLevel.MinimumLevel == Serilog.Events.LogEventLevel.Verbose),
			};
#endif
			menu_debug_loglevel.DropDownItems.Add(menu_debug_loglevel_info);
			menu_debug_loglevel.DropDownItems.Add(menu_debug_loglevel_debug);
#if DEBUG
			menu_debug_loglevel.DropDownItems.Add(menu_debug_loglevel_trace);
#endif

			UpdateLogLevelSelection();

			menu_debug_keepsettings = new ToolStripMenuItem("Save debug settings") { Checked = SaveDebugSettings, CheckOnClick = true };
			menu_debug_keepsettings.Click += (_, _2) => SaveDebugSettings = menu_debug_keepsettings.Checked;

			menu_debug_inaction = new ToolStripMenuItem("Show inaction") { Checked = ShowInaction, CheckOnClick = true };
			menu_debug_inaction.Click += (_, _2) => ShowInaction = menu_debug_inaction.Checked;
			menu_debug_agency = new ToolStripMenuItem("Show agency") { Checked = ShowAgency, CheckOnClick = true };
			menu_debug_agency.Click += (_, _2) => ShowAgency = menu_debug_agency.Checked;
			menu_debug_scanning = new ToolStripMenuItem("Scanning")
			{
				Checked = Process.Manager.DebugScan,
				CheckOnClick = true,
				Enabled = ProcessMonitorEnabled,
			};
			menu_debug_scanning.Click += (_, _2) =>
			{
				Process.Manager.DebugScan = menu_debug_scanning.Checked;
				if (Process.Manager.DebugScan) EnsureVerbosityLevel();
			};

			var menu_debug_procs = new ToolStripMenuItem("Processes")
			{
				Checked = Process.Manager.DebugProcesses,
				CheckOnClick = true,
				Enabled = ProcessMonitorEnabled,
			};

			menu_debug_procs.Click += (_, _2) =>
			{
				processmanager.SetDebug(menu_debug_procs.Checked);
				if (Process.Manager.DebugProcesses)
					StartProcessDebug();
				else
					StopProcessDebug();
			};

			var menu_debug_adjustdelay = new ToolStripMenuItem("Adjust delay")
			{
				Checked = Process.Manager.DebugAdjustDelay,
				CheckOnClick = true,
				Enabled = ProcessMonitorEnabled,
			};
			menu_debug_adjustdelay.Click += (_, _2) => Process.Manager.DebugAdjustDelay = menu_debug_adjustdelay.Checked;

			var menu_debug_foreground = new ToolStripMenuItem(HumanReadable.System.Process.Foreground)
			{
				Checked = DebugForeground,
				CheckOnClick = true,
				Enabled = ActiveAppMonitorEnabled,
			};
			menu_debug_foreground.Click += (_, _2) =>
			{
				DebugForeground = menu_debug_foreground.Checked;
				if (DebugForeground)
					StartProcessDebug();
				else
					StopProcessDebug();
			};

			/*
			var menu_debug_paths = new ToolStripMenuItem("Paths")
			{
				Checked = Process.Manager.DebugPaths,
				CheckOnClick = true,
			};
			menu_debug_paths.Click += (_, _2) =>
			{
				Process.Manager.DebugPaths = menu_debug_paths.Checked;
				if (Process.Manager.DebugPaths) EnsureVerbosityLevel();
			};
			*/
			var menu_debug_power = new ToolStripMenuItem(HumanReadable.Hardware.Power.Section)
			{
				Checked = DebugPower,
				CheckOnClick = true,
				Enabled = PowerManagerEnabled,
			};
			menu_debug_power.Click += (_, _2) =>
			{
				DebugPower = menu_debug_power.Checked;
				if (DebugPower)
				{
					if (powerDebugTab is null) BuildPowerDebugPanel();
					else TabPages.Controls.Add(powerDebugTab);
					EnsureVerbosityLevel();

					AttachPowerDebug();
				}
				else
				{
					DetachPowerDebug();

					bool refocus = TabPages.SelectedTab.Equals(powerDebugTab);

					TabPages.Controls.Remove(powerDebugTab);
					if (refocus) TabPages.SelectedIndex = 1; // watchlist

					powerDebugTab?.Dispose();
					powerDebugTab = null;
				}
			};

			menu_debug_network = new ToolStripMenuItem("Network")
			{
				Checked = netmonitor?.DebugNet ?? false,
				CheckOnClick = true,
				Enabled = NetworkMonitorEnabled,
			};
			menu_debug_network.Click += (_, _2) =>
			{
				if (netmonitor != null)
				{
					netmonitor.DebugNet = menu_debug_network.Checked;
				}
			};

			var menu_debug_session = new ToolStripMenuItem("Session")
			{
				Checked = DebugSession,
				CheckOnClick = true,
				Enabled = PowerManagerEnabled,
			};
			menu_debug_session.Click += (_, _2) =>
			{
				DebugSession = menu_debug_session.Checked;
				if (DebugSession) EnsureVerbosityLevel();
			};
			var menu_debug_monitor = new ToolStripMenuItem(HumanReadable.Hardware.Monitor.Section)
			{
				Checked = DebugMonitor,
				CheckOnClick = true,
				Enabled = PowerManagerEnabled,
			};
			menu_debug_monitor.Click += (_, _2) =>
			{
				DebugMonitor = menu_debug_monitor.Checked;
				if (DebugMonitor) EnsureVerbosityLevel();
			};

			var menu_debug_audio = new ToolStripMenuItem(HumanReadable.Hardware.Audio.Section)
			{
				Checked = DebugAudio,
				CheckOnClick = true,
				Enabled = AudioManagerEnabled,
			};
			menu_debug_audio.Click += (_, _2) =>
			{
				DebugAudio = menu_debug_audio.Checked;
				if (DebugAudio) EnsureVerbosityLevel();
			};

			var menu_debug_clear = new ToolStripMenuItem("Clear UI log", null, (_, _2) => ClearLog());

			// TODO: This menu needs to be clearer
			menu_debug.DropDownItems.Add(menu_debug_loglevel);
			menu_debug.DropDownItems.Add(new ToolStripSeparator());
			menu_debug.DropDownItems.Add(menu_debug_keepsettings);
			menu_debug.DropDownItems.Add(new ToolStripSeparator());
			menu_debug.DropDownItems.Add(menu_debug_inaction);
			menu_debug.DropDownItems.Add(menu_debug_agency);
			menu_debug.DropDownItems.Add(new ToolStripSeparator());
			//menu_debug.DropDownItems.Add(menu_debug_scanning);
			menu_debug.DropDownItems.Add(menu_debug_procs);
			menu_debug.DropDownItems.Add(menu_debug_adjustdelay);
			menu_debug.DropDownItems.Add(menu_debug_foreground);
			//menu_debug.DropDownItems.Add(menu_debug_paths);
			menu_debug.DropDownItems.Add(menu_debug_power);
			menu_debug.DropDownItems.Add(menu_debug_network);
			menu_debug.DropDownItems.Add(menu_debug_session);
			menu_debug.DropDownItems.Add(menu_debug_monitor);
			menu_debug.DropDownItems.Add(menu_debug_audio);
			menu_debug.DropDownItems.Add(new ToolStripSeparator());
			menu_debug.DropDownItems.Add(menu_debug_clear);
			#endregion // Debug toolstrip menu

			// INFO menu
			#region Info toolstrip menu
			var menu_info = new ToolStripMenuItem(InfoName);
			// Sub Items

			//menu_info.DropDownItems.Add(new ToolStripMenuItem("Changelog", null, OpenChangelog));
			//menu_info.DropDownItems.Add(new ToolStripSeparator());
			menu_info.DropDownItems.Add(new ToolStripMenuItem("Github", null, (_, _2) => System.Diagnostics.Process.Start(GitURL.ToString())));
			menu_info.DropDownItems.Add(new ToolStripMenuItem("Itch.io", null, (_, _2) => System.Diagnostics.Process.Start(ItchURL.ToString())));
			menu_info.DropDownItems.Add(new ToolStripSeparator());
			menu_info.DropDownItems.Add(new ToolStripMenuItem(Constants.License, null, (_, _2) => OpenLicenseDialog()));
			menu_info.DropDownItems.Add(new ToolStripMenuItem("3rd party licenses", null, ShowExternalLicenses));
			menu_info.DropDownItems.Add(new ToolStripSeparator());
			menu_info.DropDownItems.Add(new ToolStripMenuItem("About", null, ShowAboutDialog));
			#endregion

			MenuToolbar.Items.AddRange(new[] { menu_action, menu_view, menu_power, menu_config, menu_debug, menu_info });

			// no simpler way?

			menu_action.MouseEnter += ToolStripMenuAutoOpen;
			menu_view.MouseEnter += ToolStripMenuAutoOpen;
			menu_config.MouseEnter += ToolStripMenuAutoOpen;
			menu_debug.MouseEnter += ToolStripMenuAutoOpen;
			menu_info.MouseEnter += ToolStripMenuAutoOpen;

			menu_action.DropDown.AutoClose = true;
			menu_view.DropDown.AutoClose = true;
			menu_config.DropDown.AutoClose = true;
			menu_debug.DropDown.AutoClose = true;
			menu_info.DropDown.AutoClose = true;

			infoTab = new Extensions.TabPage(InfoName) { Padding = BigPadding };
			TabPages.Controls.Add(infoTab);

			watchTab = new Extensions.TabPage(WatchlistName) { Padding = BigPadding };
			TabPages.Controls.Add(watchTab);

			var infopanel = new FlowLayoutPanel
			{
				Anchor = AnchorStyles.Top,
				Dock = DockStyle.Fill,
				FlowDirection = FlowDirection.TopDown,
				WrapContents = false,
				AutoSize = true,
				//Padding = DefaultPadding,
			};

			LoadUIConfiguration(out int opentab, out int[] appwidths, out int[] apporder, out int[] micwidths, out int[] ifacewidths);

			if (MicrophoneManagerEnabled)
			{
				var micpanel = new Extensions.TableLayoutPanel
				{
					Dock = DockStyle.Fill,
					ColumnCount = 1,
					//Padding = DefaultPadding,
					//Width = tabLayout.Width - 12
				};
				micpanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20)); // this is dumb

				AudioInputDevice = new Extensions.Label { Text = HumanReadable.Generic.Uninitialized, AutoEllipsis = true };
				var micNameRow = new Extensions.TableLayoutPanel
				{
					RowCount = 1,
					ColumnCount = 2,
					Dock = DockStyle.Top,
					//AutoSize = true // why not?
				};
				micNameRow.Controls.Add(new Extensions.Label { Text = "Default communications device:" });
				micNameRow.Controls.Add(AudioInputDevice);

				var miccntrl = new Extensions.TableLayoutPanel()
				{
					RowCount = 1,
					ColumnCount = 6,
					Dock = DockStyle.Fill,
					AutoSize = true,
				};

				AudioInputVolume = new Extensions.NumericUpDownEx
				{
					Unit = "%",
					Increment = 1.0M,
					Maximum = 100.0M,
					Minimum = 0.0M,
					Width = 60,
					ReadOnly = true,
					Enabled = false,
					Dock = DockStyle.Top
				};
				AudioInputVolume.ValueChanged += UserMicVol;

				corCountLabel = new Extensions.Label { Text = "0" };

				AudioInputEnable = new ComboBox()
				{
					DropDownStyle = ComboBoxStyle.DropDownList,
					Items = { HumanReadable.Generic.Enabled, HumanReadable.Generic.Disabled },
					SelectedIndex = 1,
					Enabled = false,
				};

				miccntrl.Controls.AddRange(new Control[] {
					new Extensions.Label { Text = HumanReadable.Hardware.Audio.Volume },
					AudioInputVolume,
					new Extensions.Label { Text = "Correction count:" },
					corCountLabel,
					new Extensions.Label { Text = "Control:" },
					AudioInputEnable,
				});

				// End: Volume control

				// Main Window row 3, microphone device enumeration
				AudioInputs = new Extensions.ListViewEx
				{
					Dock = DockStyle.Top,
					Height = 120,
					View = View.Details,
					AutoSize = true,
					MinimumSize = new System.Drawing.Size(-2, -2),
					FullRowSelect = true
				};

				AudioInputs.Columns.Add(Constants.Name, micwidths[0]);
				AudioInputs.Columns.Add(Constants.GUID, micwidths[1]);
				AudioInputs.Columns.Add(HumanReadable.Hardware.Audio.Volume, micwidths[2]);
				AudioInputs.Columns.Add("Target", micwidths[3]);
				AudioInputs.Columns.Add("Control", micwidths[4]);
				AudioInputs.Columns.Add("State", micwidths[5]);

				micpanel.SizeChanged += (_, _2) => AudioInputs.Width = micpanel.Width - micpanel.Margin.Horizontal - micpanel.Padding.Horizontal;

				micpanel.Controls.AddRange(new Control[] { micNameRow, miccntrl, AudioInputs });

				micTab = new Extensions.TabPage(HumanReadable.Hardware.Audio.Microphone) { Padding = BigPadding };

				micTab.Controls.Add(micpanel);

				TabPages.Controls.Add(micTab);
			}

			// Main Window row 4-5, internet status
			var netstatus = NetworkMonitorEnabled ? BuildNetworkStatusUI(infopanel, ifacewidths) : null;
			// End: Inet status

			GotFocus += StartUIUpdates;

			FormClosing += StopUIUpdates;

			//UItimer.Tick += Cleanup;

			// End: Settings

			BuildWatchlist(appwidths, apporder);

			// UI Log
			// -1 = contents, -2 = heading
			EventLogMainColumn = EventLogList.Columns.Add("Event Log", -2, HorizontalAlignment.Left); // 2

			//ResizeEnd += ResizeLogList;
			//Resize += ResizeLogList;

			SizeChanged += ResizeLogList;
			Shown += ResizeLogList;

			var loglistms = new ContextMenuStrip();
			var logcopy = new ToolStripMenuItem("Copy to clipboard", null, CopyLogToClipboard);
			loglistms.Items.Add(logcopy);
			EventLogList.ContextMenuStrip = loglistms;

			using var cfg = Application.Config.Load(CoreConfigFilename);
			MaxLogSize = cfg.Config[HumanReadable.Generic.Logging].GetOrSet("UI max items", 200)
				.InitComment("Maximum number of items/lines to retain on UI level.")
				.Int;

			UItimer.Interval = cfg.Config[Constants.UserInterface].GetOrSet(UpdateFrequencyName, 2000)
				.InitComment("In milliseconds. Frequency of controlled UI updates. Affects visual accuracy of timers and such. Valid range: 100 to 5000.")
				.Int.Constrain(100, 5000);

			if (AudioManagerEnabled)
			{
				menu_config_visuals_topmost_volume.Checked = cfg.Config.Get(Constants.VolumeMeter)?.Get(TopmostName)?.Bool ?? true;
			}

			Extensions.TableLayoutPanel cachePanel = null;
			if (DebugCache)
			{
				cachePanel = new Extensions.TableLayoutPanel()
				{
					ColumnCount = 5,
					AutoSize = true,
					Dock = DockStyle.Fill,
				};

				cacheObjects = new Extensions.Label() { Width = 40, Text = HumanReadable.Generic.Uninitialized };
				cacheRatio = new Extensions.Label() { Width = 40, Text = HumanReadable.Generic.Uninitialized };

				cachePanel.Controls.AddRange(new Control[] {
					new Extensions.Label() { Text = "Path cache:" },
					new Extensions.Label() { Text = "Objects" },
					cacheObjects,
					new Extensions.Label() { Text = "Ratio" },
					cacheRatio,
				});
			}

			Extensions.TableLayoutPanel tempmonitorpanel = null;
			if (TempMonitorEnabled)
			{
				tempObjectCount = new Extensions.Label() { Width = 40, Text = HumanReadable.Generic.Uninitialized };

				tempObjectSize = new Extensions.Label() { Width = 40, Text = HumanReadable.Generic.Uninitialized };

				tempmonitorpanel = new Extensions.TableLayoutPanel
				{
					Dock = DockStyle.Top,
					RowCount = 1,
					ColumnCount = 5,
					Height = 40,
					AutoSize = true
				};

				tempmonitorpanel.Controls.AddRange(new Control[] {
					new Extensions.Label { Text = "Temp" },
					new Extensions.Label { Text = "Objects" },
					tempObjectCount,
					new Extensions.Label { Text = "Size (MB)" },
					tempObjectSize,
				});
			}

			var corepanel = new Extensions.TableLayoutPanel()
			{
				ColumnCount = 2,
				AutoSize = true,
				AutoSizeMode = AutoSizeMode.GrowOnly,
				//Dock = DockStyle.Fill,
				Dock = DockStyle.Fill,
			};

			if (PowerManagerEnabled)
			{
				corepanel.Controls.Add(new Extensions.Label() { Text = "CPU" });
				cpuload = new Extensions.Label() { Text = HumanReadable.Generic.Uninitialized };
				corepanel.Controls.Add(cpuload);
			}
			// TODO: Add high, low and average

			corepanel.Controls.Add(new Extensions.Label() { Text = "RAM" });
			ramload = new Extensions.Label() { Text = HumanReadable.Generic.Uninitialized };
			corepanel.Controls.Add(ramload);

			Extensions.TableLayoutPanel gpupanel = null;
			if (HardwareMonitorEnabled)
			{
				gpupanel = new Extensions.TableLayoutPanel()
				{
					ColumnCount = 2,
					AutoSize = true,
					AutoSizeMode = AutoSizeMode.GrowOnly,
					//Dock = DockStyle.Fill,
					Dock = DockStyle.Fill,
				};

				gpuvram = new Extensions.Label() { Text = HumanReadable.Generic.Uninitialized };
				gpuload = new Extensions.Label() { Text = HumanReadable.Generic.Uninitialized };
				gputemp = new Extensions.Label() { Text = HumanReadable.Generic.Uninitialized };
				gpufan = new Extensions.Label() { Text = HumanReadable.Generic.Uninitialized };

				gpupanel.Controls.AddRange(new Control[] {
					new Extensions.Label() { Text = "VRAM" },
					gpuvram,
					new Extensions.Label() { Text = "Load" },
					gpuload,
					new Extensions.Label() { Text = "Temp" },
					gputemp,
					new Extensions.Label() { Text = "Fan" },
					gpufan,
				});
			}

			TableLayoutPanel nvmpanel = null;
			if (HealthMonitorEnabled) BuildNVMPanel(out nvmpanel);

			TableLayoutPanel powerpanel = null;
			if (PowerManagerEnabled) BuildPowerPanel(out powerpanel);

			Extensions.TableLayoutPanel lastmodifypanel = null;
			if (LastModifiedList) lastmodifypanel = BuildLastModifiedPanel(appwidths);

			var coresystems = new FlowLayoutPanel()
			{
				FlowDirection = FlowDirection.TopDown,
				WrapContents = false,
				Dock = DockStyle.Fill,
				AutoSizeMode = AutoSizeMode.GrowOnly,
				AutoSize = true,
			};

			var additionalsystems = new FlowLayoutPanel()
			{
				FlowDirection = FlowDirection.TopDown,
				WrapContents = false,
				Dock = DockStyle.Fill,
				AutoSizeMode = AutoSizeMode.GrowOnly,
				AutoSize = true,
			};

			var systemlayout = new Extensions.TableLayoutPanel()
			{
				ColumnCount = 2,
				RowCount = 1,
				Dock = DockStyle.Fill,
				AutoSizeMode = AutoSizeMode.GrowOnly,
				AutoSize = true,
			};

			// Insert info panel/tab contents
			if (corepanel != null)
			{
				coresystems.Controls.Add(new Extensions.Label() { Text = Constants.Core, Font = BoldFont });
				coresystems.Controls.Add(corepanel);
			}
			if (gpupanel != null)
			{
				coresystems.Controls.Add(new Extensions.Label() { Text = "GPU", Font = BoldFont });
				coresystems.Controls.Add(gpupanel);
			}
			if (powerpanel != null)
			{
				additionalsystems.Controls.Add(new Extensions.Label { Text = HumanReadable.Hardware.Power.Section, Font = BoldFont });
				additionalsystems.Controls.Add(powerpanel);
			}
			if (nvmpanel != null)
			{
				additionalsystems.Controls.Add(new Extensions.Label { Text = "Non-Volatile Memory", Font = BoldFont });
				additionalsystems.Controls.Add(nvmpanel);
			}
			systemlayout.Controls.Add(coresystems);
			systemlayout.Controls.Add(additionalsystems);
			systemlayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
			systemlayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f)); // surprisingly not redundant
			infopanel.Controls.Add(systemlayout);

			if (netstatus != null && NetworkDevices != null)
			{
				infopanel.Controls.Add(netstatus);
				infopanel.Controls.Add(NetworkDevices);
			}
			if (cachePanel != null) infopanel.Controls.Add(cachePanel);
			if (tempmonitorpanel != null) infopanel.Controls.Add(tempmonitorpanel);
			if (lastmodifypanel != null) infopanel.Controls.Add(lastmodifypanel);

			infoTab.Controls.Add(infopanel);

			// POWER DEBUG TAB

			if (DebugPower) BuildPowerDebugPanel();

			// -------------------------------------------------------------------------------------------------------

			if (Process.Manager.DebugProcesses || DebugForeground)
				BuildProcessDebug();

			// End Process Debug

			TabPages.SelectedIndex = opentab >= TabPages.TabCount ? 0 : opentab;

			// HANDLE TIMERS

			UItimer.Tick += UpdateMemoryStats;
			GotFocus += UpdateMemoryStats;
			UpdateMemoryStats(this, EventArgs.Empty);

			UItimer.Tick += UpdateTrackingCounter;
			GotFocus += UpdateTrackingCounter;
			UpdateTrackingCounter(this, EventArgs.Empty);

			if (DebugCache && Process.Utility._PathCacheSettings.MaxItems > 0)
			{
				UItimer.Tick += PathCacheUpdate;
				GotFocus += PathCacheUpdate;
				PathCacheUpdate(this, EventArgs.Empty);
			}
			#endregion

			tooltip.IsBalloon = true;
			tooltip.InitialDelay = 2000;
			tooltip.ShowAlways = true;

			WatchlistSearchTimer.Interval = 250;
			WatchlistSearchTimer.Tick += WatchlistSearchTimer_Tick;

			// TODO: Detect mic device changes
			// TODO: Delay fixing by 5 seconds to prevent fix diarrhea

			// the form itself
			WindowState = FormWindowState.Normal;

			FormBorderStyle = FormBorderStyle.Sizable;
			SizeGripStyle = SizeGripStyle.Auto;

			AutoSizeMode = AutoSizeMode.GrowOnly;
			AutoSize = false;

			MaximizeBox = true;
			MinimizeBox = true;

			MinimumHeight += TabPages.MinimumSize.Height
				+ EventLogList.MinimumSize.Height
				+ MenuToolbar.Height
				+ statusbar.Height
				+ 40; // why is this required? window deco?

			MinimumSize = new System.Drawing.Size(780, MinimumHeight);

			// FormBorderStyle = FormBorderStyle.FixedDialog; // no min/max buttons as wanted
			// CenterToScreen();

			Shown += OnShown;

			// TODO: WPF
			/*
			System.Windows.Shell.JumpList jumplist = System.Windows.Shell.JumpList.GetJumpList(System.Windows.Application.Current);
			//System.Windows.Shell.JumpTask task = new System.Windows.Shell.JumpTask();
			System.Windows.Shell.JumpPath jpath = new System.Windows.Shell.JumpPath();
			jpath.Path = cfgpath;
			jumplist.JumpItems.Add(jpath);
			jumplist.Apply();
			*/

			FillLog();

			if (Trace) Log.Verbose("MainWindow constructed");

			ResumeLayout(performLayout: false);
			Visible = true;
		}

		void OnShown(object _, EventArgs _2)
		{
			if (!IsHandleCreated || _Disposed) return;

			Logging.DebugMsg("<Main Window> Showing");

			if (!IsHandleCreated) return;

			ShowLastLog();
		}

		public void ExitRequest(object _, EventArgs _2) => ConfirmExit(restart: false);

		void WindowClose(object _, FormClosingEventArgs ea)
		{
			try
			{
				if (SaveDebugSettings)
				{
					using var dbgcfg = Application.Config.Load(CoreConfigFilename);
					var dbgsec = dbgcfg.Config[HumanReadable.Generic.Debug];
					var logsec = dbgcfg.Config[HumanReadable.Generic.Logging];

					if (ShowInaction) logsec["Show inaction"].Bool = true;
					else logsec.TryRemove("Show inaction");
					if (ShowAgency) logsec["Show agency"].Bool = true;
					else logsec.TryRemove("Show agency");
					if (Process.Manager.DebugProcesses) dbgsec["Procsses"].Bool = true;
					else dbgsec.TryRemove("Processes");
					if (Process.Manager.DebugAdjustDelay) dbgsec["Adjust delay"].Bool = true;
					else dbgsec.TryRemove("Adjust delay");
					if (DebugForeground) dbgsec[HumanReadable.System.Process.Foreground].Bool = true;
					else dbgsec.TryRemove(HumanReadable.System.Process.Foreground);
					if (DebugPower) dbgsec[HumanReadable.Hardware.Power.Section].Bool = true;
					else dbgsec.TryRemove(HumanReadable.Hardware.Power.Section);
					if (globalmodules.netmonitor?.DebugNet ?? false) dbgsec[Network.Constants.Network].Bool = true;
					else dbgsec.TryRemove(Network.Constants.Network);
					if (DebugSession) dbgsec[Constants.Session].Bool = true;
					else dbgsec.TryRemove(Constants.Session);
					if (DebugMonitor) dbgsec[HumanReadable.Hardware.Monitor.Section].Bool = true;
					else dbgsec.TryRemove(HumanReadable.Hardware.Monitor.Section);
					if (DebugAudio) dbgsec[HumanReadable.Hardware.Audio.Section].Bool = true;
					else dbgsec.TryRemove(HumanReadable.Hardware.Audio.Section);
					//if (DebugMemory) dbgsec[HumanReadable.Hardware.Memory].Bool = true;
					//if (DebugCache) dbgsec[Constants.Cache].Bool = true;

					int loglevel = uiloglevelswitch.MinimumLevel switch
					{
						Serilog.Events.LogEventLevel.Verbose => 2,
						Serilog.Events.LogEventLevel.Debug => 1,
						_ => 0,
					};
					logsec[Constants.Verbosity].Int = loglevel;
					Logging.DebugMsg("<Debug> Verbosity: " + loglevel);
				}

				SaveUIState();

				if (!Trace) return;

				Logging.DebugMsg("WindowClose = " + ea.CloseReason.ToString());
				switch (ea.CloseReason)
				{
					case CloseReason.UserClosing:
						// X was pressed or similar
						break;
					case CloseReason.WindowsShutDown:
						Log.Debug("Exit: Windows shutting down.");
						break;
					case CloseReason.TaskManagerClosing:
						Log.Debug("Exit: Task manager told us to close.");
						break;
					case CloseReason.ApplicationExitCall:
						Log.Debug("Exit: User asked to close.");
						break;
					default:
						Log.Debug("Exit: Unidentified close reason: " + ea.CloseReason.ToString());
						break;
				}
				Logging.DebugMsg("WindowClose.Handled");
			}
			catch (Exception ex) { Logging.Stacktrace(ex); }
		}

		void SelfDispose(object _, EventArgs _2) => Dispose(true);

		// this restores the main window to a place where it can be easily found if it's lost
		/// <summary>
		/// Restores the main window to the center of the screen.
		/// </summary>
		public void UnloseWindowRequest()
		{
			if (Trace) Log.Verbose("Making sure main window is not lost.");

			if (!IsHandleCreated || _Disposed) return;

			BeginInvoke(new Action(() =>
			{
				if (!IsHandleCreated || _Disposed) return;

				Reveal(activate: true);
				CenterToScreen();
			}));
		}

		public void Reveal(bool activate = false)
		{
			if (!IsHandleCreated || _Disposed) return;

			BeginInvoke(new Action(() => Reveal_Invoke(activate)));
		}

		void Reveal_Invoke(bool activate)
		{
			if (!IsHandleCreated || _Disposed) return;

			WindowState = FormWindowState.Normal;

			// shuffle to top in the most hackish way possible, these are all unreliable
			// does nothing without show(), unreliable even with it
			if (activate)
			{
				//TopMost = true;
				//TopMost = false;
				Activate();
			}

			Show();
		}

		void ShowLastLog()
		{
			if (!IsHandleCreated || _Disposed) return;

			lock (LogListData_Lock)
			{
				int count;
				try
				{
					EventLogList.VirtualListSize = LogListData.Count;

					//int count = LogList.Items.Count;
					//if (count > 0) LogList.EnsureVisible(count - 1);

					count = EventLogList.Items.Count;
					if (count > 0)
					{
						//var li = LogList.Items[count - 1];
						//li.Focused = true; // does nothing

						EventLogList.TopItem = EventLogList.Items[count - 1]; // triggers RetrieveVirtualItem
					}
				}
				catch
				{
					// ignore, apparently virtualistsize and topitem can randomly throw errors?
				}
			}
		}

		// HOOKS
		Audio.MicManager? micmanager = null;
		StorageManager? storagemanager = null;
		Process.Manager? processmanager = null;
		Process.ForegroundManager? activeappmonitor = null;
		Power.Manager? powermanager = null;
		Hardware.CPUMonitor? cpumonitor = null;
		Network.Manager? netmonitor = null;

		#region Microphone control code
		Audio.Device DefaultAudioInput = null;

		void SetDefaultCommDevice()
		{
			if (!IsHandleCreated || _Disposed) return;

			BeginInvoke(new Action(SetDefaultCommDevice_Invoke));
		}

		void SetDefaultCommDevice_Invoke()
		{
			if (!IsHandleCreated || _Disposed) return;

			try
			{
				// TODO: less direct access to mic manager

				var devname = micmanager.Device.Name;

				AudioInputDevice.Text = !string.IsNullOrEmpty(devname) ? devname : HumanReadable.Generic.NotAvailable;

				corCountLabel.Text = micmanager.Corrections.ToString(CultureInfo.CurrentCulture);

				AudioInputVolume.Maximum = Convert.ToDecimal(Audio.MicManager.Maximum);
				AudioInputVolume.Minimum = Convert.ToDecimal(Audio.MicManager.Minimum);
				AudioInputVolume.Value = Convert.ToInt32(micmanager.Volume);

				AudioInputEnable.SelectedIndex = micmanager.Control ? 0 : 1;
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		void AddAudioInput(Audio.Device device)
		{
			if (micmanager is null) return;

			try
			{
				var li = new ListViewItem(new string[] {
					device.Name,
					device.GUID.ToString(),
					$"{device.Volume * 100d:0.#} %",
					$"{device.Target:0.#} %",
					(device.VolumeControl ? HumanReadable.Generic.Enabled : HumanReadable.Generic.Disabled),
					device.State.ToString(),
				});

				AudioInputs.Items.Add(li);
				MicGuidToAudioInputs.TryAdd(device.GUID, li);
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		void AlternateListviewRowColors(ListView lv, bool alternate = false)
		{
			bool alter = true;
			foreach (ListViewItem li in lv.Items)
				li.BackColor = (alternate && (alter = !alter)) ? AlterColor : DefaultLIBGColor;
		}

		void RemoveAudioInput(Guid guid)
		{
			if (micmanager is null) return;

			if (MicGuidToAudioInputs.TryRemove(guid, out var li))
				AudioInputs.Items.Remove(li);
		}

		void UpdateAudioInputs()
		{
			if (!IsHandleCreated || _Disposed) return;

			if (micmanager is null) return;

			// TODO: mark default device in list
			AudioInputs.Items.Clear();

			foreach (var dev in micmanager.Devices)
				AddAudioInput(dev);

			AlternateListviewRowColors(AudioInputs, AlternateRowColorsDevices);
		}

		Audio.Manager? audiomanager = null;

		public void Hook(Audio.Manager manager)
		{
			Debug.Assert(manager != null);

			try
			{
				audiomanager = manager;
				audiomanager.StateChanged += AudioDeviceStateChanged;
				audiomanager.Removed += AudioDeviceRemoved;
				audiomanager.Added += AudioDeviceAdded;
				audiomanager.OnDisposed += (_, _2) => audiomanager = null;
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				throw;
			}
		}

		public void Hook(Audio.MicManager manager)
		{
			Debug.Assert(manager != null);

			try
			{
				micmanager = manager;
				micmanager.OnDisposed += (_, _2) => micmanager = null;

				if (Trace) Log.Verbose("Hooking microphone monitor.");

				BeginInvoke(new Action(() =>
				{
					if (_Disposed || !IsHandleCreated) return;

					try
					{
						SetDefaultCommDevice();
						UpdateAudioInputs();
					}
					catch (OutOfMemoryException) { throw; }
					catch (Exception ex)
					{
						Logging.Stacktrace(ex);
					}
				}));

				// TODO: Hook all device changes
				micmanager.VolumeChanged += VolumeChangeDetected;
				micmanager.DefaultChanged += MicrophoneDefaultChanged;

				FormClosing += (_, _2) => micmanager.VolumeChanged -= VolumeChangeDetected;
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				throw;
			}
		}

		void AudioDeviceAdded(object sender, Audio.DeviceEventArgs ea)
		{
			if (_Disposed || !IsHandleCreated) return;

			switch (ea.Device.Flow)
			{
				case NAudio.CoreAudioApi.DataFlow.Capture:
					if (micmanager is null) return;
					AddAudioInput(ea.Device);
					AlternateListviewRowColors(AudioInputs, AlternateRowColorsDevices);
					break;
				case NAudio.CoreAudioApi.DataFlow.Render:
					break;
			}
		}

		void AudioDeviceRemoved(object sender, Audio.DeviceEventArgs ea)
		{
			if (_Disposed || !IsHandleCreated) return;

			if (micmanager is null) return;

			RemoveAudioInput(ea.GUID);
			AlternateListviewRowColors(AudioInputs, AlternateRowColorsDevices);
		}

		void MicrophoneDefaultChanged(object sender, Audio.DefaultDeviceEventArgs ea)
		{
			if (!IsHandleCreated || _Disposed) return;

			DefaultAudioInput = ea.Device;

			BeginInvoke(new Action(MicrophoneDefaultChanged_Update));
		}

		void MicrophoneDefaultChanged_Update()
		{
			if (_Disposed || !IsHandleCreated) return;

			try
			{
				if (DefaultAudioInput is null)
				{
					AudioInputEnable.Text = HumanReadable.Generic.Uninitialized;
					AudioInputDevice.Text = HumanReadable.Generic.Uninitialized;
				}
				else
				{
					//AudioInputEnable.SelectedIndex = micmon.Control ? 0 : 1;
					//AudioInputEnable.Text = HumanReadable.Generic.Ellipsis;
					//AudioInputDevice.Text = HumanReadable.Generic.Ellipsis;
					SetDefaultCommDevice();
				}

				UpdateAudioInputs();
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		readonly ConcurrentDictionary<Guid, ListViewItem> MicGuidToAudioInputs = new ConcurrentDictionary<Guid, ListViewItem>();

		void AudioDeviceStateChanged(object sender, Audio.DeviceStateEventArgs ea)
		{
			if (!IsHandleCreated || _Disposed) return;

			BeginInvoke(new Action(() => AudioDeviceStateChanged_Update(ea)));
		}

		void AudioDeviceStateChanged_Update(Audio.DeviceStateEventArgs ea)
		{
			if (_Disposed || !IsHandleCreated) return;

			if (MicGuidToAudioInputs.TryGetValue(ea.GUID, out ListViewItem li))
				li.SubItems[5].Text = ea.State.ToString();
		}

		void UserMicVol(object _, EventArgs _2)
		{
			// TODO: Handle volume changes. Not really needed. Give presets?
			// micMonitor.setVolume(micVol.Value);
		}

		void VolumeChangeDetected(object _, VolumeChangedEventArgs ea)
		{
			if (!IsHandleCreated || _Disposed) return;

			BeginInvoke(new Action(() => VolumeChangeDetected_Update(ea)));
		}

		void VolumeChangeDetected_Update(VolumeChangedEventArgs ea)
		{
			if (!IsHandleCreated || _Disposed) return;

			AudioInputVolume.Value = Convert.ToInt32(ea.New); // this could throw ArgumentOutOfRangeException, but we trust the source
			corCountLabel.Text = ea.Corrections.ToString(CultureInfo.CurrentCulture);
		}
		#endregion // Microphone control code

		public void ProcessTouchEvent(Process.ModificationInfo mi)
		{
			if (!IsHandleCreated || _Disposed) return;

			BeginInvoke(new Action(() => ProcessTouchEvent_Update(mi)));
		}

		void ProcessTouchEvent_Update(Process.ModificationInfo pmi)
		{
			if (!IsHandleCreated || _Disposed) return;

			//adjustcounter.Text = Statistics.TouchCount.ToString();

			var prc = pmi.Info.Controller; // cache

			if (WatchlistMap.TryGetValue(prc, out ListViewItem item))
			{
				item.SubItems[AdjustColumn].Text = prc.Adjusts.ToString(CultureInfo.CurrentCulture);
				// item.SubItems[SeenColumn].Text = prc.LastSeen.ToLocalTime().ToString();
			}
			else
				Log.Error(prc.FriendlyName + " not found in UI watchlist list.");

			if (LastModifiedList)
			{
				try
				{
					var info = pmi.Info;
					var mi = new ListViewItem(new string[] {
							DateTime.Now.ToLongTimeString(),
							info.Name,
							prc.FriendlyName,
							(pmi.PriorityNew.HasValue ? MKAh.Readable.ProcessPriority(pmi.PriorityNew.Value) : HumanReadable.Generic.NotAvailable),
							(pmi.AffinityNew >= 0 ? HumanInterface.BitMask(pmi.AffinityNew, Hardware.Utility.ProcessorCount) : HumanReadable.Generic.NotAvailable),
							info.Path
						});
					lastmodifylist.Items.Add(mi);
					if (lastmodifylist.Items.Count > 5) lastmodifylist.Items.RemoveAt(0);
				}
				catch (Exception ex) { Logging.Stacktrace(ex); }
			}
		}

		public void OnActiveWindowChanged(object _, Process.WindowChangedArgs windowchangeev)
		{
			if (!IsHandleCreated || _Disposed) return;

			if (windowchangeev.Process is null) return;

			BeginInvoke(new Action(() =>
			{
				// int maxlength = 70;
				// string cutstring = e.Title.Substring(0, Math.Min(maxlength, e.Title.Length)) + (e.Title.Length > maxlength ? "..." : "");
				// activeLabel.Text = cutstring;
				activeLabel.Text = windowchangeev.Title;
				activeExec.Text = windowchangeev.Executable;
				//activeFullscreen.Text = windowchangeev.Fullscreen ? "Full" : "Window";
				activeFullscreen.Text = "?";
				activePID.Text = windowchangeev.Id.ToString(CultureInfo.InvariantCulture);
			}));
		}

		public void Hook(StorageManager manager)
		{
			storagemanager = manager;
			storagemanager.TempScan = TempScanStats;
			storagemanager.OnDisposed += (_, _2) => storagemanager = null;
		}

		public void Hook(Process.Manager manager)
		{
			Debug.Assert(manager != null);

			processmanager = manager;
			processmanager.OnDisposed += (_, _2) => processmanager = null;

			processmanager.ProcessStateChange += ExitWaitListHandler;
			if (DebugCache) PathCacheUpdate(null, EventArgs.Empty);

			UpdateTrackingCounter(this, EventArgs.Empty);

			WatchlistRules.VisibleChanged += (_, _2) => { if (WatchlistRules.Visible) WatchlistColor(); };

			if (manager.ScanFrequency.HasValue)
			{
				UItimer.Tick += UpdateRescanCountdown;
				GotFocus += UpdateRescanCountdown;
				UpdateRescanCountdown(this, EventArgs.Empty);
			}

			processmanager.WatchlistSorted += UpdateWatchlist;
			processmanager.ProcessModified += ProcessTouchEvent;

			BeginInvoke(new Action(() =>
			{
				foreach (var prc in processmanager.GetWatchlist())
					AddToWatchlistList(prc);

				WatchlistColor();

				menu_view_loaders.Enabled = processmanager?.LoaderTracking ?? false;

				//

				foreach (var info in processmanager.GetExitWaitList())
					ExitWaitListHandler(info);
			}));
		}

		void UnhookProcessManager()
		{
			processmanager.ProcessModified -= ProcessTouchEvent;
			processmanager.ProcessStateChange -= ExitWaitListHandler;
			processmanager.HandlingStateChange -= ProcessHandlingStateChangeEvent;
		}

		void UpdateWatchlist(object _, EventArgs _2)
		{
			if (!IsHandleCreated || _Disposed) return;

			BeginInvoke(new Action(() =>
			{
				if (!IsHandleCreated || _Disposed) return;

				foreach (var li in WatchlistMap)
				{
					li.Value.SubItems[0].Text = (li.Key.ActualOrder + 1).ToString(CultureInfo.InvariantCulture);
					WatchlistItemColor(li.Value, li.Key);
				}

				// re-sort if user is not interacting?
			}));
		}

		void RescanRequestEvent(object _, EventArgs _2) => processmanager?.HastenScan(TimeSpan.Zero).ConfigureAwait(false);

		void RestartRequestEvent(object sender, EventArgs _2) => ConfirmExit(restart: true, admin: sender == menu_action_restartadmin);

		/// <summary>
		///
		/// </summary>
		/// <remarks>No locks</remarks>
		void WatchlistItemColor(ListViewItem li, Process.Controller prc)
		{
			var alter = AlternateRowColorsWatchlist && (li.Index + 1) % 2 == 0; // every even line

			try
			{
				li.UseItemStyleForSubItems = false;
				foreach (ListViewItem.ListViewSubItem si in li.SubItems)
				{
					si.ForeColor = prc.Enabled ? System.Drawing.SystemColors.ControlText : System.Drawing.SystemColors.GrayText;
					si.BackColor = alter ? AlterColor : DefaultLIBGColor;
				}

				if (prc.PriorityStrategy == Process.PriorityStrategy.Ignore)
					li.SubItems[PrioColumn].ForeColor = System.Drawing.SystemColors.GrayText;

				if (string.IsNullOrEmpty(prc.Path))
					li.SubItems[PathColumn].ForeColor = System.Drawing.SystemColors.GrayText;

				if (prc.PowerPlan == Power.Mode.Undefined)
					li.SubItems[PowerColumn].ForeColor = System.Drawing.SystemColors.GrayText;

				if (prc.AffinityMask < 0)
					li.SubItems[AffColumn].ForeColor = System.Drawing.SystemColors.GrayText;
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		int watchlistcolor_i = 0;

		void WatchlistColor()
		{
			if (Trace) Logging.DebugMsg("COLORING LINES");

			System.Threading.Interlocked.Increment(ref watchlistcolor_i);

			lock (watchlist_lock)
			{
				try
				{
					int i = 0;
					foreach (var item in WatchlistMap)
					{
						if (watchlistcolor_i > 1) return;

						if (Trace) Logging.DebugMsg($"{i++:00} --- {item.Value.Index:00} : {(item.Value.Index + 1) % 2 == 0} --- {item.Key.FriendlyName}");
						WatchlistItemColor(item.Value, item.Key);
					}
				}
				finally
				{
					System.Threading.Interlocked.Decrement(ref watchlistcolor_i);
				}
			}
		}

		void AddToWatchlistList(Process.Controller prc)
		{
			string aff = string.Empty;
			if (prc.AffinityMask > 0)
				aff = AffinityStyle == BitmaskStyle.Bits ? HumanInterface.BitMask(prc.AffinityMask, Hardware.Utility.ProcessorCount) : prc.AffinityMask.ToString(CultureInfo.InvariantCulture);

			var litem = new ListViewItem(new string[] {
				(prc.ActualOrder+1).ToString(CultureInfo.InvariantCulture),
				prc.OrderPreference.ToString(CultureInfo.InvariantCulture),
				prc.FriendlyName,
				prc.Executables.Length > 0 ? string.Join(", ", prc.Executables) : string.Empty,
				string.Empty,
				aff,
				string.Empty,
				prc.Adjusts.ToString(CultureInfo.CurrentCulture),
				string.Empty
			});

			WatchlistRules.Items.Add(litem);
			WatchlistMap.TryAdd(prc, litem);

			FormatWatchlist(litem, prc);
			WatchlistUpdateTooltip(litem, prc);
			WatchlistItemColor(litem, prc);
		}

		static void WatchlistUpdateTooltip(ListViewItem li, Process.Controller prc)
		{
			// BUG: Doens't work for some reason. Gets set but is never shown.
			li.ToolTipText = prc.ToDetailedString();
		}

		void FormatWatchlist(ListViewItem litem, Process.Controller prc)
		{
			if (!IsHandleCreated || _Disposed) return;

			BeginInvoke(new Action(() => FormatWatchlist_Invoke(litem, prc)));
		}

		void FormatWatchlist_Invoke(ListViewItem litem, Process.Controller prc)
		{
			if (!IsHandleCreated || _Disposed) return;

			litem.SubItems[PrefColumn].Text = prc.OrderPreference.ToString(CultureInfo.InvariantCulture);
			litem.SubItems[NameColumn].Text = prc.FriendlyName;
			litem.SubItems[ExeColumn].Text = (prc.Executables.Length > 0) ? string.Join(", ", prc.Executables) : string.Empty;
			litem.SubItems[PrioColumn].Text = prc.Priority.HasValue ? MKAh.Readable.ProcessPriority(prc.Priority.Value) : string.Empty;
			string aff = string.Empty;
			if (prc.AffinityMask >= 0)
			{
				if (prc.AffinityMask == Process.Utility.FullCPUMask || prc.AffinityMask == 0)
					aff = "Full/OS";
				else if (AffinityStyle == 0)
					aff = HumanInterface.BitMask(prc.AffinityMask, Hardware.Utility.ProcessorCount);
				else
					aff = prc.AffinityMask.ToString(CultureInfo.InvariantCulture);
			}
			litem.SubItems[AffColumn].Text = aff;
			litem.SubItems[PowerColumn].Text = (prc.PowerPlan != Power.Mode.Undefined ? Power.Utility.GetModeName(prc.PowerPlan) : string.Empty);
			litem.SubItems[PathColumn].Text = (string.IsNullOrEmpty(prc.Path) ? string.Empty : prc.Path);
		}

		void UpdateWatchlistRule(Process.Controller prc)
		{
			if (!IsHandleCreated || _Disposed) return;

			if (WatchlistMap.TryGetValue(prc, out ListViewItem litem))
				BeginInvoke(new Action(() => UpdateWatchlistRule_Invoke(litem, prc)));
		}

		void UpdateWatchlistRule_Invoke(ListViewItem litem, Process.Controller prc)
		{
			if (!IsHandleCreated || _Disposed) return;

			FormatWatchlist(litem, prc);

			WatchlistUpdateTooltip(litem, prc);

			WatchlistItemColor(litem, prc);
		}

		Extensions.Label AudioInputDevice = null;
		Extensions.NumericUpDownEx AudioInputVolume = null;
		Extensions.ListViewEx AudioInputs = null, WatchlistRules;

		readonly ConcurrentDictionary<Process.Controller, ListViewItem> WatchlistMap = new ConcurrentDictionary<Process.Controller, ListViewItem>();
		readonly object watchlist_lock = new object();

		Extensions.Label corCountLabel = null;
		ComboBox AudioInputEnable = null;

		Extensions.ListViewEx lastmodifylist = null, powerbalancerlog = null;

		Extensions.Label powerbalancer_behaviour = null, powerbalancer_plan = null, powerbalancer_forcedcount = null;

		Extensions.ListViewEx ExitWaitList = null, ProcessingList = null;
		ConcurrentDictionary<int, ListViewItem> ExitWaitlistMap = null;

		#region Foreground Monitor
		Extensions.Label activeLabel = null, activeExec = null, activeFullscreen = null, activePID = null;
		#endregion

		#region Path Cache
		Extensions.Label cacheObjects = null, cacheRatio = null;
		#endregion

		MKAh.Synchronize.Atomic PathCacheUpdateLock = new MKAh.Synchronize.Atomic();

		async void PathCacheUpdate(object _, EventArgs _2)
		{
			if (!IsHandleCreated || _Disposed) return;

			if (!PathCacheUpdateLock.TryLock()) return;
			using var plock = PathCacheUpdateLock.ScopedUnlock();

			await Task.Delay(5_000).ConfigureAwait(false);

			BeginInvoke(new Action(PathCacheUpdate_Invoke)); // required after task.delay
		}

		void PathCacheUpdate_Invoke()
		{
			cacheObjects.Text = Statistics.PathCacheCurrent.ToString(CultureInfo.CurrentCulture);
			double ratio = (Statistics.PathCacheMisses > 0 ? ((double)Statistics.PathCacheHits / (double)Statistics.PathCacheMisses) : 1d);
			cacheRatio.Text = ratio <= 99.99f ? $"{ratio:0.##}" : ">99.99"; // let's just not overflow the UI
		}

		// BackColor = System.Drawing.Color.LightGoldenrodYellow
		Extensions.Label netstatuslabel, inetstatuslabel, uptimestatuslabel, uptimeMeanLabel, netTransmit, netQueue;

		internal static Serilog.Core.LoggingLevelSwitch LogIncludeLevel;

		public int UIUpdateFrequency
		{
			get => UItimer.Interval;
			set => UItimer.Interval = value.Constrain(100, 5000);
		}

		public void SetUIUpdateFrequency(int freq) => UIUpdateFrequency = freq;

		readonly System.Windows.Forms.Timer UItimer = new System.Windows.Forms.Timer();

		void StartUIUpdates(object sender, EventArgs _2)
		{
			if (_Disposed || !IsHandleCreated) StopUIUpdates(this, EventArgs.Empty);
			else if (!UItimer.Enabled)
			{
				UpdateMemoryStats(sender, EventArgs.Empty);
				UpdateHealthMon(sender, EventArgs.Empty);
				UpdateNetwork(sender, EventArgs.Empty);
				UItimer.Start();
			}
		}

		void StopUIUpdates(object _, EventArgs _2)
		{
			if (UItimer.Enabled) UItimer.Stop();
		}

		void Cleanup(object _, EventArgs _2)
		{
			if (!IsHandleCreated) return;

			if (LastCauseTime.To(DateTimeOffset.UtcNow).TotalMinutes >= 3d)
			{
				pwcause.Text = HumanReadable.Generic.NotAvailable;
			}
		}

		void UpdateRescanCountdown(object _, EventArgs _2)
		{
			if (_Disposed || !IsHandleCreated) return;

			if (processmanager is null) return; // not yet assigned

			// Rescan Countdown
			if (processmanager.ScanFrequency.HasValue)
				processingtimer.Text = $"{DateTimeOffset.UtcNow.To(processmanager.NextScan).TotalSeconds:N0}s";
			else
				processingtimer.Text = HumanReadable.Generic.NotAvailable;
		}

		void UpdateNetwork(object _, EventArgs _2)
		{
			if (_Disposed || !IsHandleCreated) return;

			if (netmonitor is null) return;

			uptimestatuslabel.Text = HumanInterface.TimeString(netmonitor.Uptime);
			var mean = netmonitor.UptimeMean();
			uptimeMeanLabel.Text = double.IsInfinity(mean) ? "Infinite" : HumanInterface.TimeString(TimeSpan.FromMinutes(mean));

			var delta = netmonitor.GetTraffic;
			//float netTotal = delta.Input + delta.Output;
			netTransmit.Text = $"{delta.Input / 1000:0.#} kB In, {delta.Output / 1000:0.#} kB Out [{delta.Packets:N0} packets; {delta.Queue:N0} queued]";
		}

		Extensions.ListViewEx NetworkDevices = null;

		ContextMenuStrip ifacems, watchlistms;
		ToolStripMenuItem watchlistenable, watchlistadd;

		void InterfaceContextMenuOpen(object _, EventArgs _2)
		{
			try
			{
				foreach (ToolStripItem msi in ifacems.Items)
					msi.Enabled = (NetworkDevices.SelectedItems.Count == 1);
			}
			catch { } // discard
		}

		void CopyIPv4AddressToClipboard(object _, EventArgs _2)
		{
			if (NetworkDevices.SelectedItems.Count == 1)
				Clipboard.SetText(NetworkDevices.SelectedItems[0].SubItems[IPv4Column].Text, TextDataFormat.UnicodeText);
		}

		void CopyIPv6AddressToClipboard(object _, EventArgs _2)
		{
			if (NetworkDevices.SelectedItems.Count == 1)
				Clipboard.SetText($"[{NetworkDevices.SelectedItems[0].SubItems[IPv6Column].Text}]", TextDataFormat.UnicodeText);
		}

		void CopyIfaceToClipboard(object _, EventArgs _2)
		{
			if (NetworkDevices.SelectedItems.Count == 1)
				Clipboard.SetText(netmonitor.GetDeviceData(NetworkDevices.SelectedItems[0].SubItems[0].Text), TextDataFormat.UnicodeText);
		}

		void CopyLogToClipboard(object _, EventArgs _2)
		{
			var selected = EventLogList.SelectedIndices;
			if (selected.Count == 0) return;

			var sbs = new StringBuilder(256);

			foreach (int item in selected)
				sbs.Append(EventLogList.Items[item].SubItems[0].Text);

			Clipboard.SetText(sbs.ToString(), TextDataFormat.UnicodeText);
		}

		readonly Extensions.TabControl TabPages;
		readonly Extensions.TabPage infoTab, watchTab;
		// Optional tabs
		Extensions.TabPage? micTab = null, powerDebugTab = null, ProcessDebugTab = null;

		// TODO: Easier column access somehow than this?
		//int OrderColumn = 0;
		const int PrefColumn = 1, NameColumn = 2, ExeColumn = 3, PrioColumn = 4, AffColumn = 5, PowerColumn = 6, AdjustColumn = 7, PathColumn = 8;

		readonly ToolStripMenuItem
			menu_debug, menu_debug_loglevel,
			menu_debug_inaction, menu_debug_agency, menu_debug_scanning,
#if DEBUG
			menu_debug_loglevel_trace,
#endif
			menu_debug_loglevel_info,
			menu_debug_loglevel_debug,
			menu_debug_keepsettings;

		void EnsureVerbosityLevel()
		{
			if (LogIncludeLevel.MinimumLevel == Serilog.Events.LogEventLevel.Information)
				LogIncludeLevel.MinimumLevel = Serilog.Events.LogEventLevel.Debug;

			UpdateLogLevelSelection();
		}

		void UpdateLogLevelSelection()
		{
			var level = LogIncludeLevel.MinimumLevel;

			menu_debug_loglevel_info.Checked = (level == Serilog.Events.LogEventLevel.Information);
			menu_debug_loglevel_debug.Checked = (level == Serilog.Events.LogEventLevel.Debug);
#if DEBUG
			menu_debug_loglevel_trace.Checked = (level == Serilog.Events.LogEventLevel.Verbose);
#else
			menu_debug_loglevel_trace.Checked = false;
#endif
		}

		int MinimumHeight = 0;
		//int MinimumWidth = 0;

		ToolStripMenuItem menu_action_restartadmin = null;

		const string UpdateFrequencyName = "Update frequency",
			TopmostName = "Topmost",
			InfoName = "Info",
			TraceName = "Trace",
			ShowUnmodifiedPortionsName = "Unmodified portions",
			WatchlistName = "Watchlist";

		ToolStripMenuItem power_auto, power_highperf, power_balanced, power_saving, power_manual;

		void LogListSelectionChanged(object sender, ListViewVirtualItemsSelectionRangeChangedEventArgs e)
		{
			/*
			if (e.IsSelected)
			{
				// e.StartIndex to e.EndIndex
			}
			*/
		}

		void LogListSearchItem(object sender, SearchForVirtualItemEventArgs e)
		{
			// for
			//LogList.FindItemWithText
			//LogList.FindNearestItem
			if (e.IsTextSearch)
			{

			}
			else
			{

			}
		}

		void VirtualModeNoAction(object sender, EventArgs e)
		{

		}

		int LogListFirst = 0, ErrorListFirst = 0;

		readonly object LogListData_Lock = new object();

		readonly List<LogEventArgs> LogListData = new List<LogEventArgs>(200);
		readonly MKAh.Cache.SimpleCache<ulong, ListViewItem> LogListCache = new MKAh.Cache.SimpleCache<ulong, ListViewItem>(200, 50);

		void LogListCacheItem(object sender, CacheVirtualItemsEventArgs e)
		{
			// Confirm necessity of update
			lock (LogListData_Lock)
			{
				if (e.StartIndex >= LogListFirst && e.EndIndex <= LogListFirst + LogListData.Count)
					return; // Subset of old cache

				// Build cache
				LogListFirst = e.StartIndex;
				int newVisibleLength = e.EndIndex - e.StartIndex + 1; // inclusive range

				//Fill the cache with the appropriate ListViewItems.
				for (int i = 0; i < newVisibleLength; i++)
				{
					var item = LogListData[i];

					if (!LogListCache.Get(item.ID, out _))
						LogListGenerateItem(item);
				}
			}
			//LogList.VirtualListSize = LogListData.Count;
		}

		void LogListRetrieveItem(object sender, RetrieveVirtualItemEventArgs e)
		{
			LogEventArgs ev;

			int index = e.ItemIndex - LogListFirst;

			lock (LogListData_Lock)
			{
				int count = LogListData.Count;

				try
				{
					if (index >= 0 && index < count)
					{
						ev = LogListData[index];

						if (LogListCache.Get(ev.ID, out var li))
						{
							e.Item = li;
							return;
						}
					}
					else
						ev = LogListData.Last();

					if (e.Item is null)
						e.Item = LogListGenerateItem(ev);
				}
				catch (ArgumentOutOfRangeException ex)
				{
					Logging.Stacktrace(ex);
					Log.Error("<UI> Log retrieve item – index (" + index.ToString(CultureInfo.InvariantCulture) + ") out of range – items: " + count.ToString(CultureInfo.InvariantCulture));
				}
				catch (Exception ex)
				{
					Logging.Stacktrace(ex);
				}
			}
		}

		ListViewItem LogListGenerateItem(LogEventArgs ea)
		{
			var li = new ListViewItem(ea.Message);

			switch (ea.Level)
			{
				case Serilog.Events.LogEventLevel.Verbose:
				case Serilog.Events.LogEventLevel.Information:
					li.ImageIndex = 0;
					break;
				case Serilog.Events.LogEventLevel.Debug:
				case Serilog.Events.LogEventLevel.Warning:
					li.ImageIndex = 1;
					break;
				case Serilog.Events.LogEventLevel.Error:
				case Serilog.Events.LogEventLevel.Fatal:
					li.ImageIndex = 2;
					break;
			}

			// color errors and worse red
			if ((int)ea.Level >= (int)Serilog.Events.LogEventLevel.Error)
				li.ForeColor = System.Drawing.Color.Red;

			// alternate back color

			if (AlternateRowColorsLog && ea.ID % 2 == 0)
				li.BackColor = AlterColor;

			LogListCache.Add(ea.ID, li);

			return li;
		}

		void UpdateTrackingCounter(object _, EventArgs _2)
		{
			if (_Disposed || !IsHandleCreated) return;

			processingcount.Text = processmanager?.HandlingCount.ToString(CultureInfo.CurrentCulture) ?? "n/a";
			trackingcount.Text = processmanager?.RunningCount.ToString(CultureInfo.CurrentCulture) ?? "n/a";
		}

		void ResizeLogList(object sender, EventArgs ev)
		{
			EventLogMainColumn.Width = -2;
			EventLogMainColumn.Width -= 2; // HACK: Enable visual styles causes horizontal bar to always be present without the following.

			EventLogList.Height = ClientSize.Height - TabPages.Height - statusbar.Height - MenuToolbar.Height;

			LogPanel.Height = ClientSize.Height - TabPages.Height - statusbar.Height - MenuToolbar.Height;

			//LogSearch.Anchor = AnchorStyles.Top | AnchorStyles.Right;

			ShowLastLog();
		}

		void OpenChangelog(object sender, EventArgs e)
		{
			using var changelog = new ChangeLog("test");
			changelog.ShowDialog();
		}

		void SetAutoPower(object _, EventArgs _2)
			=> powermanager.SetBehaviour(powermanager.Behaviour != Power.PowerBehaviour.Auto ? Power.PowerBehaviour.Auto : Power.PowerBehaviour.RuleBased);

		void SetManualPower(object _, EventArgs _2)
			=> powermanager.SetBehaviour(powermanager.Behaviour != Power.PowerBehaviour.Manual ? Power.PowerBehaviour.Manual : Power.PowerBehaviour.RuleBased);

		void HighlightPowerMode()
		{
			power_saving.Checked = false;
			power_balanced.Checked = false;
			power_highperf.Checked = false;

			switch (powermanager.CurrentMode)
			{
				case Power.Mode.Balanced:
					power_balanced.Checked = true;
					break;
				case Power.Mode.HighPerformance:
					power_highperf.Checked = true;
					break;
				case Power.Mode.PowerSaver:
					power_saving.Checked = true;
					break;
			}
		}

		void SetPower(Power.Mode mode)
		{
			try
			{
				if (DebugPower) Log.Debug("<Power> Setting behaviour to manual.");

				powermanager.SetBehaviour(Power.PowerBehaviour.Manual);

				if (DebugPower) Log.Debug("<Power> Setting manual mode: " + mode.ToString());

				// powermanager.Restore(0).Wait(); // already called by setBehaviour as necessary
				powermanager?.SetMode(mode, new Cause(OriginType.User));

				// powermanager.RequestMode(mode);
				HighlightPowerMode();
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		void ShowExternalLicenses(object sender, EventArgs e)
			=> MessageBox.ShowModal("Third Party Licenses for " + Application.Name,
				Properties.Resources.ExternalLicenses,
				MessageBox.Buttons.OK, MessageBox.Type.Rich, parent: this);

		void ShowVolumeBox(object sender, EventArgs e) => BuildVolumeMeter(modules);

		public static void BuildVolumeMeter(ModuleManager modules)
		{
			if (!AudioManagerEnabled) return;

			lock (window_creation_lock)
			{
				if (modules.volumemeter is null)
				{
					modules.volumemeter = new UI.VolumeMeter(modules.audiomanager);
					modules.volumemeter.OnDisposed += (_, _2) => modules.volumemeter = null;
				}
			}
		}

		void ShowLoaderBox(object sender, EventArgs e) => BuildLoaderBox(modules);

		public static void BuildLoaderBox(ModuleManager modules)
		{
			if (modules.loaderdisplay is null)
			{
				modules.loaderdisplay = new UI.LoaderDisplay(modules);
				modules.loaderdisplay.OnDisposed += (_, _2) => modules.loaderdisplay = null;

				modules.processmanager?.GenerateLoadTrackers();
			}
		}

		void ToolStripMenuAutoOpen(object sender, EventArgs _)
		{
			if (!ContainsFocus || !AutoOpenMenus) return;
			(sender as ToolStripMenuItem)?.ShowDropDown();
		}

		static void OpenLicenseDialog()
		{
			try
			{
				using var n = new LicenseDialog(initial: false);
				n.ShowDialog();
			}
			catch (Exception ex) { Logging.Stacktrace(ex); }
		}

		void BuildWatchlist(int[] appwidths, int[] apporder)
		{
			WatchlistRules = new Extensions.ListViewEx
			{
				Parent = this,
				View = View.Details,
				Dock = DockStyle.Fill,
				AutoSize = true,
				FullRowSelect = true,
				MinimumSize = new System.Drawing.Size(-2, -2),
				AllowColumnReorder = true,
				ShowItemToolTips = true,
			};

			WatchlistRules.KeyPress += WatchlistRulesKeyboardSearch;

			var numberColumns = new int[] { 0, AdjustColumn };
			var watchlistSorter = new WatchlistSorter(numberColumns, PrioColumn, PowerColumn);

			WatchlistRules.ListViewItemSorter = watchlistSorter; // what's the point of this?

			WatchlistRules.ColumnClick += (_, ea) =>
			{
				if (watchlistSorter.Column == ea.Column)
				{
					// flip order
					watchlistSorter.Order = watchlistSorter.Order == SortOrder.Ascending ? SortOrder.Descending : SortOrder.Ascending;
				}
				else
				{
					watchlistSorter.Order = SortOrder.Ascending;
					watchlistSorter.Column = ea.Column;
				}

				// deadlock if locked while adding
				WatchlistRules.Sort();
				WatchlistColor();
			};

			watchlistms = new ContextMenuStrip();
			watchlistms.Opened += WatchlistContextMenuOpen;
			watchlistenable = new ToolStripMenuItem(HumanReadable.Generic.Enabled, null, EnableWatchlistRule);
			var watchlistedit = new ToolStripMenuItem("Edit", null, EditWatchlistRule);
			watchlistadd = new ToolStripMenuItem("Create new", null, AddWatchlistRule);
			var watchlistdel = new ToolStripMenuItem("Remove", null, DeleteWatchlistRule);
			var watchlistclip = new ToolStripMenuItem("Copy to clipboard", null, CopyRuleToClipboard);

			watchlistms.Items.AddRange(new ToolStripItem[] {
				watchlistenable,
				new ToolStripSeparator(),
				watchlistedit,
				watchlistadd,
				new ToolStripSeparator(),
				watchlistdel,
				new ToolStripSeparator(),
				watchlistclip
			});

			WatchlistRules.ContextMenuStrip = watchlistms;

			WatchlistRules.Columns.Add("#", appwidths[0]);
			WatchlistRules.Columns.Add("Pref.", appwidths[PrefColumn]);
			WatchlistRules.Columns.Add(Application.Constants.Name, appwidths[NameColumn]);
			WatchlistRules.Columns.Add(HumanReadable.System.Process.Executable, appwidths[ExeColumn]);
			WatchlistRules.Columns.Add(HumanReadable.System.Process.Priority, appwidths[PrioColumn]);
			WatchlistRules.Columns.Add(HumanReadable.System.Process.Affinity, appwidths[AffColumn]);
			WatchlistRules.Columns.Add(HumanReadable.Hardware.Power.Plan, appwidths[PowerColumn]);
			WatchlistRules.Columns.Add("Adjusts", appwidths[AdjustColumn]);
			WatchlistRules.Columns.Add(HumanReadable.System.Process.Path, appwidths[PathColumn]);

			for (int i = 0; i < 8; i++)
				WatchlistRules.Columns[i].DisplayIndex = apporder[i];

			WatchlistRules.Scrollable = true;
			WatchlistRules.Alignment = ListViewAlignment.Left;

			WatchlistRules.DoubleClick += EditWatchlistRule; // for in-app editing

			watchTab.Controls.Add(WatchlistRules);
		}

		void LoadUIConfiguration(out int opentab, out int[] appwidths, out int[] apporder, out int[] micwidths, out int[] ifacewidths)
		{
			using var uicfg = Application.Config.Load(UIConfigFilename);

			var wincfg = uicfg.Config[Constants.Windows];
			var colcfg = uicfg.Config[Constants.Columns];
			var gencfg = uicfg.Config[Constants.Visuals];

			opentab = uicfg.Config[Constants.Tabs].Get(Constants.Open)?.Int ?? 0;
			appwidths = null;
			var appwidthsDefault = new int[] { 20, 20, 120, 140, 82, 60, 76, 46, 160 };
			appwidths = colcfg.GetOrSet(Constants.Apps, appwidthsDefault).IntArray;
			if (appwidths.Length != appwidthsDefault.Length) appwidths = appwidthsDefault;

			var appOrderDefault = new int[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 };
			apporder = colcfg.GetOrSet(Constants.AppOrder, appOrderDefault).IntArray;
			var unqorder = new HashSet<int>(appOrderDefault.Length);
			foreach (var i in apporder) unqorder.Add(i);
			if (unqorder.Count != appOrderDefault.Length || unqorder.Max() != 7 || unqorder.Min() != 0) apporder = appOrderDefault;

			micwidths = null;
			if (MicrophoneManagerEnabled)
			{
				int[] micwidthsDefault = new int[] { 200, 220, 60, 60, 60, 120 };
				micwidths = colcfg.GetOrSet(Constants.Mics, micwidthsDefault).IntArray;
				if (micwidths.Length != micwidthsDefault.Length) micwidths = micwidthsDefault;
			}

			ifacewidths = null;
			if (NetworkMonitorEnabled)
			{
				int[] ifacewidthsDefault = new int[] { 100, 60, 90, 72, 180, 72, 60, 60, 40 };
				ifacewidths = colcfg.GetOrSet(Constants.Interfaces, ifacewidthsDefault).IntArray;
				if (ifacewidths.Length != ifacewidthsDefault.Length) ifacewidths = ifacewidthsDefault;
			}

			int[] winpos = wincfg.Get(Constants.Main)?.IntArray ?? null;
			if (winpos?.Length == 4)
			{
				StartPosition = FormStartPosition.Manual;
				Bounds = new System.Drawing.Rectangle(winpos[0], winpos[1], winpos[2], winpos[3]);

				if (!Screen.AllScreens.Any(screen => screen.Bounds.IntersectsWith(Bounds)))
					CenterToParent();
			}

			//var alternateRowColor = gencfg.GetSetDefault("Alternate row color", new[] { 1 }, out modified).IntArray;

			AutocalcAlterColor();

			WarningColor = System.Drawing.Color.Red; // no decent way to autocalculate good warning color in case it blends with background

			//GrayText = System.Drawing.Color.FromArgb(130, 130, 130); // ignores user styles
			//AlterColor = System.Drawing.Color.FromArgb(245, 245, 245); // ignores user styles

			AlternateRowColorsDevices = gencfg.GetOrSet("Alternate device row colors", false).Bool;
			AlternateRowColorsWatchlist = gencfg.GetOrSet("Alternate watchlist row colors", true).Bool;
			AlternateRowColorsLog = gencfg.GetOrSet("Alternate log row colors", true).Bool;
		}

		void AutocalcAlterColor()
		{
			var defcolor = new ListViewItem().BackColor; // HACK; gets current color scheme default color

			int red = defcolor.R, green = defcolor.G, blue = defcolor.B;

			//int totalRGB = blue + green + red;
			//int highest = Math.Max(Math.Max(blue, green), red);
			int lowest = Math.Min(Math.Min(blue, green), red);

			if (lowest > 200) // bright = darken
			{
				red = (red - Math.Max(Convert.ToInt32(red * 0.04), 6)).Constrain(0, 255);
				green = (green - Math.Max(Convert.ToInt32(green * 0.04), 6)).Constrain(0, 255);
				blue = (blue - Math.Max(Convert.ToInt32(blue * 0.04), 6)).Constrain(0, 255);
				AlterColor = System.Drawing.Color.FromArgb(red, green, blue);
			}
			else // dark/midtone = brighten
			{
				red = (red + Math.Max(Convert.ToInt32(red * 0.04), 6)).Constrain(0, 255);
				green = (green + Math.Max(Convert.ToInt32(green * 0.04), 6)).Constrain(0, 255);
				blue = (blue + Math.Max(Convert.ToInt32(blue * 0.04), 6)).Constrain(0, 255);
				AlterColor = System.Drawing.Color.FromArgb(red, green, blue);
			}

			//Logging.DebugMsg($"ALTER COLOR: {AlterColor.R}, {AlterColor.G}, {AlterColor.B}");
		}

		void BuildPowerDebugPanel()
		{
			var powerlayout = new Extensions.TableLayoutPanel()
			{
				ColumnCount = 1,
				AutoSize = true,
				Dock = DockStyle.Fill
			};
			powerlayout.Controls.Add(new Extensions.Label() { Text = "Power mode autobalancing tracker...", Padding = BigPadding });

			powerbalancerlog = new Extensions.ListViewEx()
			{
				Parent = this,
				Dock = DockStyle.Top,
				AutoSize = true,
				MinimumSize = new System.Drawing.Size(-2, 180),
				FullRowSelect = true,
				View = View.Details,
			};

			powerbalancerlog.Columns.Add("Current", 60);
			powerbalancerlog.Columns.Add("Mean", 60);
			powerbalancerlog.Columns.Add("High", 60);
			powerbalancerlog.Columns.Add("Low", 60);
			powerbalancerlog.Columns.Add("Reaction", 80);
			powerbalancerlog.Columns.Add("Reactionary Plan", 120);
			powerbalancerlog.Columns.Add("Enacted", 60);
			powerbalancerlog.Columns.Add("Pressure", 60);

			powerlayout.Controls.Add(powerbalancerlog);

			var powerbalancerstatus = new FlowLayoutPanel()
			{
				FlowDirection = FlowDirection.LeftToRight,
				WrapContents = false,
				AutoSize = true,
				Dock = DockStyle.Top
			};
			powerbalancer_behaviour = new Extensions.Label() { Text = HumanReadable.Generic.Uninitialized };
			powerbalancer_plan = new Extensions.Label() { Text = HumanReadable.Generic.Uninitialized };
			powerbalancer_forcedcount = new Extensions.Label() { Text = HumanReadable.Generic.Uninitialized };

			powerbalancerstatus.Controls.AddRange(new Control[] {
				new Extensions.Label() { Text = "Behaviour:" },
				powerbalancer_behaviour,
				new Extensions.Label() { Text = "| Plan:" },
				powerbalancer_plan,
				new Extensions.Label() { Text = "Forced by:" },
				powerbalancer_forcedcount,
			});

			powerlayout.Controls.Add(powerbalancerstatus);

			powerDebugTab = new Extensions.TabPage("Power Debug") { Padding = BigPadding };
			powerDebugTab.Controls.Add(powerlayout);
			TabPages.Controls.Add(powerDebugTab);
		}

		Extensions.TableLayoutPanel BuildLastModifiedPanel(int[] appwidths)
		{
			var lastmodifypanel = new Extensions.TableLayoutPanel
			{
				Dock = DockStyle.Top,
				ColumnCount = 1,
				Height = 40,
				AutoSize = true
			};
			lastmodifypanel.Controls.Add(new Extensions.Label() { Text = "Last process modifications" });
			lastmodifylist = new Extensions.ListViewEx()
			{
				Parent = this,
				Dock = DockStyle.Top,
				AutoSize = true,
				View = View.Details,
				FullRowSelect = true,
				HeaderStyle = ColumnHeaderStyle.Nonclickable,
				//Scrollable = true,
				MinimumSize = new System.Drawing.Size(-2, 60),
				//MinimumSize = new System.Drawing.Size(-2, -2), // doesn't work
			};

			lastmodifylist.Columns.Add("Time", 60);
			lastmodifylist.Columns.Add(HumanReadable.System.Process.Executable, appwidths[2]);
			lastmodifylist.Columns.Add("Rule", appwidths[1]);
			lastmodifylist.Columns.Add(HumanReadable.System.Process.Priority, appwidths[3]);
			lastmodifylist.Columns.Add(HumanReadable.System.Process.Affinity, appwidths[4]);
			lastmodifylist.Columns.Add(HumanReadable.System.Process.Path, -2);

			lastmodifypanel.Controls.Add(lastmodifylist);
			var lastmodifyms = new ContextMenuStrip();
			var lastmodifycopy = new ToolStripMenuItem("Copy path to clipboard", null, (_, _2) =>
			{
				if (lastmodifylist.SelectedItems.Count > 0)
				{
					string path = lastmodifylist.SelectedItems[0].SubItems[5].Text;
					if (!string.IsNullOrEmpty(path))
						Clipboard.SetText(path, TextDataFormat.UnicodeText);
				}
			});
			lastmodifyms.Opened += (_, _2) => lastmodifycopy.Enabled = (lastmodifylist.SelectedItems.Count == 1);
			lastmodifyms.Items.Add(lastmodifycopy);
			lastmodifylist.ContextMenuStrip = lastmodifyms;
			return lastmodifypanel;
		}

		void BuildPowerPanel(out TableLayoutPanel powerpanel)
		{
			powerpanel = new Extensions.TableLayoutPanel()
			{
				ColumnCount = 2,
				AutoSize = true,
				AutoSizeMode = AutoSizeMode.GrowOnly,
				Dock = DockStyle.Fill,
			};

			pwmode = new Extensions.Label { Text = HumanReadable.Generic.Uninitialized };
			pwcause = new Extensions.Label { Text = HumanReadable.Generic.Uninitialized };
			pwbehaviour = new Extensions.Label { Text = HumanReadable.Generic.Uninitialized };

			powerpanel.Controls.AddRange(new Control[] {
				new Extensions.Label { Text = "Behaviour:" },
				pwbehaviour,
				new Extensions.Label { Text = "Mode:" },
				pwmode,
				new Extensions.Label { Text = "Cause:" },
				pwcause,
			});
		}

		void BuildNVMPanel(out TableLayoutPanel nvmpanel)
		{
			nvmpanel = new Extensions.TableLayoutPanel()
			{
				ColumnCount = 2,
				AutoSize = true,
				AutoSizeMode = AutoSizeMode.GrowOnly,
				Dock = DockStyle.Fill,
			};

			nvmtransferslabel = new Extensions.Label { Text = HumanReadable.Generic.Uninitialized };
			nvmsplitio = new Extensions.Label { Text = HumanReadable.Generic.Uninitialized };
			nvmdelaylabel = new Extensions.Label { Text = HumanReadable.Generic.Uninitialized };
			nvmqueuelabel = new Extensions.Label { Text = HumanReadable.Generic.Uninitialized };
			//hardfaults = new Label { Text = HumanReadable.Generic.Uninitialized, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Left };

			nvmpanel.Controls.AddRange(new Control[] {
				new Extensions.Label { Text = "Transfers" },
				nvmtransferslabel,
				new Extensions.Label { Text = "Split I/O" },
				nvmsplitio,
				new Extensions.Label { Text = "Delay" },
				nvmdelaylabel,
				new Extensions.Label { Text = "Queued" },
				nvmqueuelabel,
				//new Label { Text = "Hard faults", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Dock = DockStyle.Left },
				//hardfaults,
			});
		}

		Extensions.TableLayoutPanel BuildNetworkStatusUI(FlowLayoutPanel infopanel, int[] ifacewidths)
		{
			netstatuslabel = new Extensions.Label() { Text = HumanReadable.Generic.Uninitialized };
			inetstatuslabel = new Extensions.Label() { Text = HumanReadable.Generic.Uninitialized };
			uptimeMeanLabel = new Extensions.Label() { Text = HumanReadable.Generic.Uninitialized };
			netTransmit = new Extensions.Label() { Text = HumanReadable.Generic.Uninitialized };
			netQueue = new Extensions.Label() { Text = HumanReadable.Generic.Uninitialized };
			uptimestatuslabel = new Extensions.Label { Text = HumanReadable.Generic.Uninitialized };

			var netstatus = new Extensions.TableLayoutPanel
			{
				ColumnCount = 6,
				RowCount = 1,
				Dock = DockStyle.Top,
				AutoSize = true,
			};

			netstatus.Controls.AddRange(new Control[] {
				// first row
				new Extensions.Label() { Text = "Network" },
				netstatuslabel,
				new Extensions.Label() { Text = "Uptime" },
				uptimestatuslabel,
				new Extensions.Label() { Text = "Transmission" },
				netTransmit,
				// second row
				new Extensions.Label() { Text = "Internet" },
				inetstatuslabel,
				new Extensions.Label { Text = "Average" },
				uptimeMeanLabel,
				//new Label() { Text = "??", Dock = DockStyle.Left, AutoSize = true, TextAlign = System.Drawing.ContentAlignment.MiddleLeft },
				//netQueue,
			});

			NetworkDevices = new Extensions.ListViewEx
			{
				AutoSize = true,
				MinimumSize = new System.Drawing.Size(-2, 40),
				View = View.Details,
				FullRowSelect = true,
				Height = 64,
			};

			infopanel.SizeChanged += (_, _2) => NetworkDevices.Width = infopanel.ClientSize.Width - infopanel.Padding.Horizontal - infopanel.Margin.Vertical;

			ifacems = new ContextMenuStrip();
			ifacems.Opened += InterfaceContextMenuOpen;
			var ifaceip4copy = new ToolStripMenuItem("Copy IPv4 address", null, CopyIPv4AddressToClipboard);
			var ifaceip6copy = new ToolStripMenuItem("Copy IPv6 address", null, CopyIPv6AddressToClipboard);
			var ifacecopy = new ToolStripMenuItem("Copy full information", null, CopyIfaceToClipboard);
			ifacems.Items.Add(ifaceip4copy);
			ifacems.Items.Add(ifaceip6copy);
			ifacems.Items.Add(ifacecopy);
			NetworkDevices.ContextMenuStrip = ifacems;

			NetworkDevices.Columns.Add("Device", ifacewidths[0]); // 0
			NetworkDevices.Columns.Add("Type", ifacewidths[1]); // 1
			NetworkDevices.Columns.Add("IPv4", ifacewidths[2]); // 4
			NetworkDevices.Columns.Add("IPv4 Status", ifacewidths[3]); // 2
			NetworkDevices.Columns.Add("IPv6", ifacewidths[4]); // 5
			NetworkDevices.Columns.Add("IPv6 Status", ifacewidths[5]); // 2
			NetworkDevices.Columns.Add("Packet Δ", ifacewidths[6]); // 6
			NetworkDevices.Columns.Add("Error Δ", ifacewidths[7]); // 7
			NetworkDevices.Columns.Add("Errors", ifacewidths[8]); // 8

			IPv4Column = 2;
			IPv6Column = 4;

			PacketDeltaColumn = 6;
			ErrorDeltaColumn = 7;
			ErrorTotalColumn = 8;

			NetworkDevices.Scrollable = true;

			netstatus.RowStyles.Add(new RowStyle(SizeType.AutoSize, 32));

			return netstatus;
		}

		static void ShowExperimentConfig(object sender, EventArgs ea) => Config.ExperimentConfig.Reveal();

		void ShowAboutDialog(object sender, EventArgs ea)
		{
			var builddate = BuildDate();

			var now = DateTime.UtcNow;
			var age = (now - builddate).TotalDays;

			var sbs = new StringBuilder(1024)
				.AppendLine(Application.Name)
				.Append("Version: ").AppendLine(Version)
				.Append("Built: ").Append(builddate.ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture)).Append(" [").AppendFormat(CultureInfo.InvariantCulture, "{0:N0}", age).AppendLine(" days old]")
				.AppendLine()
				.AppendLine("Created by M.A., 2016–2019")
				.AppendLine()
				.Append("At Github: ").AppendLine(GitURL.ToString())
				.Append("At Itch.io: ").AppendLine(ItchURL.ToString())
				.AppendLine()
				.AppendLine("Free system maintenance and de-obnoxifying app.")
				.AppendLine()
				.AppendLine("Available under MIT license.");

			MessageBox.ShowModal("About " + Application.Name + "!", sbs.ToString(), MessageBox.Buttons.OK, parent: this);
		}

		readonly Stopwatch WatchlistSearchInputTimer = new Stopwatch();
		readonly System.Windows.Forms.Timer WatchlistSearchTimer = new System.Windows.Forms.Timer();
		string SearchString = string.Empty;

		void WatchlistRulesKeyboardSearch(object _, KeyPressEventArgs ea)
		{
			//bool ctrlchar = char.IsControl(ea.KeyChar);

			// RESET
			if (WatchlistSearchInputTimer.ElapsedMilliseconds > 2_700) // previous input too long ago
				SearchString = string.Empty;

			if (string.IsNullOrEmpty(SearchString)) // catches above and initial state
				WatchlistSearchTimer.Start();

			WatchlistSearchInputTimer.Restart();

			if (Trace) Logging.DebugMsg($"INPUT: {((int)ea.KeyChar):X}");

			if (char.IsControl(ea.KeyChar))
			{
				if (Trace) Logging.DebugMsg("CONTROL CHARACTER!");

				if (ea.KeyChar == (char)Keys.Back && SearchString.Length > 0) // BACKSPACE
					SearchString = SearchString.Remove(SearchString.Length - 1); // ugly and probably slow
				else if (ea.KeyChar == 0x7F && SearchString.Length > 0) // 0x7F is ctrl-backspace (delete)
					SearchString = SearchString.Remove(SearchString.LastIndexOfAny(new[] { ' ', '\t', '\r', '\n' }).Min(0));
				else if (ea.KeyChar == (char)Keys.Escape)
				{
					SearchString = string.Empty;
					WatchlistSearchTimer.Stop();
					return;
				}
				// ignore control characters otherwise
				//else
				//	SearchString += ea.KeyChar;
			}
			else
				SearchString += ea.KeyChar;

			if (!WatchlistSearchTimer.Enabled) WatchlistSearchTimer.Start();

			ea.Handled = true;

			tooltip.Show("Searching: " + SearchString, WatchlistRules,
				WatchlistRules.ClientSize.Width / 3, WatchlistRules.ClientSize.Height,
				string.IsNullOrEmpty(SearchString) ? 500 : 2_500);
		}

		void WatchlistSearchTimer_Tick(object sender, EventArgs e)
		{
			if (_Disposed || !IsHandleCreated) return;

			bool foundprimary = false, found = false;

			if (!string.IsNullOrEmpty(SearchString))
			{
				var search = SearchString.ToLowerInvariant();

				foreach (ListViewItem item in WatchlistRules.Items)
				{
					found = false;

					if (item.SubItems[NameColumn].Text.IndexOf(search, StringComparison.InvariantCultureIgnoreCase) >= 0)
						found = true;
					else if (item.SubItems[ExeColumn].Text.IndexOf(search, StringComparison.InvariantCultureIgnoreCase) >= 0)
						found = true;
					else if (item.SubItems[PathColumn].Text.IndexOf(search, StringComparison.InvariantCultureIgnoreCase) >= 0)
						found = true;

					if (found)
					{
						if (!foundprimary)
						{
							foundprimary = true;
							WatchlistRules.FocusedItem = item;
							item.Focused = true;
							item.EnsureVisible();
						}
					}

					item.Selected = found;
				}
			}

			if (found || WatchlistSearchInputTimer.ElapsedMilliseconds > 1_000)
				WatchlistSearchTimer.Stop();
		}

		public void CPULoadEvent(object _, Hardware.CPUSensorEventArgs _2)
		{
			if (!IsHandleCreated || _Disposed) return;

			BeginInvoke(new Action(() =>
			{
				//
			}));
		}

		public void GPULoadEvent(object _, Hardware.GPUSensorEventArgs ea)
		{
			if (!IsHandleCreated || _Disposed) return;

			BeginInvoke(new Action(() => GPULoadEvent_Invoke(ea)));
		}

		void GPULoadEvent_Invoke(Hardware.GPUSensorEventArgs ea)
		{
			if (!IsHandleCreated || _Disposed) return;

			try
			{
				GPUSensorUpdate(ea.Data);
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		void GPULoadPoller(object sender, EventArgs e)
		{
			if (_Disposed || !IsHandleCreated) return;

			if (hardwaremonitor is null)
			{
				Log.Error("<Window> Hardware Monitor inaccessible, disabling GPU updates.");
				UItimer.Tick -= GPULoadPoller;
				return;
			}

			try
			{
				var sensors = hardwaremonitor?.GPUSensorData();
				if (sensors.HasValue) GPUSensorUpdate(sensors.Value);
			}
			catch (InvalidOperationException) { /* happens on first polling */ }
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		void GPUSensorUpdate(Hardware.GPUSensors sensors)
		{
			try
			{
				float vramTotal = sensors.MemTotal / 1024;
				float vramUsed = vramTotal * (sensors.MemLoad / 100);
				float vramFree = vramTotal - vramUsed;

				// gpuvram.Text = $"{vramFree:N2} of {vramTotal:N1} GiB free ({ea.Data.MemLoad:N1} % usage) [Controller: {ea.Data.MemCtrl:N1} %]";
				gpuvram.Text = $"{vramFree:0.##} GiB free ({sensors.MemLoad:0.#} % usage) [Controller: {sensors.MemCtrl:0.#} %]";
				gpuload.Text = $"{sensors.Load:0.#} %";
				gpufan.Text = $"{sensors.FanLoad:0.#} % [{sensors.FanSpeed} RPM]";
				gputemp.Text = $"{sensors.Temperature:0.#} C";
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		void StartProcessDebug()
		{
			bool enabled = Process.Manager.DebugProcesses || DebugForeground;
			if (!enabled) return;

			if (ProcessDebugTab is null) BuildProcessDebug();

			if (Process.Manager.DebugProcesses) processmanager.HandlingStateChange += ProcessHandlingStateChangeEvent;

			if (activeappmonitor != null && DebugForeground)
				activeappmonitor.ActiveChanged += OnActiveWindowChanged;

			EnsureVerbosityLevel();
		}

		void StopProcessDebug()
		{
			if (!DebugForeground && activeappmonitor != null) activeappmonitor.ActiveChanged -= OnActiveWindowChanged;
			if (!Process.Manager.DebugProcesses) processmanager.HandlingStateChange -= ProcessHandlingStateChangeEvent;

			bool enabled = Process.Manager.DebugProcesses || DebugForeground;

			if (activeappmonitor != null && DebugForeground)
				activeappmonitor.ActiveChanged -= OnActiveWindowChanged;

			bool refocus = TabPages.SelectedTab.Equals(ProcessDebugTab);
			if (!enabled)
			{
				if (activeappmonitor != null)
				{
					activePID.Text = HumanReadable.Generic.Undefined;
					activeFullscreen.Text = HumanReadable.Generic.Undefined;
				}

				TabPages.Controls.Remove(ProcessDebugTab);
				ProcessingList.Items.Clear();
				ExitWaitList.Items.Clear();

				ProcessDebugTab?.Dispose();
				ProcessDebugTab = null;
			}

			// TODO: unlink events
			if (refocus) TabPages.SelectedIndex = 0; // info tab
		}

		void BuildProcessDebug()
		{
			ExitWaitlistMap = new ConcurrentDictionary<int, ListViewItem>();

			ExitWaitList = new Extensions.ListViewEx()
			{
				AutoSize = true,
				FullRowSelect = true,
				View = View.Details,
				MinimumSize = new System.Drawing.Size(-2, 80),
				Dock = DockStyle.Fill,
			};

			ExitWaitList.Columns.Add("Id", 50);
			ExitWaitList.Columns.Add(HumanReadable.System.Process.Executable, 280);
			ExitWaitList.Columns.Add("State", 160);
			ExitWaitList.Columns.Add(HumanReadable.Hardware.Power.Section, 80);

			var waitlist = processmanager?.GetExitWaitList();
			if ((waitlist?.Length ?? 0) > 0)
				foreach (var info in waitlist)
					ExitWaitListHandler(info);

			var processlayout = new Extensions.TableLayoutPanel()
			{
				ColumnCount = 1,
				AutoSize = true,
				Dock = DockStyle.Fill,
			};

			if (ActiveAppMonitorEnabled)
			{
				var foregroundapppanel = new FlowLayoutPanel
				{
					Dock = DockStyle.Fill,
					FlowDirection = FlowDirection.LeftToRight,
					WrapContents = false,
					AutoSize = true,
					//Width = tabLayout.Width - 3,
				};

				activeLabel = new Extensions.Label() { Text = "no active window found", AutoEllipsis = true };
				activeExec = new Extensions.Label() { Text = HumanReadable.Generic.Uninitialized, Width = 100 };
				activeFullscreen = new Extensions.Label() { Text = HumanReadable.Generic.Uninitialized, Width = 60 };
				activePID = new Extensions.Label() { Text = HumanReadable.Generic.Uninitialized, Width = 60 };

				foregroundapppanel.Controls.AddRange(new Control[] {
					new Extensions.Label() { Text = "Active window:", Width = 80 },
					activeLabel,
					activeExec,
					activeFullscreen,
					new Extensions.Label { Text = "Id:", Width = 20 },
					activePID,
				});

				processlayout.Controls.Add(foregroundapppanel);
			}

			processlayout.Controls.Add(new Extensions.Label() { Text = "Exit wait list...", Padding = BigPadding });
			processlayout.Controls.Add(ExitWaitList);

			processlayout.Controls.Add(new Extensions.Label() { Text = "Processing list" });

			ProcessingList = new Extensions.ListViewEx()
			{
				AutoSize = true,
				FullRowSelect = true,
				View = View.Details,
				MinimumSize = new System.Drawing.Size(-2, 120),
				Dock = DockStyle.Fill,
			};

			ProcessingList.Columns.Add("Id", 50);
			ProcessingList.Columns.Add(HumanReadable.System.Process.Executable, 280);
			ProcessingList.Columns.Add("State", 160);
			ProcessingList.Columns.Add("Time", 80);

			processlayout.Controls.Add(ProcessingList);

			ProcessDebugTab = new Extensions.TabPage("Process Debug") { Padding = BigPadding };

			ProcessDebugTab.Controls.Add(processlayout);

			TabPages.Controls.Add(ProcessDebugTab);
		}

		/// <summary>
		/// Process ID to processinglist mapping.
		/// </summary>
		readonly ConcurrentDictionary<int, ListViewItem> ProcessEventMap = new ConcurrentDictionary<int, ListViewItem>();

		void ProcessHandlingStateChangeEvent(object _, Process.HandlingStateChangeEventArgs ea)
		{
			if (!IsHandleCreated || _Disposed) return;

			if (!Process.Manager.DebugProcesses && !DebugForeground) return;

			try
			{
				int key = ea.Info.Id;
				bool newitem = false;
				if (!ProcessEventMap.TryGetValue(key, out ListViewItem item))
				{
					item = new ListViewItem(new string[] { key.ToString(CultureInfo.InvariantCulture), ea.Info.Name, string.Empty, string.Empty });
					newitem = true;
					ProcessEventMap.TryAdd(key, item);
				}

				BeginInvoke(new Action(() => ProcessHandlingStateChangeEvent_Invoke(ea, item, newitem)));
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		void ProcessHandlingStateChangeEvent_Invoke(Process.HandlingStateChangeEventArgs ea, ListViewItem item, bool newitem = false)
		{
			if (!IsHandleCreated || _Disposed) return;

			int key = ea.Info.Id;

			try
			{
				// 0 = Id, 1 = Name, 2 = State
				item.SubItems[0].Text = ea.Info.Id.ToString(CultureInfo.InvariantCulture);
				item.SubItems[2].Text = ea.Info.State.ToString();
				item.SubItems[3].Text = DateTime.Now.ToLongTimeString();

				if (newitem) ProcessingList.Items.Insert(0, item);

				if (ea.Info.Handled) RemoveOldProcessingEntry(key);
			}
			catch (System.ObjectDisposedException) { Statistics.DisposedAccesses++; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		void RemoveOldProcessingEntry(int key)
		{
			if (!IsHandleCreated || _Disposed) return;

			BeginInvoke(new Action(async () => await RemoveOldProcessingEntry_Invoke(key).ConfigureAwait(true)));
		}

		async Task RemoveOldProcessingEntry_Invoke(int key)
		{
			if (!IsHandleCreated || _Disposed) return;

			await Task.Delay(TimeSpan.FromSeconds(15)).ConfigureAwait(true);

			if (!IsHandleCreated || _Disposed) return;

			if (ProcessEventMap.TryRemove(key, out ListViewItem item))
				ProcessingList.Items.Remove(item);
		}

		StatusStrip statusbar;
		ToolStripStatusLabel processingcount, trackingcount, processingtimer, powermodestatusbar;

		void BuildStatusbar()
		{
			statusbar = new StatusStrip()
			{
				Parent = this,
				Dock = DockStyle.Bottom,
			};

			processingcount = new ToolStripStatusLabel("[" + HumanReadable.Generic.Uninitialized + "]") { AutoSize = false };
			processingtimer = new ToolStripStatusLabel("[" + HumanReadable.Generic.Uninitialized + "]") { AutoSize = false };
			trackingcount = new ToolStripStatusLabel("[" + HumanReadable.Generic.Uninitialized + "]") { AutoSize = false };
			powermodestatusbar = new ToolStripStatusLabel(Power.Utility.GetModeName(powermanager?.CurrentMode ?? Power.Mode.Undefined)) { Alignment = ToolStripItemAlignment.Right };

			statusbar.Items.AddRange(new ToolStripItem[] {
				new ToolStripStatusLabel("Processing:"),
				processingcount, // not truly useful for anything but debug to show if processing is hanging Somewhere
				new ToolStripStatusLabel("Next scan in:"),
				processingtimer,
				new ToolStripStatusLabel("Tracking:"),
				trackingcount,
				new ToolStripStatusLabel() { Alignment = ToolStripItemAlignment.Right, Width = -2, Spring = true },
				new ToolStripStatusLabel("Power plan:") { Alignment = ToolStripItemAlignment.Right },
				powermodestatusbar,
			});
		}

		void FreeMemoryRequest(object _, EventArgs _2)
		{
			try
			{
				using var exsel = new ProcessSelectDialog(
					modules.processmanager,
					"WARNING: This Can be a Bad idea." +
					"\nAll application memory is pushed to page file. This will temporarily increase available RAM," +
					"\nbut increases NVM usage significantly until apps have paged back the memory they actively need." +
					"\n\nSelection omits chosen app from paging. Select nothing to try free memory in general.");

				if (exsel.ShowDialog(this) == DialogResult.OK)
					processmanager?.FreeMemoryAsync(exsel.Info.Id).ConfigureAwait(false);
			}
			catch (Exception ex) { Logging.Stacktrace(ex); }
		}

		public void ExitWaitListHandler(Process.ProcessEx ea)
		{
			//if (activeappmonitor is null) return;
			if (!IsHandleCreated || _Disposed) return;

			BeginInvoke(new Action(() => ExitWaitListHandler_Invoke(ea)));
		}

		void ExitWaitListHandler_Invoke(Process.ProcessEx info)
		{
			if (!IsHandleCreated || _Disposed) return;

			try
			{
				bool fgonly = info.Controller.Foreground != ForegroundMode.Ignore;
				bool fg = (info.Id == (activeappmonitor?.ForegroundId ?? info.Id));

				ListViewItem li = null;
				string fgonlytext = fgonly ? (fg ? HumanReadable.System.Process.Foreground : HumanReadable.System.Process.Background) : "Ignored";
				string powertext = (info.PowerWait ? "FORCED" : HumanReadable.Generic.NotAvailable);

				if (ExitWaitlistMap?.TryGetValue(info.Id, out li) ?? false)
				{
					if (Trace && DebugForeground) Log.Debug($"WaitlistHandler: {info.Name} = {info.State.ToString()}");

					switch (info.State)
					{
						case Process.HandlingState.Paused:
							break;
						case Process.HandlingState.Resumed:
							// move item to top
							//exitwaitlist.Items.Remove(li);
							//exitwaitlist.Items.Insert(0, li);
							//li.EnsureVisible();
							break;
						case Process.HandlingState.Exited:
							ExitWaitList?.Items.Remove(li);
							ExitWaitlistMap?.TryRemove(info.Id, out _);
							return;
						default:
							break;
					}

					li.SubItems[2].Text = fgonlytext;
					li.SubItems[3].Text = powertext;
				}
				else
				{
					li = new ListViewItem(new string[] {
							info.Id.ToString(CultureInfo.InvariantCulture),
							info.Name,
							fgonlytext,
							powertext,
						});

					ExitWaitlistMap?.TryAdd(info.Id, li);
					ExitWaitList?.Items.Insert(0, li);
					li.EnsureVisible();
				}
			}
			catch (Exception ex) { Logging.Stacktrace(ex); }
		}

		// Called by UI update timer, should be UI thread by default
		void UpdateMemoryStats(object _, EventArgs _2)
		{
			if (!IsHandleCreated || _Disposed) return;
			if (!ramload.Visible) return;

			Memory.Update(); // TODO: this is kinda dumb way to do things
			double freegb = (double)Memory.Free / 1_073_741_824d;
			double totalgb = (double)Memory.Total / 1_073_741_824d;
			double usage = 1 - (freegb / totalgb);
			//ramload.Text = $"{freegb:N2} of {totalgb:N1} GiB free ({usage * 100d:N1} % usage), {MemoryManager.Pressure * 100:N1} % pressure";
			ramload.Text = $"{freegb:0.##} GiB free ({usage * 100d:0.#} % usage), {Memory.Pressure * 100:0.#} % pressure";

			// TODO: Print warning if MemoryManager.Pressure > 100%

			//vramload.Text = $"{healthmonitor.VRAM()/1_048_576:N0} MB"; // this returns total, not free or used
		}

		// called by cpumonitor, not in UI thread by default
		// TODO: Reverse this design, make the UI poll instead
		void CPULoadHandler(object _, Hardware.ProcessorLoadEventArgs ea)
		{
			if (!IsHandleCreated || _Disposed) return;
			if (!cpuload.Visible) return;

			BeginInvoke(new Action(() => CPULoadHandler_Invoke(ea)));
		}

		void CPULoadHandler_Invoke(Hardware.ProcessorLoadEventArgs ea)
		{
			if (!IsHandleCreated || _Disposed) return;

			var load = ea.Load;

			cpuload.Text = $"{load.Current:0.#} %, Low: {load.Low:0.#} %, Mean: {load.Mean:0.#} %, High: {load.High:0.#} %; Queue: {load.Queue:N0}";
			// 50 %, Low: 33.2 %, Mean: 52.1 %, High: 72.8 %, Queue: 1
		}

		readonly System.Drawing.Color
			Reddish = System.Drawing.Color.FromArgb(255, 230, 230),
			Greenish = System.Drawing.Color.FromArgb(240, 255, 230),
			Orangeish = System.Drawing.Color.FromArgb(255, 250, 230);

		public void PowerLoadDebugHandler(object _, Power.AutoAdjustReactionEventArgs ea)
		{
			if (!IsHandleCreated || _Disposed) return;

			BeginInvoke(new Action(() => PowerLoadDebugHandler_Invoke(ea)));
		}

		void PowerLoadDebugHandler_Invoke(Power.AutoAdjustReactionEventArgs ea)
		{
			if (!IsHandleCreated || _Disposed) return;

			try
			{
				var load = ea.Load;

				var li = new ListViewItem(new string[] {
					$"{load.Current:0.##} %",
					$"{load.Mean:0.##} %",
					$"{load.High:0.##} %",
					$"{load.Low:0.##} %",
					ea.Reaction.ToString(),
					Power.Utility.GetModeName(ea.Mode),
					ea.Enacted.ToString(CultureInfo.InvariantCulture),
					$"{ea.Pressure * 100f:0.#} %"
				})
				{
					UseItemStyleForSubItems = false
				};

				if (ea.Enacted)
				{
					li.SubItems[4].BackColor =
						li.SubItems[5].BackColor =
						li.SubItems[6].BackColor = System.Drawing.SystemColors.ActiveCaption;
				}

				if (ea.Mode == Power.Mode.HighPerformance)
					li.SubItems[3].BackColor = Reddish;
				else if (ea.Mode == Power.Mode.PowerSaver)
					li.SubItems[2].BackColor = Greenish;
				else
					li.SubItems[3].BackColor = li.SubItems[2].BackColor = Orangeish;

				// this tends to throw if this event is being handled while the window is being closed
				if (powerbalancerlog.Items.Count > 7)
					powerbalancerlog.Items.RemoveAt(0);
				powerbalancerlog.Items.Add(li);

				powerbalancer_forcedcount.Text = powermanager.ForceCount.ToString(CultureInfo.InvariantCulture);
			}
			catch (Exception ex) { Logging.Stacktrace(ex); }
		}

		void WatchlistContextMenuOpen(object _, EventArgs _2)
		{
			bool oneitem = (WatchlistRules.SelectedItems.Count == 1);

			try
			{
				foreach (ToolStripItem lsi in watchlistms.Items)
				{
					if (lsi == watchlistadd) continue;
					lsi.Enabled = oneitem;
				}

				if (oneitem)
				{
					if (processmanager.GetControllerByName(WatchlistRules.SelectedItems[0].SubItems[NameColumn].Text, out var prc))
					{
						watchlistenable.Enabled = true;
						watchlistenable.Checked = prc.Enabled;
					}
				}
				else
					watchlistenable.Enabled = false;
			}
			catch (Exception ex) { Logging.Stacktrace(ex); }
		}

		void EnableWatchlistRule(object _, EventArgs _2)
		{
			try
			{
				if (WatchlistRules.SelectedItems.Count == 1)
				{
					var li = WatchlistRules.SelectedItems[0];
					if (processmanager.GetControllerByName(li.SubItems[NameColumn].Text, out var prc))
					{
						watchlistenable.Enabled = true;
						watchlistenable.Checked = prc.Enabled = !watchlistenable.Checked;

						Log.Information("[" + prc.FriendlyName + "] " + (prc.Enabled ? HumanReadable.Generic.Enabled : HumanReadable.Generic.Disabled));

						prc.SaveConfig();

						prc.ResetInvalid();

						WatchlistItemColor(li, prc);

						processmanager?.HastenScan(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
					}
				}
				else
					watchlistenable.Enabled = false;
			}
			catch (Exception ex) { Logging.Stacktrace(ex); }
		}

		void EditWatchlistRule(object _, EventArgs _2)
		{
			if (WatchlistRules.SelectedItems.Count == 1)
			{
				try
				{
					processmanager.GetControllerByName(WatchlistRules.SelectedItems[0].SubItems[NameColumn].Text, out var prc);

					using var editdialog = new Config.WatchlistEditWindow(modules, prc); // 1 = executable
					if (editdialog.ShowDialog() == DialogResult.OK)
					{
						UpdateWatchlistRule(prc);
						processmanager?.HastenScan(TimeSpan.FromMinutes(1), forceSort: true).ConfigureAwait(false);
					}
				}
				catch (Exception ex) { Logging.Stacktrace(ex); }
			}
		}

		void AddWatchlistRule(object _, EventArgs _2)
		{
			try
			{
				using var ew = new Config.WatchlistEditWindow(modules);
				var rv = ew.ShowDialog();
				if (rv == DialogResult.OK)
				{
					var prc = ew.Controller;

					if (processmanager.AddController(prc))
					{
						AddToWatchlistList(prc);

						processmanager?.HastenScan(TimeSpan.FromMinutes(1), forceSort: true).ConfigureAwait(false);
					}
					else
						prc.Dispose();
				}
			}
			catch (Exception ex) { Logging.Stacktrace(ex); }
		}

		void DeleteWatchlistRule(object _, EventArgs _2)
		{
			if (WatchlistRules.SelectedItems.Count != 1) return;

			try
			{
				var li = WatchlistRules.SelectedItems[0];

				if (processmanager.GetControllerByName(li.SubItems[NameColumn].Text, out var prc) &&
					MessageBox.ShowModal("Remove watchlist item", $"Really remove '{prc.FriendlyName}'", MessageBox.Buttons.AcceptCancel, parent: this)
						== MessageBox.ResultType.OK)
				{
					processmanager.RemoveController(prc);
					Process.Manager.DeleteConfig(prc);

					Log.Information("[" + prc.FriendlyName + "] Rule removed");

					lock (watchlist_lock)
					{
						WatchlistMap.TryRemove(prc, out ListViewItem _);
						WatchlistRules.Items.Remove(li);
					}
				}
			}
			catch (Exception ex) { Logging.Stacktrace(ex); }

			WatchlistColor(); // not necessary if the item removed was the last. Sadly not significant enough to give special case.
		}

		// This should be somewhere else
		// TODO: Make this better
		void CopyRuleToClipboard(object _, EventArgs _2)
		{
			if (WatchlistRules.SelectedItems.Count == 1)
			{
				try
				{
					var li = WatchlistRules.SelectedItems[0];
					var name = li.SubItems[NameColumn].Text;

					if (!processmanager.GetControllerByName(name, out Process.Controller prc))
					{
						Log.Error("[" + name + "] Not found. Something's terribly wrong.");
						return;
					}

					var sbs = new StringBuilder(1024)
						.Append('[').Append(prc.FriendlyName).AppendLine("]");

					if (prc.Executables.Length > 0) sbs.Append("Executables = { ").Append(string.Join(", ", prc.Executables)).AppendLine(" }");
					if (!string.IsNullOrEmpty(prc.Path)) sbs.Append("Path = ").AppendLine(prc.Path);
					if (!string.IsNullOrEmpty(prc.Description)) sbs.Append("Description = ").Append(prc.Description);
					if (prc.IgnoreList != null) sbs.Append("Ignore = { ").Append(string.Join(", ", prc.IgnoreList)).AppendLine(" }");

					if (prc.Priority.HasValue)
					{
						sbs.Append(HumanReadable.System.Process.Priority).Append(" = ").Append(prc.Priority.Value.ToInt32()).AppendLine();
						sbs.Append(HumanReadable.System.Process.PriorityStrategy).Append(" = ").Append((int)prc.PriorityStrategy).AppendLine();
					}
					if (prc.AffinityMask >= 0)
					{
						sbs.Append(HumanReadable.System.Process.Affinity).Append(" = ").Append(prc.AffinityMask).AppendLine();
						sbs.Append(HumanReadable.System.Process.AffinityStrategy).Append(" = ").Append((int)prc.AffinityStrategy).AppendLine();
					}

					if (prc.IOPriority != Process.IOPriority.Ignore) sbs.Append("IO priority = ").Append((int)prc.IOPriority).AppendLine();

					if (prc.PowerPlan != Power.Mode.Undefined)
						sbs.Append(HumanReadable.Hardware.Power.Plan).Append(" = ").AppendLine(Power.Utility.GetModeName(prc.PowerPlan));
					if (prc.Recheck > 0) sbs.Append("Recheck = ").Append(prc.Recheck).AppendLine();
					if (prc.AllowPaging) sbs.Append("Allow paging = ").Append(prc.AllowPaging).AppendLine();

					if (prc.Foreground != ForegroundMode.Ignore)
					{
						sbs.Append("Foreground mode = ").Append((int)prc.Foreground).AppendLine();
						if (prc.BackgroundPriority.HasValue)
							sbs.Append("Background priority = ").Append(prc.BackgroundPriority.Value.ToInt32()).AppendLine();
						if (prc.BackgroundAffinity >= 0)
							sbs.Append("Background affinity = ").Append(prc.BackgroundAffinity).AppendLine();
					}

					if (prc.ModifyDelay > 0) sbs.Append(Process.Constants.ModifyDelay).Append(" = ").Append(prc.ModifyDelay).AppendLine();

					if (prc.PathVisibility != Process.PathVisibilityOptions.Invalid)
						sbs.Append("Path visibility = ").Append((int)prc.PathVisibility).AppendLine();

					if (prc.VolumeStrategy != Audio.VolumeStrategy.Ignore)
					{
						sbs.Append("Volume = ").AppendFormat(CultureInfo.InvariantCulture, "{0:0.##}", prc.Volume).AppendLine();
						sbs.Append("Volume strategy = ").Append((int)prc.VolumeStrategy).AppendLine();
					}

					sbs.Append("Preference = ").Append(prc.OrderPreference).AppendLine();

					if (!prc.LogAdjusts) sbs.Append(Constants.Logging).AppendLine(" = false");
					if (prc.LogStartAndExit) sbs.AppendLine("Log start and exit = true");
					if (!prc.Enabled) sbs.AppendLine("Enabled = false");

					// TODO: Add Modify Delay

					try
					{
						Clipboard.SetText(sbs.ToString(), TextDataFormat.UnicodeText);
						Log.Information("[" + name + "] Configuration saved to clipboard.");
					}
					catch (OutOfMemoryException) { throw; }
					catch
					{
						Log.Warning("[" + name + "] Failed to copy configuration to clipboard.");
					}
				}
				catch (Exception ex) { Logging.Stacktrace(ex); }
			}
		}

		Extensions.Label?
			tempObjectCount = null, tempObjectSize = null,
			cpuload = null, ramload = null,
			pwmode = null, pwcause = null, pwbehaviour = null,
			nvmtransferslabel = null, nvmsplitio = null, nvmdelaylabel = null, nvmqueuelabel = null,
			gpuvram = null, gpuload = null, gputemp = null, gpufan = null;

		public void TempScanStats(StorageManager.ScanState state, StorageManager.DirectoryStats stats)
		{
			if (!IsHandleCreated || _Disposed) return;

			BeginInvoke(new Action(() => TempScanStats_Invoke(state, stats)));
		}

		void TempScanStats_Invoke(StorageManager.ScanState state, StorageManager.DirectoryStats stats)
		{
			if (!IsHandleCreated || _Disposed) return;

			tempObjectSize.Text = (stats.Size / 1_000_000).ToString(CultureInfo.CurrentCulture);
			tempObjectCount.Text = (stats.Dirs + stats.Files).ToString(CultureInfo.CurrentCulture);
		}

		readonly Panel LogPanel;
		readonly Extensions.ListViewEx EventLogList;
		readonly ColumnHeader EventLogMainColumn;
		readonly MenuStrip MenuToolbar;
		readonly ImageList ImgList;

		public void FillLog()
		{
			MemoryLog.MemorySink.OnNewEvent += NewLogReceived;

			// Log.Verbose("Filling GUI log.");
			var logbuffer = MemoryLog.MemorySink.ToArray();
			Logging.DebugMsg("Filling backlog of messages: " + logbuffer.Length.ToString(CultureInfo.InvariantCulture));

			lock (LogListData_Lock)
			{
				foreach (var logmsg in logbuffer)
					LogListData.Add(logmsg);
			}

			//ShowLastLog(); // part of resizeloglist

			ResizeLogList(this, EventArgs.Empty);
		}

		public void Hook(Process.ForegroundManager manager)
		{
			if (manager is null) return;

			if (Trace) Log.Verbose("Hooking active app manager.");

			activeappmonitor = manager;
			activeappmonitor.OnDisposed += (_, _2) => activeappmonitor = null;

			if (DebugForeground || Process.Manager.DebugProcesses)
				StartProcessDebug();
		}

		public void Hook(Power.Manager manager)
		{
			if (manager is null) return;

			if (Trace) Log.Verbose("Hooking power manager.");

			powermanager = manager;
			powermanager.OnDisposed += (_, _2) => powermanager = null;

			powermanager.BehaviourChange += PowerBehaviourEvent;
			powermanager.PlanChange += PowerPlanEvent;

			var bev = new Power.PowerBehaviourEventArgs(powermanager.Behaviour);
			PowerBehaviourEvent(this, bev); // populates pwbehaviour
			var pev = new Power.ModeEventArgs(powermanager.CurrentMode);
			PowerPlanEvent(this, pev); // populates pwplan and pwcause

			if (DebugPower) AttachPowerDebug();

			power_auto.Enabled = true;
			UpdatePowerBehaviourHighlight(powermanager.Behaviour);
			HighlightPowerMode();

			BeginInvoke(new Action(() =>
			{
				pwcause.Text = powermanager?.LastCause.ToString() ?? "n/a";
			}));
		}

		void AttachPowerDebug()
		{
			PowerBehaviourDebugEvent(this, new Power.PowerBehaviourEventArgs(powermanager.Behaviour)); // populates powerbalancer_behaviour
			PowerPlanDebugEvent(this, new Power.ModeEventArgs(powermanager.CurrentMode)); // populates powerbalancer_plan

			powermanager.PlanChange += PowerPlanDebugEvent;
			powermanager.BehaviourChange += PowerBehaviourDebugEvent;
			powermanager.AutoAdjustAttempt += PowerLoadDebugHandler;
		}

		void DetachPowerDebug()
		{
			powermanager.PlanChange -= PowerPlanDebugEvent;
			powermanager.BehaviourChange -= PowerBehaviourDebugEvent;
			powermanager.AutoAdjustAttempt -= PowerLoadDebugHandler;
		}

		void UpdatePowerBehaviourHighlight(Power.PowerBehaviour behaviour)
		{
			switch (behaviour)
			{
				case Power.PowerBehaviour.Manual:
					power_auto.Checked = false;
					power_manual.Checked = true;
					break;
				case Power.PowerBehaviour.Auto:
					power_auto.Checked = true;
					power_manual.Checked = false;
					break;
				default: // rules-based
					power_auto.Checked = false;
					power_manual.Checked = false;
					break;
			}
		}

		void PowerBehaviourEvent(object sender, Power.PowerBehaviourEventArgs e)
		{
			if (!IsHandleCreated || _Disposed) return;

			BeginInvoke(new Action(() => PowerBehaviourEvent_Invoke(e)));
		}

		void PowerBehaviourEvent_Invoke(Power.PowerBehaviourEventArgs e)
		{
			if (!IsHandleCreated || _Disposed) return;

			UpdatePowerBehaviourHighlight(e.Behaviour);
			pwbehaviour.Text = Power.Utility.GetBehaviourName(e.Behaviour);
		}

		DateTimeOffset LastCauseTime = DateTimeOffset.MinValue;

		void PowerPlanEvent(object sender, Power.ModeEventArgs ea)
		{
			if (!IsHandleCreated || _Disposed) return;

			BeginInvoke(new Action(() => PowerPlanEvent_Invoke(ea)));
		}

		void PowerPlanEvent_Invoke(Power.ModeEventArgs e)
		{
			if (!IsHandleCreated || _Disposed) return;

			HighlightPowerMode();

			powermodestatusbar.Text = pwmode.Text = Power.Utility.GetModeName(e.NewMode);
			pwcause.Text = e.Cause != null ? e.Cause.ToString() : HumanReadable.Generic.Undefined;
			LastCauseTime = DateTimeOffset.UtcNow;
		}

		Hardware.Monitor? hardwaremonitor = null;

		public void Hook(Hardware.Monitor monitor)
		{
			hardwaremonitor = monitor;
			hardwaremonitor.OnDisposed += (_, _2) => hardwaremonitor = null;

			//hw.GPUPolling += GPULoadEvent;
			UItimer.Tick += GPULoadPoller;

			GPULoadPoller(this, EventArgs.Empty);
		}

		public void Hook(Hardware.CPUMonitor monitor)
		{
			cpumonitor = monitor;
			cpumonitor.Sampling += CPULoadHandler;
			cpumonitor.OnDisposed += (_, _2) => cpumonitor = null;

			CPULoadHandler(this, new Hardware.ProcessorLoadEventArgs() { Load = cpumonitor.GetLoad });
		}

		HealthMonitor healthmonitor = null;

		public void Hook(HealthMonitor monitor)
		{
			healthmonitor = monitor;
			healthmonitor.OnDisposed += (_, _2) => healthmonitor = null;

			healthmonitor.Poll();

			UpdateMemoryStats(this, EventArgs.Empty);
			UItimer.Tick += UpdateHealthMon;
			GotFocus += UpdateHealthMon;

			VisibleChanged += VisibleChangedEvent;

			TabPages.TabIndexChanged += VisibleTabChangedEvent;

			UpdateHealthMon(this, EventArgs.Empty);
		}

		void VisibleTabChangedEvent(object sender, EventArgs e)
		{
			// TODO: Stop updating specific tabs?
		}

		void VisibleChangedEvent(object sender, EventArgs e)
		{
			if (Visible)
				UItimer.Start();
			else
				UItimer.Stop();
		}

		int skipTransfers = 0, skipSplits = 0, skipDelays = 0, skipQueues = 0;

		int updatehealthmon_lock = 0;

		async void UpdateHealthMon(object sender, EventArgs e)
		{
			if (!IsHandleCreated || _Disposed) return;

			if (powermanager?.SessionLocked ?? false) return;

			if (!Atomic.Lock(ref updatehealthmon_lock)) return;

			await Task.Delay(100).ConfigureAwait(false);

			if (_Disposed) return; // recheck

			try
			{
				var health = healthmonitor?.Poll();
				if (health is null) return;

				float nvmtransfers = health.NVMTransfers, splitio = health.SplitIO, nvmdelayt = health.NVMDelay, nvmqueue = health.NVMQueue;

				/*
				float impact_transfers = (nvmtransfers / 500f).Max(3f); // expected to cause 0 to 2, and up to 4
				float impact_splits = (splitio / 125f); // expected to cause 0 to 2
				float impact_delay = (nvmdelayt / 12f); // should cause 0 to 4 usually
				float impact_queue = (nvmqueue / 2f).Max(4f);
				float impact = impact_transfers + impact_splits + impact_delay + impact_queue;
				*/

				//float impact_faults = health.PageFaults;

				BeginInvoke(new Action(() => UpdateNVMLabels(nvmtransfers, splitio, nvmdelayt, nvmqueue)));

				//hardfaults.Text = !float.IsNaN(health.PageInputs) ? $"{health.PageInputs / health.PageFaults:N1} %" : HumanReadable.Generic.NotAvailable;

				//oldHealthReport = health;
			}
			catch (OutOfMemoryException) { throw; }
			catch (ObjectDisposedException) { Statistics.DisposedAccesses++; throw; }
			catch (NullReferenceException) { /* happens only due to disposal elsewhere */ }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
			finally
			{
				Atomic.Unlock(ref updatehealthmon_lock);
			}
		}

		void UpdateNVMLabels(float nvmtransfers, float splitio, float nvmdelayt, float nvmqueue)
		{
			if (nvmtransfers >= float.Epsilon)
			{
				this.nvmtransferslabel.Text = $"{nvmtransfers:0.#} {WarningLevelString((int)nvmtransfers, 200, 320, 480)}";
				this.nvmtransferslabel.ForeColor = System.Drawing.SystemColors.WindowText;
				skipTransfers = 0;
			}
			else
			{
				if (skipTransfers++ == 0)
					this.nvmtransferslabel.ForeColor = System.Drawing.SystemColors.GrayText;
				else
					this.nvmtransferslabel.Text = "0.0";
			}

			if (splitio >= float.Epsilon)
			{
				nvmsplitio.Text = $"{splitio:0.#} {WarningLevelString((int)splitio, 20, 80, Math.Max(120, (int)(nvmtransfers * 0.5)))}";
				nvmsplitio.ForeColor = System.Drawing.SystemColors.WindowText;
				skipSplits = 0;
			}
			else
			{
				if (skipSplits++ == 0)
					nvmsplitio.ForeColor = System.Drawing.SystemColors.GrayText;
				else
					nvmsplitio.Text = "0.0";
			}

			if (nvmdelayt >= float.Epsilon)
			{
				float delay = nvmdelayt * 1000f;
				nvmdelaylabel.Text = $"{delay:0.#} ms {WarningLevelString((int)delay, 22, 52, 70)}";
				nvmdelaylabel.ForeColor = System.Drawing.SystemColors.WindowText;
				skipDelays = 0;
			}
			else
			{
				if (skipDelays++ == 0)
					nvmdelaylabel.ForeColor = System.Drawing.SystemColors.GrayText;
				else
					nvmdelaylabel.Text = "0 ms";
			}

			if (nvmqueue >= float.Epsilon)
			{
				nvmqueuelabel.Text = $"{nvmqueue:N0} {WarningLevelString((int)nvmqueue, 2, 8, 22)}";
				nvmqueuelabel.ForeColor = System.Drawing.SystemColors.WindowText;
				skipQueues = 0;
			}
			else
			{
				if (skipQueues++ == 0)
					nvmqueuelabel.ForeColor = System.Drawing.SystemColors.GrayText;
				else
					nvmqueuelabel.Text = "0";
			}
		}

		static string WarningLevelString(int value, int high, int vhigh, int extreme)
		{
			if (value >= extreme)
				return "extreme";
			else if (value >= vhigh)
				return "very high";
			else if (value >= high)
				return "high";
			else
				return string.Empty;
		}

		public void PowerBehaviourDebugEvent(object _, Power.PowerBehaviourEventArgs ea)
		{
			if (!IsHandleCreated || _Disposed) return;

			if (!DebugPower) return;

			BeginInvoke(new Action(() => PowerBehaviourDebugEvent_Invoke(ea)));
		}

		void PowerBehaviourDebugEvent_Invoke(Power.PowerBehaviourEventArgs ea)
		{
			if (!IsHandleCreated || _Disposed) return;

			powerbalancer_behaviour.Text = Power.Utility.GetBehaviourName(ea.Behaviour);
			if (ea.Behaviour != Power.PowerBehaviour.Auto)
				powerbalancerlog.Items.Clear();
		}

		public void PowerPlanDebugEvent(object _, Power.ModeEventArgs ea)
		{
			if (!IsHandleCreated || _Disposed) return;

			if (!DebugPower) return;

			BeginInvoke(new Action(() => PowerPlanDebugEvent_Invoke(ea)));
		}

		void PowerPlanDebugEvent_Invoke(Power.ModeEventArgs ea)
		{
			if (!IsHandleCreated || _Disposed) return;

			powerbalancer_plan.Text = Power.Utility.GetModeName(ea.NewMode);
		}

		public void UpdateNetworkDevices(object _, EventArgs _2)
		{
			if (!IsHandleCreated || _Disposed) return;

			BeginInvoke(new Action(UpdateNetworkDevices_Invoke));
		}

		void UpdateNetworkDevices_Invoke()
		{
			if (!IsHandleCreated || _Disposed) return;

			InetStatusLabel(netmonitor.InternetAvailable);
			NetStatusLabelUpdate(netmonitor.NetworkAvailable);

			NetworkDevices.Items.Clear();

			var interfaces = netmonitor.InterfaceList;

			ListViewItem[] niclist = new ListViewItem[interfaces.Count];

			int index = 0;
			foreach (var dev in interfaces)
			{

				var li = niclist[index++] = new ListViewItem(new string[] {
					dev.Name,
					dev.Type.ToString(),
					dev.IPv4Address?.ToString() ?? HumanReadable.Generic.NotAvailable,
					dev.IPv4Status.ToString(),
					dev.IPv6Address?.ToString() ?? HumanReadable.Generic.NotAvailable,
					dev.IPv6Status.ToString(),
					HumanReadable.Generic.NotAvailable, // traffic delta
					HumanReadable.Generic.NotAvailable, // error delta
					HumanReadable.Generic.NotAvailable, // total errors
				})
				{
					UseItemStyleForSubItems = false
				};

				var address = dev.IPv4Address.GetAddressBytes();

				var ip4li = li.SubItems[IPv4Column];
				if ((address[0] == 169 && address[1] == 254) || (address[0] == 198 && address[1] == 168))
					ip4li.ForeColor = System.Drawing.SystemColors.GrayText;

				var ip6li = li.SubItems[IPv6Column];
				if (dev.IPv6Address.IsIPv6LinkLocal || dev.IPv6Address.IsIPv6SiteLocal)
					ip6li.ForeColor = System.Drawing.SystemColors.GrayText;
			}

			NetworkDevices.Items.AddRange(niclist);

			AlternateListviewRowColors(NetworkDevices, AlternateRowColorsDevices);
		}

		public void Hook(Network.Manager manager)
		{
			if (manager is null) return; // disabled

			if (Trace) Log.Verbose("Hooking network monitor.");

			netmonitor = manager;
			netmonitor.OnDisposed += (_, _2) => netmonitor = null;

			UpdateNetworkDevices(this, EventArgs.Empty);

			netmonitor.InternetStatusChange += InetStatusChangeEvent;
			netmonitor.IPChanged += UpdateNetworkDevices;
			netmonitor.NetworkStatusChange += NetStatusChangeEvent;
			netmonitor.DeviceSampling += NetSampleHandler;

			NetSampleHandler(this, new Network.DeviceTrafficEventArgs() { Traffic = new Network.DeviceTraffic(netmonitor.CurrentTraffic) });
			NetStatusChangeEvent(this, new Network.Status() { Available = netmonitor.NetworkAvailable });

			UItimer.Tick += UpdateNetwork;
			GotFocus += UpdateNetwork;

			menu_debug_network.Checked = manager.DebugNet;

			UpdateNetwork(this, EventArgs.Empty);
		}

		void UnhookNetwork()
		{
			netmonitor.InternetStatusChange -= InetStatusChangeEvent;
			netmonitor.IPChanged -= UpdateNetworkDevices;
			netmonitor.NetworkStatusChange -= NetStatusChangeEvent;
			netmonitor.DeviceSampling -= NetSampleHandler;
			UItimer.Tick -= UpdateNetwork;
			GotFocus -= UpdateNetwork;
		}

		void NetSampleHandler(object _, Network.DeviceTrafficEventArgs ea)
		{
			if (!IsHandleCreated || _Disposed) return;

			BeginInvoke(new Action(() => NetSampleHandler_Invoke(ea)));
		}

		void NetSampleHandler_Invoke(Network.DeviceTrafficEventArgs ea)
		{
			if (!IsHandleCreated || _Disposed) return;

			try
			{
				var item = NetworkDevices.Items[ea.Traffic.Index];
				item.SubItems[PacketDeltaColumn].Text = "+" + ea.Traffic.Delta.Unicast.ToString(CultureInfo.InvariantCulture);
				item.SubItems[ErrorDeltaColumn].Text = "+" + ea.Traffic.Delta.Errors.ToString(CultureInfo.InvariantCulture);
				item.SubItems[ErrorDeltaColumn].ForeColor = ea.Traffic.Delta.Errors > 0 ? System.Drawing.Color.OrangeRed : System.Drawing.SystemColors.ControlText;
				item.SubItems[ErrorTotalColumn].Text = ea.Traffic.Total.Errors.ToString(CultureInfo.CurrentCulture);
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		int IPv4Column = 2, IPv6Column = 4, PacketDeltaColumn = 6, ErrorDeltaColumn = 7, ErrorTotalColumn = 8;

		void InetStatusLabel(bool available)
		{
			if (!IsHandleCreated || _Disposed) return;

			BeginInvoke(new Action(() => InetStatusLabel_Invoke(available)));
		}

		void InetStatusLabel_Invoke(bool available)
		{
			if (!IsHandleCreated || _Disposed) return;

			inetstatuslabel.Text = available ? HumanReadable.Hardware.Network.Connected : HumanReadable.Hardware.Network.Disconnected;
			// inetstatuslabel.BackColor = available ? System.Drawing.Color.LightGoldenrodYellow : System.Drawing.Color.Red;
			//inetstatuslabel.BackColor = available ? System.Drawing.SystemColors.Menu : System.Drawing.Color.Red;
		}

		public void InetStatusChangeEvent(object _, Network.InternetStatus ea)
		{
			InetStatusLabel(ea.Available);
		}

		public void IPChange(object _, EventArgs ea)
		{
			// ??
		}

		void NetStatusLabelUpdate(bool available)
		{
			if (!IsHandleCreated || _Disposed) return;

			BeginInvoke(new Action(() => NetStatusLabelUpdate_Invoke(available)));
		}

		void NetStatusLabelUpdate_Invoke(bool available)
		{
			if (!IsHandleCreated || _Disposed) return;

			netstatuslabel.Text = available ? HumanReadable.Hardware.Network.Connected : HumanReadable.Hardware.Network.Disconnected;
			// netstatuslabel.BackColor = available ? System.Drawing.Color.LightGoldenrodYellow : System.Drawing.Color.Red;
			//netstatuslabel.BackColor = available ? System.Drawing.SystemColors.Menu : System.Drawing.Color.Red;
		}

		void NetStatusChangeEvent(object _, Network.Status ea)
		{
			NetStatusLabelUpdate(ea.Available);
		}

		// BUG: DO NOT LOG INSIDE THIS FOR FUCKS SAKE
		// it creates an infinite log loop
		public static int MaxLogSize { get => MemoryLog.MemorySink.Max; private set => MemoryLog.MemorySink.Max = value; }

		void ClearLog()
		{
			//loglist.Clear();

			lock (LogListData_Lock)
			{
				LogListData.Clear();
				LogListCache.Empty();
				EventLogList.VirtualListSize = 0;
			}

			//LogList.Items.Clear();
			MemoryLog.MemorySink.Clear();
		}

		async void NewLogReceived(object _, LogEventArgs logmsg)
		{
			if (!IsHandleCreated || _Disposed
				|| (LogIncludeLevel.MinimumLevel > logmsg.Level)) return;

			await Task.Delay(0).ConfigureAwait(false);

			lock (LogListData_Lock)
			{
				try
				{
					LogListData.Add(logmsg);

					LogEventArgs first;
					while (LogListData.Count > MaxLogSize)
					{
						first = LogListData[0];
						LogListData.Remove(first);
					}

					//var li = LogListGenerateItem(ea);

					//LogList.Items.Add(li);
					//LogList.Columns[0].Width = -2;

					//li.EnsureVisible();
				}
				catch (Exception ex)
				{
					Logging.Stacktrace(ex);
				}

				BeginInvoke(new Action(() => EventLogList.VirtualListSize = LogListData.Count));

				//Logging.DebugMsg("LogList new size: " + newSize.ToString());
			}

			BeginInvoke(new Action(ShowLastLog));
		}

		/*
		void NewLogReceived_Invoke(LogEventArgs logmsg)
		{
			if (!IsHandleCreated || disposed) return;

			var excessitems = Math.Max(0, (LogList.Items.Count - MaxLogSize));
			while (excessitems-- > 0)
				LogList.Items.RemoveAt(0);

			AddLog(logmsg);
		}
		*/

		void AddLog(LogEventArgs ea)
		{
			int count;

			lock (LogListData_Lock)
			{
				LogListData.Add(ea);

				LogEventArgs first;
				while (LogListData.Count > MaxLogSize)
				{
					first = LogListData[0];
					LogListData.Remove(first);
				}

				count = LogListData.Count;
			}

			if (!IsHandleCreated || _Disposed) return;

			//Logging.DebugMsg("LogList new size: " + newSize.ToString());

			BeginInvoke(new Action(() => EventLogList.VirtualListSize = count));

			//var li = LogListGenerateItem(ea);

			//LogList.Items.Add(li);
			//LogList.Columns[0].Width = -2;

			//li.EnsureVisible();
		}

		void SaveUIState()
		{
			if (!IsHandleCreated) return;

			try
			{
				if (WatchlistRules.Columns.Count == 0) return;

				using var cfg = Application.Config.Load(UIConfigFilename);

				var cols = cfg.Config[Constants.Columns];

				var appWidths = new List<int>(WatchlistRules.Columns.Count);
				var apporder = new List<int>(WatchlistRules.Columns.Count);
				for (int i = 0; i < WatchlistRules.Columns.Count; i++)
				{
					appWidths.Add(WatchlistRules.Columns[i].Width);
					apporder.Add(WatchlistRules.Columns[i].DisplayIndex);
				}

				cols[Constants.Apps].IntArray = appWidths.ToArray();
				cols["App order"].IntArray = apporder.ToArray();

				if (NetworkMonitorEnabled)
				{
					var ifaceWidths = new List<int>(NetworkDevices.Columns.Count);
					for (int i = 0; i < NetworkDevices.Columns.Count; i++)
						ifaceWidths.Add(NetworkDevices.Columns[i].Width);
					cols["Interfaces"].IntArray = ifaceWidths.ToArray();
				}

				if (MicrophoneManagerEnabled)
				{
					var micWidths = new List<int>(AudioInputs.Columns.Count);
					for (int i = 0; i < AudioInputs.Columns.Count; i++)
						micWidths.Add(AudioInputs.Columns[i].Width);
					cols["Mics"].IntArray = micWidths.ToArray();
				}

				var uistate = cfg.Config[Constants.Tabs];
				uistate["Open"].Int = TabPages.SelectedIndex;

				var windows = cfg.Config[Constants.Windows];

				var saveBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;

				windows[Constants.Main].IntArray = new int[] { saveBounds.Left, saveBounds.Top, saveBounds.Width, saveBounds.Height };
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}

		#region IDispose
		public bool _Disposed { get; internal set; } = false;

		protected override void Dispose(bool disposing)
		{
			if (_Disposed) return;
			_Disposed = true;

			DestroyHandle();

			if (disposing)
			{
				if (Trace) Log.Verbose("Disposing main window...");

				Logging.DebugMsg("<Window> Log list cache - hits: " + LogListCache.Hits.ToString(CultureInfo.InvariantCulture) + ", misses: " + LogListCache.Misses.ToString(CultureInfo.InvariantCulture) + ", ratio: " + ((float)LogListCache.Hits / LogListCache.Misses).ToString("N1", CultureInfo.InvariantCulture));

				UItimer.Stop();

				if (MemoryLog.MemorySink != null)
					MemoryLog.MemorySink.OnNewEvent -= NewLogReceived; // unnecessary?

				if (powermanager != null)
				{
					powermanager.BehaviourChange -= PowerBehaviourEvent;
					powermanager.PlanChange -= PowerPlanEvent;

					DetachPowerDebug();

					powermanager = null;
				}

				if (cpumonitor != null)
				{
					cpumonitor.Sampling -= CPULoadHandler;
					cpumonitor = null;
				}

				if (hardwaremonitor != null)
				{
					hardwaremonitor.GPUPolling -= GPULoadEvent;
					hardwaremonitor = null;
				}

				if (activeappmonitor != null)
				{
					activeappmonitor.ActiveChanged -= OnActiveWindowChanged;
					activeappmonitor = null;
				}

				if (storagemanager != null)
				{
					storagemanager.TempScan -= TempScanStats;
					storagemanager = null;
				}

				if (processmanager != null)
				{
					UnhookProcessManager();
					processmanager = null;
				}

				if (netmonitor != null)
				{
					UnhookNetwork();
					netmonitor = null;
				}

				if (micmanager != null)
				{
					micmanager.VolumeChanged -= VolumeChangeDetected;
					micmanager = null;
				}

				WatchlistSearchTimer.Dispose();
				UItimer.Dispose();
				ExitWaitList?.Dispose();
				ExitWaitlistMap?.Clear();
				ExitWaitList = null;

				infoTab.Dispose();
				watchTab.Dispose();

				micTab?.Dispose();
				TabPages.Dispose();

				// Pointless
				EventLogList.Dispose();
				MenuToolbar.Dispose();

				/*
				activeLabel.Dispose();
				activeExec.Dispose();
				activeFullscreen.Dispose();
				activePID.Dispose();
				*/
			}

			base.Dispose(disposing);
		}
		#endregion Dispose
		public void ShutdownEvent(object sender, EventArgs ea)
			=> UItimer.Stop();
	}
}