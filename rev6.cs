using System;
using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using Sandbox.Common.ObjectBuilders;
using VRage;
using VRageMath;

class rev6 : Program {
	// =================================[ START OF CONFIGURATION ]=================================
	static class Configuration {
		// Target maximum power output to reach (in kW).
		// Default (Vanilla): 119 for large ship/station, 29 for small ship
		// Allowed values: 0 - 999999999
		public const int TargetPowerOutput = 119;

		// Delay between executions while aligning (in seconds).
		// Default: 2.0f
		// Recommended values: 1.5f - 2.5f
		public const float WorkDelay = 2.0f;

		// Delay between executions while idling (in seconds)
		// Default: 10.0f
		// Recommended values: 2.5f - 10.0f
		public const float IdleDelay = 10.0f;

		// Speed of all rotors that are used to align the solar panels (in RPM).
		// Lower values mean higher accuracy but also higher alignment time.
		// Default: 0.1f
		// Recommended values: 0.05f - 0.5f
		public const float RotorSpeed = 0.1f;

		// Name of the solar panel that should be used for optimization.
		// The name has to be EXACTLY THE SAME as in the terminal overview.
		// Default: "Solar Panel (optimized)"
		public const string ReferencePanelName = "Solar Panel (optimized)";

		// Name of the timer block that should be used for looping.
		// The name has to be EXACTLY THE SAME as in the terminal overview.
		// Default: "Loop Timer"
		public const string TimerName = "Loop Timer";

		// Names of all rotors that are connected to the solar panels that should be optimized.
		// Each name has to be EXACTLY THE SAME as the corresponding rotor's name in the terminal overview.
		// Default: { "Advanced Rotor" }
		public static readonly string[] RotorNames = new string[] { "Advanced Rotor" };

		// Configure features like the Energy Saver feature inside this class.
		public static class Features {
			public static class EnergySaver {
				// Enables the energy saver feature. This will cause rotors to move faster if the power output is low or make them do nothing if the power output is too low.
				// Default: true
				// Allowed values: true, false
				public const bool Enabled = true;

				// Maximum power output at which the script will stay in idle mode (in kW).
				// Default (Vanilla): 20 for large ship/station, 5 for small ship
				// Allowed values: 0 - 999999999
				public const int HibernatePowerOutput = 20;

				// Maximum power output at which the rotors will be accelerated by the panic speed multiplier (in kW).
				// Default (Vanilla): 60 for large ship/station, 15 for small ship
				// Allowed values: 0 - 999999999
				public const int PanicPowerOutput = 60;

				// Value by which the default rotor speed will be multiplied while in panic mode..
				// Default: 2.0f
				// Recommended values: 1.0f - 5.0f
				public const float PanicSpeedMultiplier = 2.0f;
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

	static class Status { public const uint UPDATING = 1, TESTING = 2, ALIGNING = 3; }

	int CurrentRotorIndex = 0;
	uint CurrentStatus = Status.UPDATING;
	IMySolarPanel ReferencePanel;
	IMyTimerBlock Timer;
	IMyMotorStator CurrentRotor;
	List<IMyMotorStator> Rotors = new List<IMyMotorStator>();

	void Main() {
		// initialize the reference solar panel
		if (ReferencePanel == null) {
			// get a list of all available solar panels
			List<IMyTerminalBlock> solarPanels = new List<IMyTerminalBlock>();
			GridTerminalSystem.GetBlocksOfType<IMySolarPanel>(solarPanels);

			// search all available blocks for one containing the reference solar panel's name
			for (int i = 0; i < solarPanels.Count; i++) {
				IMySolarPanel solarPanel = solarPanels[i] as IMySolarPanel;
				if (solarPanel != null && solarPanel.CustomName.StartsWith(Configuration.ReferencePanelName)) {
					ReferencePanel = solarPanel;
					if (ReferencePanel != null) break;
				}
			}
			if (ReferencePanel == null) throw new Exception(" Main(): Failed to find solar panel with name \"" + Configuration.ReferencePanelName + "\"");
		}

		// initialize the timer
		if (Timer == null) {
			Timer = GridTerminalSystem.GetBlockWithName(Configuration.TimerName) as IMyTimerBlock;
			if (Timer == null) throw new Exception(" Main(): Failed to find timer block with name \"" + Configuration.TimerName + "\"");
		}

		// initialize rotors list
		if (Rotors.Count <= 0) {
			for (int i=0; i < Configuration.RotorNames.Length; i++) {
				IMyMotorStator rotor = GridTerminalSystem.GetBlockWithName(Configuration.RotorNames[i]) as IMyMotorStator;

				// only add rotors that exist and haven't been added yet
				if (rotor != null && !Rotors.Contains(rotor)) {
					Rotors.Add(rotor);
					rotor.SetValue("Velocity", Configuration.RotorSpeed);
					ToggleOff(rotor);
				}
			}
			if (Rotors.Count < Configuration.RotorNames.Length) throw new Exception(" Main(): Failed to find all rotors - found " + Rotors.Count + " rotors, " + Configuration.RotorNames.Length + " were specified");
		}

		// rotate the current rotor
		float currentPower;
		if (!MaxOutput(ReferencePanel, out currentPower)) throw new Exception(" Main(): Failed to read maximum power output from the solar panel's information");

		// check if the energy saver feature has been enabled
		if (Configuration.Features.EnergySaver.Enabled) {
			// check if there's enough power output to operate reasonably
			if (currentPower <= Configuration.Features.EnergySaver.HibernatePowerOutput) {
				Hibernate();
				return;
			}

			// check if the power output is low enough to enable panic mode
			Panic(currentPower <= Configuration.Features.EnergySaver.PanicPowerOutput);
		}

		// check if the target power output has been reached
		if (currentPower >= Configuration.TargetPowerOutput) {
			Hibernate();
			return;
		}

		// update the rotor and test which direction yields the higher power output
		if (CurrentRotor == null || CurrentStatus == Status.UPDATING) {
			CurrentRotor = Rotors[CurrentRotorIndex];
			UpdateName(ReferencePanel);
			ToggleOn(CurrentRotor);
			CurrentStatus = Status.TESTING;
			TriggerTimer();
			return;
		}

		// set the direction towards the higher power output
		if (CurrentStatus == Status.TESTING) {
			ToggleOff(CurrentRotor);
			float oldPower;
			UpdateName(ReferencePanel, out oldPower, out currentPower);
			if (oldPower > currentPower) Reverse(CurrentRotor);
			CurrentStatus = Status.ALIGNING;
		}

		// rotate towards maximum power output
		if (CurrentStatus == Status.ALIGNING) {
			ToggleOn(CurrentRotor);
			float oldPower;
			UpdateName(ReferencePanel, out oldPower, out currentPower);
			if (oldPower > currentPower) {
				if (Rotors.Count > 1) {
					ToggleOff(CurrentRotor);
					CurrentStatus = Status.UPDATING;
					CurrentRotorIndex = (CurrentRotorIndex + 1) % Rotors.Count;
				} else {
					Reverse(CurrentRotor);
				}
			}
			TriggerTimer();
			return;
		}
	}

	void Hibernate() {
		for (int i=0; i < Rotors.Count; i++) {
			ToggleOff(Rotors[i]);
		}
		UpdateName(ReferencePanel);
		TriggerTimerIdle();
	}

	void Panic(bool activate) {
		for (int i=0; i < Rotors.Count; i++) {
			Rotors[i].SetValue("Velocity", Math.Sign(Rotors[i].Velocity) * Configuration.RotorSpeed * (activate ? Configuration.Features.EnergySaver.PanicSpeedMultiplier : 1.0f));
		}
	}

	void TriggerTimer() {
		TriggerTimer(Timer, Configuration.WorkDelay);
	}

	void TriggerTimerIdle() {
		TriggerTimer(Timer, Configuration.IdleDelay);
	}

	static void Reverse(IMyMotorStator rotor) {
		rotor.GetActionWithName("Reverse").Apply(rotor);
	}

	static void TriggerTimer(IMyTimerBlock timer, float delay) {
		timer.SetValue("TriggerDelay", delay);
		timer.GetActionWithName("Start").Apply(timer);
	}

	static void UpdateName(IMySolarPanel solarPanel) {
		float oldPower, currentPower;
		UpdateName(solarPanel, out oldPower, out currentPower);
	}

	static void UpdateName(IMySolarPanel solarPanel, out float oldPower, out float currentPower) {
		if (solarPanel == null) throw new Exception(" UpdateName(IMySolarPanel, out float, out float): solarPanel is null");
		if (!MaxOutput(solarPanel, out currentPower)) throw new Exception(" UpdateName(IMySolarPanel, out float, out float): Failed to read maximum power output from the solar panel's information");

		string[] array = solarPanel.CustomName.Split('~');
		if (array.Length > 1) {
			// old power was set
			float.TryParse(array[array.Length - 1], out oldPower);

			// update current power output
			string newName = "";
			for (int i=0; i < array.Length - 1; i++) {
				newName += array[i];
			}
			newName += "~" + currentPower;
			solarPanel.SetCustomName(newName);
			return;
		}

		oldPower = 0.0f;
		solarPanel.SetCustomName(solarPanel.CustomName + " ~" + currentPower);
	}

	static void ToggleOff(IMyFunctionalBlock block) {
		if (block == null) throw new Exception(" ToggleOff(IMyFunctionalBlock): block is null");
		block.GetActionWithName("OnOff_Off").Apply(block);
	}

	static void ToggleOn(IMyFunctionalBlock block) {
		if (block == null) throw new Exception(" ToggleOn(IMyFunctionalBlock): block is null");
		block.GetActionWithName("OnOff_On").Apply(block);
	}

	static bool MaxOutput(IMySolarPanel solarPanel, out float power) {
		if (solarPanel == null) throw new Exception(" MaxOutput(IMySolarPanel, out float): solarPanel is null");

		int start = StartIndexMaxOutput(solarPanel);
		int end = StartIndexCurrentOutput(solarPanel) - Configuration.Localization.CurrentOutput.Length;
		string maxOutput = solarPanel.DetailedInfo.Substring(start, end - start);

		if (float.TryParse(System.Text.RegularExpressions.Regex.Replace(maxOutput, @"[^0-9.]", ""), out power)) {
			// convert to kW
			if (maxOutput.Contains(" W")) {
				// W -> kW: * 0.001
				power *= 0.001f;
			} else if (maxOutput.Contains(" kW")) {
				// kW -> kW: * 1
				power *= 1f;
			} else if (maxOutput.Contains(" MW")) {
				// MW -> kW: * 1000
				power *= 1000f;
			} else if (maxOutput.Contains(" GW")) {
				// GW -> kW: * 1000000
				power *= 1000000f;
			} else throw new Exception(" MaxOutput(IMySolarPanel, out float): maximum power output is too high (" + maxOutput + ")");

			return true;
		}

		return false;
	}

	[Obsolete("Currently not used.")]
	static bool CurrentOutput(IMySolarPanel solarPanel, out float power) {
		if (solarPanel == null) throw new Exception(" CurrentOutput(IMySolarPanel, out float): solarPanel is null");

		int start = StartIndexCurrentOutput(solarPanel);
		int end = solarPanel.DetailedInfo.Length;
		string currentOutput = solarPanel.DetailedInfo.Substring(start, end - start);

		if (float.TryParse(System.Text.RegularExpressions.Regex.Replace(currentOutput, @"[^0-9.]", ""), out power)) {
			// convert to kW
			if (currentOutput.Contains(" W")) {
				// W -> kW: * 0.001
				power *= 0.001f;
			} else if (currentOutput.Contains(" kW")) {
				// kW -> kW: * 1
				power *= 1f;
			} else if (currentOutput.Contains(" MW")) {
				// MW -> kW: * 1000
				power *= 1000f;
			} else if (currentOutput.Contains(" GW")) {
				// GW -> kW: * 1000000
				power *= 1000000f;
			} else throw new Exception(" CurrentOutput(IMySolarPanel, out float): current power output is too high (" + currentOutput + ")");

			return true;
		}

		return false;
	}

	static int StartIndexMaxOutput(IMySolarPanel solarPanel) {
		if (solarPanel == null) throw new Exception(" StartIndexMaxOutput(IMySolarPanel): solarPanel is null");
		int ret = solarPanel.DetailedInfo.IndexOf(Configuration.Localization.MaxOutput);
		if (ret < 0) throw new Exception(" StartIndexMaxOutput(IMySolarPanel): incompatible solar panel");
		return ret + Configuration.Localization.MaxOutput.Length;
	}

	static int StartIndexCurrentOutput(IMySolarPanel solarPanel) {
		if (solarPanel == null) throw new Exception(" StartIndexCurrentOutput(IMySolarPanel): solarPanel is null");
		int ret = solarPanel.DetailedInfo.IndexOf(Configuration.Localization.CurrentOutput);
		if (ret < 0 || ret <= StartIndexMaxOutput(solarPanel)) throw new Exception(" StartIndexCurrentOutput(IMySolarPanel): incompatible solar panel");
		return ret + Configuration.Localization.CurrentOutput.Length;
	}
}