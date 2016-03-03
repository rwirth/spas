using System;
using System.Text;
using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using VRage;
using VRageMath;


class rev9 : Program {
	// =================================[ START OF CONFIGURATION ]================================= 
	static class Configuration {
		// Target average maximum power output to reach (in kW)
		// Default (Vanilla): 119.5f for large ship/station, 29.5f for small ship
		// Allowed values: 0f - 999999999f
		public const float TargetAveragePowerOutput = 119.5f;

		// Delay between executions while aligning (in seconds)
		// Default: 2.0f 
		// Recommended values: 1.5f - 2.5f 
		public const float WorkDelay = 2.0f;

		// Delay between executions while idling (in seconds) 
		// Default: 10.0f 
		// Recommended values: 2.5f - 10.0f 
		public const float IdleDelay = 10.0f;

		// Maximum deviation when comparing angles (in degrees)
		// Default: 1.0f;
		// Recommended values: 0.0001f - 1.0f
		public const float MaxAngleDeviation = 1.0f;

		// Maximum speed of all rotors that are used to align the solar panels (in RPM)
		// Lower values mean higher accuracy but also higher alignment time. 
		// Default: 0.5f 
		// Recommended values: 0.05f - 0.75f
		public const float MaxRotorSpeed = 0.5f;

		// Minimum speed of all rotors that are used to align the solar panels (in RPM)
		// Lower values mean higher accuracy but also higher alignment time.
		// Default: 0.05f
		// Recommended values: 0.05f - 0.75f
		public const float MinRotorSpeed = 0.05f;

		// Name of the solar panel that should be used for optimization
		// The name has to be EXACTLY THE SAME as in the terminal overview. 
		// Default: "Solar Panel (SPAS)" 
		public const string SolarPanelName = "Solar Panel (SPAS)";

		// Determines if the name that was provided for SolarPanelName should be used as group name (true) or block name (false). 
		// Default: false 
		// Allowed values: true, false 
		public const bool SolarPanelName_IsGroup = false;

		// Name of the timer block that should be used for looping
		// The name has to be EXACTLY THE SAME as in the terminal overview. 
		// Default: "Timer Block (SPAS)" 
		public const string TimerName = "Timer Block (SPAS)";

		// Names of all rotors that are connected to the solar panels that should be optimized
		// Each name has to be EXACTLY THE SAME as the corresponding rotor's name in the terminal overview. 
		// Default: { "Advanced Rotor (SPAS)" } 
		// Allowed values: comma-separated list of strings 
		public static readonly string[] RotorNames = { "Vertical Rotor (SPAS)", "Horizontal Rotor (SPAS)" };

		// Determines for each element of RotorNames if the name provided should be used as group name (true) or block name (false). 
		// If this does not have the same amount of elements as RotorNames, each remaining element of RotorNames is treated as block, not as group.
		// Default: { } 
		// Allowed values: comma-separated list of true and false 
		public static readonly bool[] RotorNames_IsGroup = {};

		// Suffix which can be applied to a rotor in a group to make it rotate in the opposite direction. 
		// Use this in combination with rotor groups to set up more than one rotor in one axis. 
		// Default: "[inv]" 
		public const string InvertedRotorSuffix = "[inv]";

		// Configure features like the Energy Saver feature inside this class. 
		public static class Features {
			public static class EnergySaver {
				// Enables the energy saver feature. This will cause rotors to move faster if the power output is low or make them do nothing if the power output is too low. 
				// Default: true 
				// Allowed values: true, false 
				public const bool Enabled = true;

				// Maximum power output at which the script will stay in idle mode (in kW). 
				// Default (Vanilla): 20.0f for large ship/station, 5.0f for small ship 
				// Allowed values: 0.0f - 999999999.9f
				public const float HibernatePowerOutput = 20.0f;

				// Delay between executions while aligning in hibernation mode (in seconds)
				// Default: 1.5f
				// Recommended values: 1.5f - 5f;
				public const float HibernationWorkDelay = 1.5f;

				// Delay between executions while idling in hibernation mode (in seconds)
				// Default: 10.0f
				// Recommended values: 2.5f - 10.0f
				public const float HibernationIdleDelay = 10.0f;
			}

			public static class MaintenanceMode {
				// Enables the maintenance mode feature. Use it to shut this script down temporarily, e.g. for repairs.
				// Default: true
				// Allowed values: true, false
				public const bool Enabled = true;

				// Determines if the script should reinitialize after resuming from maintenance mode. This is needed if you add blocks to a group.
				// Default: true
				// Allowed values: true, false
				public const bool Reinitialize = true;

				// Delay between executions while in maintenance mode (in seconds).
				// Default: 30.0f
				public const float MaintenanceDelay = 30.0f;

				// Suffix which should be used to mark a programmable block as in maintenance mode.
				// Default: " [MAINTENANCE]"
				public const string MaintenanceSuffix = " [MAINTENANCE]";
			}

			public static class LCDOutput {
				public const bool Enabled = true;
				public const string LCDPanelName = "Text panel (SPAS)";
				public const int MaxRows = 15;
			}
		}

		// Configure your language by editing the values inside this class to the texts that are displayed inside your game. 
		public static class Localization {
			// Text that is located before the maximum power output value in the detailed description of the solar panel, including whitespaces. 
			// English: "Max Output: " 
			public const string MaxOutput = "Max Output: ";

			// Text that is located before the current power output value in the detailed description of the solar panel, including whitespaces. 
			// English: "Current Output: " 
			public const string CurrentOutput = "Current Output: ";
		}
	}
	// =================================[ END OF CONFIGURATION ]================================= 

	static class Status { public const int HIBERNATING = -1, IDLING = 0, UPDATING = 1, TESTING = 2, ALIGNING = 3; }

	float currentPower = 0.0f;
	float currentDifference = 0.0f;
	int currentRotorIndex = 0;
	int currentDirection = 1;
	int currentStatus = Status.UPDATING;
	IMyTimerBlock timer = null;
	IMyTextPanel lcdPanel = null;
	List<IMyMotorStator> currentRotors = new List<IMyMotorStator>();
	List<IMySolarPanel> solarPanels = new List<IMySolarPanel>();
	List<List<IMyMotorStator>> rotors = new List<List<IMyMotorStator>>();
	static List<string> messages = new List<string>();

	void Main(string argument) {
		Utilities.Me = Me;
		Utilities.Console = Echo;
		Utilities.UpdateText = UpdateText;

		InitializeLCDPanel();

		if (Configuration.Features.MaintenanceMode.Enabled && (argument + " ").Contains("maintenance ")) {
			if (Me.CustomName.EndsWith(Configuration.Features.MaintenanceMode.MaintenanceSuffix)) {
				Me.SetCustomName(Me.CustomName.Substring(0, Me.CustomName.Length - Configuration.Features.MaintenanceMode.MaintenanceSuffix.Length));
				Utilities.Echo("This programmable block is no longer in maintenance mode.");

				InitializeSolarPanels(Configuration.Features.MaintenanceMode.Reinitialize);
				InitializeRotors(Configuration.Features.MaintenanceMode.Reinitialize);
				InitializeTimer(Configuration.Features.MaintenanceMode.Reinitialize);
				InitializeLCDPanel(Configuration.Features.MaintenanceMode.Reinitialize);
			} else {
				Me.SetCustomName(Me.CustomName + Configuration.Features.MaintenanceMode.MaintenanceSuffix);
				Utilities.Echo("This programmable block is now in maintenance mode.");
			}
			return;
		} else {
			InitializeSolarPanels();
			InitializeRotors();
			InitializeTimer();
		}

		if (Me.CustomName.EndsWith(Configuration.Features.MaintenanceMode.MaintenanceSuffix)) {
			Utilities.Echo("MAINTENANCE MODE");
			Utilities.Trigger(timer, Configuration.Features.MaintenanceMode.MaintenanceDelay);
			return;
		}

		float oldPower = currentPower;
		float oldDifference = currentDifference;
		currentPower = Utilities.AverageMaxOutput(solarPanels);
		currentDifference = (float)Math.Round(currentPower - oldPower, 1);

		Utilities.Echo("dP = " + currentDifference + "kW");
		Utilities.Echo("ddP = " + Math.Round(currentDifference - oldDifference, 2) + "kW");

		Work(oldPower, oldDifference);
	}

	void InitializeSolarPanels(bool forced = false) {
		if (solarPanels.Count > 0 && !forced) return;

		if (Configuration.SolarPanelName_IsGroup) InitializeSolarPanelGroup();
		else InitializeSolarPanel();

		Utilities.Echo("initialized solar panels");
	}

	void InitializeSolarPanelGroup() {
		List<IMyBlockGroup> groups = new List<IMyBlockGroup>();
		GridTerminalSystem.GetBlockGroups(groups);

		for (int i = 0; i < groups.Count; i++) {
			IMyBlockGroup group = groups[i];
			if (group.Name.Equals(Configuration.SolarPanelName)) {
				for (int j = 0; j < group.Blocks.Count; j++) {
					IMySolarPanel solarPanel = group.Blocks[j] as IMySolarPanel;
					if (solarPanel != null) solarPanels.Add(solarPanel);
				}
				break;
			}
		}

		if (solarPanels.Count <= 0) throw new Exception(" InitializeSolarPanelGroup(): failed to find any solar panels in the group \"" + Configuration.SolarPanelName + "\"");
	}

	void InitializeSolarPanel() {
		List<IMyTerminalBlock> panels = new List<IMyTerminalBlock>();
		GridTerminalSystem.GetBlocksOfType<IMySolarPanel>(panels);

		for (int i = 0; i < panels.Count; i++) {
			IMySolarPanel solarPanel = panels[i] as IMySolarPanel;
			if (solarPanel != null && solarPanel.CustomName.StartsWith(Configuration.SolarPanelName)) {
				solarPanels.Add(solarPanel);
				break;
			}
		}

		if (solarPanels.Count <= 0) throw new Exception(" InitializeSolarPanel(): failed to find solar panel with name \"" + Configuration.SolarPanelName + "\"");
	}

	void InitializeRotors(bool forced = false) {
		if (rotors.Count > 0 && !forced) return;

		for (int i = 0; i < Configuration.RotorNames.Length; i++) {
			List<IMyMotorStator> axis = new List<IMyMotorStator>();
			string name = Configuration.RotorNames[i];
			bool isGroup = i >= Configuration.RotorNames_IsGroup.Length? false : Configuration.RotorNames_IsGroup[i];

			if (isGroup) InitializeRotorGroup(axis, name);
			else InitializeRotor(axis, name);

			rotors.Add(axis);
		}

		Utilities.Echo("initialized rotors");
	}

	void InitializeRotorGroup(List<IMyMotorStator> axis, string groupName) {
		List<IMyBlockGroup> groups = new List<IMyBlockGroup>();
		GridTerminalSystem.GetBlockGroups(groups);
		bool foundGroup = false;

		for (int j = 0; j < groups.Count; j++) {
			IMyBlockGroup group = groups[j];
			if (group.Name.Equals(groupName)) {
				foundGroup = true;
				for (int k = 0; k < group.Blocks.Count; k++) {
					IMyMotorStator rotor = group.Blocks[k] as IMyMotorStator;
					if (rotor != null) {
						axis.Add(rotor);
						Utilities.ToggleOff(rotor);
						Utilities.SetTorque(rotor, Utilities.MaximumTorque);
						Utilities.SetBrakingTorque(rotor, Utilities.MaximumTorque);
					}
				}
				break;
			}
		}

		if (!foundGroup) throw new Exception(" InitializeRotorGroup(List<>, string): failed to find group with name \"" + groupName + "\"");
		if (axis.Count <= 0) throw new Exception(" InitializeRotorGroup(List<>, string): failed to find any rotors in the group \"" + groupName + "\"");
	}

	void InitializeRotor(List<IMyMotorStator> axis, string rotorName) {
		IMyMotorStator rotor = GridTerminalSystem.GetBlockWithName(rotorName) as IMyMotorStator;
		if (rotor == null) {
			string message = " InitializeRotor(List<>, string): failed to find rotor with name \"" + rotorName + "\"";
			if (rotorName.Contains(",")) {
				message += "\n\nDid you want to write\nRotorNames = { [...]\"";
				string[] split = rotorName.Split(',');
				for (int k = 0; k < split.Length; k++) {
					message += split[k];
					if (k < split.Length - 1) message += "\", \"";
				}
				message += "\" [...] };\ninstead?";
			}
			throw new Exception(message);
		}
		axis.Add(rotor);
		Utilities.ToggleOff(rotor);
		Utilities.SetTorque(rotor, Utilities.MaximumTorque);
		Utilities.SetBrakingTorque(rotor, Utilities.MaximumTorque);
	}

	void InitializeTimer(bool forced = false) {
		if (timer != null && !forced) return;

		timer = GridTerminalSystem.GetBlockWithName(Configuration.TimerName) as IMyTimerBlock;
		if (timer == null) throw new Exception("InitializeTimer(): failed to find timer block with name \"" + Configuration.TimerName + "\"");

		Utilities.Echo("initialized timer");
	}

	void InitializeLCDPanel(bool forced = false) {
		if (!Configuration.Features.LCDOutput.Enabled) return;
		if (lcdPanel != null && !forced) return;

		List<IMyTerminalBlock> panels = new List<IMyTerminalBlock>();
		GridTerminalSystem.GetBlocksOfType<IMyTextPanel>(panels);

		for (int i = 0; i < panels.Count; i++) {
			IMyTextPanel panel = panels[i] as IMyTextPanel;
			if (panel != null && panel.CustomName.StartsWith(Configuration.Features.LCDOutput.LCDPanelName)) {
				lcdPanel = panel;
				break;
			}
		}

		if (lcdPanel == null) throw new Exception(" InitializeLCDPanel(): failed to find LCD panel with name \"" + Configuration.Features.LCDOutput.LCDPanelName + "\"");
		Utilities.Echo("initialized LCD panel");
	}

	void Work(float oldPower, float oldDifference) {
		if (Configuration.Features.EnergySaver.Enabled) {
			if (currentPower <= Configuration.Features.EnergySaver.HibernatePowerOutput && currentStatus != Status.TESTING) {
				Hibernate();
				return;
			}
		}

		if (currentPower >= Configuration.TargetAveragePowerOutput) {
			Idle();
			return;
		}

		if (currentStatus == Status.UPDATING || currentStatus == Status.IDLING || currentStatus == Status.HIBERNATING) {
			currentRotors = rotors[currentRotorIndex];
			UpdateNames();
			Utilities.SetSpeed(currentRotors, GetRotorSpeed());
			Utilities.ToggleOn(currentRotors);
			currentStatus = Status.TESTING;
			Utilities.Trigger(timer, Configuration.WorkDelay);
			return;
		}

		if (currentStatus == Status.TESTING) {
			Utilities.SetSpeed(currentRotors, GetRotorSpeed());
			Utilities.ToggleOff(currentRotors);

			UpdateNames(out oldPower);
			if (currentPower < oldPower) {
				currentDirection = -currentDirection;
				Utilities.SetSpeed(currentRotors, GetRotorSpeed());
			}

			currentStatus = Status.ALIGNING;
		}

		if (currentStatus == Status.ALIGNING) {
			Utilities.SetSpeed(currentRotors, GetRotorSpeed());
			Utilities.ToggleOn(currentRotors);

			UpdateNames(out oldPower);
			if (currentPower < oldPower) {
				if (rotors.Count > 1) {
					Utilities.ToggleOff(currentRotors);
					currentStatus = Status.UPDATING;
					currentRotorIndex = (currentRotorIndex + 1) % rotors.Count;
				} else {
					currentDirection = -currentDirection;
					Utilities.SetSpeed(currentRotors, GetRotorSpeed());
				}
			}

			Utilities.Trigger(timer, Configuration.WorkDelay);
			return;
		}
	}

	void Idle() {
		for (int i = 0; i < rotors.Count; i++) Utilities.ToggleOff(rotors[i]);
		UpdateNames();
		currentStatus = Status.IDLING;
		Utilities.Trigger(timer, Configuration.IdleDelay);
	}

	void Hibernate() {
		bool idle = true;
		float speed = GetRotorSpeed();
		for (int i = 0; i < rotors.Count; i++) {
			List<IMyMotorStator> axis = rotors[i];
			for (int j = 0; j < axis.Count; j++) {
				IMyMotorStator rotor = axis[j];
				if (Utilities.ReachedLowerLimit(rotor) || Utilities.ReachedUpperLimit(rotor)) Utilities.ToggleOff(rotor);
				else {
					float distanceFromUpperLimit = rotor.UpperLimit - rotor.Angle;
					float distanceFromLowerLimit = rotor.Angle - rotor.LowerLimit;

					Utilities.ToggleOn(rotor);
					if (distanceFromLowerLimit < distanceFromUpperLimit) Utilities.RotateTowards(rotor, Utilities.ToDegrees(rotor.UpperLimit), speed);
					else Utilities.RotateTowards(rotor, Utilities.ToDegrees(rotor.LowerLimit), speed);

					idle = false;
				}
			}
		}
		UpdateNames();
		currentStatus = Status.HIBERNATING;
		Utilities.Trigger(timer, idle ? Configuration.Features.EnergySaver.HibernationIdleDelay : Configuration.Features.EnergySaver.HibernationWorkDelay);
	}

	void UpdateText() {
		if (!Configuration.Features.LCDOutput.Enabled) return;

		List<string> info = new List<string>();
		double current = Math.Round(Utilities.TotalCurrentOutput(solarPanels), 2);
		double max = Math.Round(Utilities.TotalMaxOutput(solarPanels), 2);
		info.Add("Current output: " + current + "kW / " + max + " kW");

		string status = "ERROR";
		if (currentStatus == Status.TESTING) status = "Testing";
		else if (currentStatus == Status.UPDATING) status = "Updating";
		else if (currentStatus == Status.ALIGNING) status = "Aligning";
		else if (currentStatus == Status.IDLING) status = "Idling";
		else if (currentStatus == Status.HIBERNATING) status = "Hibernating";
		info.Add("Current status: " + status);
		info.Add("Current speed: " + Math.Round(GetRotorSpeed(), 2) + "RPM");
		info.Add("");

		if (messages.Count > Configuration.Features.LCDOutput.MaxRows - info.Count) messages.RemoveRange(0, messages.Count - (Configuration.Features.LCDOutput.MaxRows - info.Count));
		info.AddRange(messages);
		Utilities.Write(lcdPanel, info);
	}

	void UpdateNames() {
		float __ignored;
		UpdateNames(out __ignored);
	}

	void UpdateNames(out float oldPower) {
		oldPower = 0.0f;
		for (int i = 0; i < solarPanels.Count; i++) {
			IMySolarPanel panel = solarPanels[i];

			float panelOutput = Utilities.MaxOutput(panel);
			string[] split = panel.CustomName.Split('~');
			if (split.Length <= 1) {
				panel.SetCustomName(panel.CustomName + "~" + panelOutput);
				continue;
			}

			float panelOldOutput;
			float.TryParse(split[split.Length - 1], out panelOldOutput);

			string name = "";
			for (int k = 0; k < split.Length - 1; k++) name += split[k];
			name += "~" + panelOutput;
			panel.SetCustomName(name);

			if (oldPower == 0) oldPower = panelOldOutput;
			else oldPower = (oldPower + panelOldOutput) / 2;
		}
	}

	float GetRotorSpeed() {
		float min = Configuration.Features.EnergySaver.Enabled ? Configuration.Features.EnergySaver.HibernatePowerOutput : 0;
		float max = Configuration.TargetAveragePowerOutput - min;
		return Utilities.EaseInOut(currentPower - min, Configuration.MaxRotorSpeed, Configuration.MinRotorSpeed - Configuration.MaxRotorSpeed, max * 1.1f) * currentDirection;
	}

	static class Utilities {
		public static IMyProgrammableBlock Me;
		public static Action<string> Console;
		public static Action UpdateText;

		public static float MaximumTorque {
			get {
				if (Me.CubeGrid.GridSizeEnum == VRage.Game.MyCubeSize.Large) return 33600000;
				return 448000;
			}
		}

		public static void Echo(string line) {
			Console(line);
			messages.Add(line);
			UpdateText();
		}

		public static void ToggleOn(IMyFunctionalBlock block) { block.GetActionWithName("OnOff_On").Apply(block); }

		public static void ToggleOn(List<IMyMotorStator> blocks) {
			for (int i = 0; i < blocks.Count; i++) {
				ToggleOn(blocks[i]);
			}
		}

		public static void ToggleOff(IMyFunctionalBlock block) { block.GetActionWithName("OnOff_Off").Apply(block); }

		public static void ToggleOff(List<IMyMotorStator> blocks) {
			for (int i = 0; i < blocks.Count; i++) {
				ToggleOff(blocks[i]);
			}
		}

		public static void Trigger(IMyTimerBlock timer, float delay) {
			UpdateText();
			timer.SetValue("TriggerDelay", delay);
			timer.GetActionWithName("Start").Apply(timer);
		}

		public static void SetSpeed(IMyMotorStator rotor, float speed) {
			if (rotor.CustomName.EndsWith(Configuration.InvertedRotorSuffix)) speed = -speed;
			rotor.SetValue("Velocity", speed);
		}

		public static void SetSpeed(List<IMyMotorStator> rotors, float speed) {
			for (int i = 0; i < rotors.Count; i++) {
				SetSpeed(rotors[i], speed);
			}
		}

		public static void SetTorque(IMyMotorStator rotor, float torque) {
			float val = Math.Min(MaximumTorque, Math.Max(0, torque));
			rotor.SetValue("Torque", val);
		}

		public static void SetBrakingTorque(IMyMotorStator rotor, float torque) {
			float val = Math.Min(MaximumTorque, Math.Max(0, torque));
			rotor.SetValue("BrakingTorque", val);
		}

		public static void RotateTowards(IMyMotorStator rotor, float target, float speed) {
			float angle = ToDegrees(rotor.Angle);
			if (RotationEquals(angle, target)) SetSpeed(rotor, 0);
			float posrot = (target + 360 - angle) % 360;
			float negrot = (angle + 360 - target) % 360;
			if (posrot < negrot) {
				if (HasUpperLimit(rotor) && rotor.UpperLimit <= target) SetSpeed(rotor, -speed);
				else SetSpeed(rotor, speed);
			} else {
				if (HasLowerLimit(rotor) && rotor.LowerLimit >= target) SetSpeed(rotor, speed);
				else SetSpeed(rotor, -speed);
			}
		}

		public static bool RotationEquals(float a, float b) { return Math.Abs(a - b) <= Configuration.MaxAngleDeviation; }

		public static bool HasLowerLimit(IMyMotorStator rotor) { return !float.IsInfinity(rotor.LowerLimit); }

		public static bool HasUpperLimit(IMyMotorStator rotor) { return !float.IsInfinity(rotor.UpperLimit); }

		public static bool ReachedLowerLimit(IMyMotorStator rotor) { return !HasLowerLimit(rotor) || (HasLowerLimit(rotor) && RotationEquals(ToDegrees(rotor.Angle), ToDegrees(rotor.LowerLimit))); }

		public static bool ReachedUpperLimit(IMyMotorStator rotor) { return !HasUpperLimit(rotor) || (HasUpperLimit(rotor) && RotationEquals(ToDegrees(rotor.Angle), ToDegrees(rotor.UpperLimit))); }

		public static float ToDegrees(float radians) { return (float)(radians / Math.PI) * 180; }

		public static float MaxOutput(IMySolarPanel panel) {
			float power = panel.MaxOutput;
			return power * 1000;
		}

		public static float CurrentOutput(IMySolarPanel panel) {
			float power = panel.CurrentOutput;
			return power * 1000;
		}

		public static float AverageMaxOutput(List<IMySolarPanel> panels) {
			float power = 0.0f;
			for (int i = 0; i < panels.Count; i++) {
				IMySolarPanel panel = panels[i];

				float maxOutput = MaxOutput(panel);
				if (power == 0) power = maxOutput;
				else power = (power + maxOutput) / 2;
			}
			return power;
		}

		public static float TotalMaxOutput(List<IMySolarPanel> panels) {
			float power = 0.0f;
			for (int i = 0; i < panels.Count; i++) {
				IMySolarPanel panel = panels[i];
				power += MaxOutput(panel);
			}
			return power;
		}

		public static float TotalCurrentOutput(List<IMySolarPanel> panels) {
			float power = 0.0f;
			for (int i = 0; i < panels.Count; i++) {
				IMySolarPanel panel = panels[i];
				power += CurrentOutput(panel);
			}
			return power;
		}

		public static float GetFontSize(IMyTextPanel panel) {
			if (panel == null) return 0;
			return panel.GetValueFloat("FontSize");
		}

		public static void SetFontSize(IMyTextPanel panel, float fontSize) {
			if (panel == null) return;
			panel.SetValue("FontSize", Math.Max(fontSize, 0.0f));
		}

		public static void Write(IMyTextPanel panel, List<string> text) {
			int width = 658;
			int y = text.Count;
			int x = 0;
			for (int i = 0; i < text.Count; i++) x = Math.Max(x, text[i].Length);

			float fontSize = GetFontSize(panel);

			if (fontSize < 0.25f) fontSize = 1.0f;
			fontSize = Math.Min(fontSize, Math.Max(0.5f, width * 10 / x / 10.0f));
			fontSize = Math.Min(fontSize, Math.Max(0.5f, 176 / y / 10.0f));
			width = (int)(width / fontSize);
			int height = (int)(17.6f / fontSize);
			SetFontSize(panel, fontSize);

			StringBuilder sb = new StringBuilder();
			for (int i = 0; i < text.Count; i++) {
				string current = text[i];
				while (current.Length > width) {
					sb.Append(current.Substring(0, width));
					current = current.Substring(width);
				}
				sb.Append(current).Append("\n");
			}

			panel.WritePublicText(sb.ToString());
			panel.ShowPublicTextOnScreen();
		}

		// source: http://gizma.com/easing/
		public static float EaseInOut(float t, float s, float c, float d) {
			t /= d / 2;
			if (t < 1) return c / 2 * t * t + s;
			t--;
			return -c / 2 * (t * (t - 2) - 1) + s;
		}
	}
}
