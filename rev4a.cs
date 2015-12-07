using System;
using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using Sandbox.Common.ObjectBuilders;
using VRage;
using VRageMath;

class rev4a : Program {
	// Target power output to reach (in kW)
	// Default (Vanilla): 119 for large ship/station, 29 for small ship
	const int targetPowerOutput = 119;

	// Time to wait between two checks (in seconds)
	// Default: 2.0f
	const float loopDelay = 2.0f;

	// Time to wait if target power output has been reached (in seconds)
	// Default: 10.0f
	const float idleDelay = 10.0f;

	// Rotor speed (in RPM). Decreasing this value will increase accuracy.
	// Default: 0.1f
	const float rotorSpeed = 0.1f;

	// Solar panel to use as reference value for optimization
	const string referencePanelName = "Solar Panel (optimized)";

	// Timer to use for looping
	const string timerName = "Loop Timer";

	// Text that stands before the maximum power output amount in the detailed description of the solar panel
	// English: "Max Output: "
	const string lang_maxOutput = "Max Output: ";

	// Text that stands before the current power output amount in the detailed description of the solar panel
	// English: "Current Output: "
	const string lang_currentOutput = "Current Output: ";

	// Rotors to rotate for solar panel power output optimization
	readonly string[] rotorNames = new string[] { "Advanced Rotor" };

	// ------------------------------------------------[ END OF CONFIGURATION ]------------------------------------------------

	bool updatedRotor = false;
	bool testingDirection = false;
	bool setting = true;
	int currentIndex = 0;
	IMySolarPanel referencePanel;
	IMyTimerBlock timer;
	IMyMotorStator currentRotor;
	List<IMyTerminalBlock> rotors = new List<IMyTerminalBlock>();

	void Main() {
		// get a list of all blocks in the grid terminal system
		List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
		GridTerminalSystem.GetBlocks(blocks);

		// initialize the reference solar panel if it hasn't been initialized yet
		if (referencePanel == null) {
			// search all available blocks for one containing the reference panel name
			for (int i = 0; i < blocks.Count; i++) {
				if (blocks[i].CustomName.Contains(referencePanelName)) {
					referencePanel = blocks[i] as IMySolarPanel;
					if (referencePanel != null) break;
				}
			}
			if (referencePanel == null) throw new Exception(" Main(): failed to find solar panel with name '" + referencePanelName + "'");
		}

		// initialize the timer if it hasn't been initialized yet
		if (timer == null) {
			// search all available blocks for one containing the timer name
			for (int i = 0; i < blocks.Count; i++) {
				if (blocks[i].CustomName.Contains(timerName)) {
					timer = blocks[i] as IMyTimerBlock;
					if (timer != null)
						break;
				}
			}
			if (timer == null) throw new Exception(" Main(): failed to find timer block with name '" + timerName + "'");
		}

		// initialize the rotors list if no rotors have been registered yet
		if (rotors.Count <= 0) {
			for (int i = 0; i < rotorNames.Length; i++) {
				IMyTerminalBlock rotor = GridTerminalSystem.GetBlockWithName(rotorNames[i]);

				// only add rotors that actually exist and haven't been added yet
				if (rotor != null && !rotors.Contains(rotor)) {
					rotors.Add(rotor);
					rotor.SetValue("Velocity", rotorSpeed);
					ToggleOff(rotor);
				}
			}
			if (rotors.Count <= 0) throw new Exception(" Main(): failed to find any rotors with the specified names");
		}

		// rotate the current rotor
		float currentPower;
		if (!MaxOutput(referencePanel, out currentPower)) throw new Exception(" Main(): failed to read current power output from DetailedInfo");
		if (currentPower >= targetPowerOutput) {
			for (int i = 0; i < rotors.Count; i++) {
				ToggleOff(rotors[i]);
			}
			UpdateName(referencePanel);
			TriggerTimerIdle();
			return;
		}
		if (currentRotor == null) {
			currentRotor = rotors[currentIndex] as IMyMotorStator;
			updatedRotor = true;
		}
		if (currentRotor == null) throw new Exception(" Main(): block '" + rotors[currentIndex].CustomName + "' is not a rotor but was registered as rotor to use");

		if (updatedRotor) {
			if (!testingDirection) {
				// set the best rotation direction
				UpdateName(referencePanel);
				ToggleOn(currentRotor);
				testingDirection = true;
				TriggerTimer();
				return;
			}

			ToggleOff(currentRotor);
			float oldPowerWDIHTCT;
			UpdateName(referencePanel, out oldPowerWDIHTCT, out currentPower);
			if (oldPowerWDIHTCT > currentPower) Reverse(currentRotor);
			updatedRotor = false;
			testingDirection = false;
		}

		// get the optimal rotation
		if (!setting) {
			ToggleOn(currentRotor);
			setting = true;
			UpdateName(referencePanel);
			TriggerTimer();
			return;
		}

		float oldPower;
		UpdateName(referencePanel, out oldPower, out currentPower);
		if (oldPower > currentPower) {
			ToggleOff(currentRotor);
			currentRotor = null;
			currentIndex = (currentIndex + 1) % rotors.Count;
		}
		setting = false;
		TriggerTimer();
	}

	void ToggleOn(IMyTerminalBlock block) {
		block.GetActionWithName("OnOff_On").Apply(block);
	}

	void ToggleOff(IMyTerminalBlock block) {
		block.GetActionWithName("OnOff_Off").Apply(block);
	}

	void Reverse(IMyMotorStator rotor) {
		rotor.GetActionWithName("Reverse").Apply(rotor);
	}

	void TriggerTimer() {
		timer.SetValue("TriggerDelay", loopDelay);
		timer.GetActionWithName("Start").Apply(timer);
	}

	void TriggerTimerIdle() {
		timer.SetValue("TriggerDelay", idleDelay);
		timer.GetActionWithName("Start").Apply(timer);
	}

	void UpdateName(IMySolarPanel solarPanel) {
		float __ignore;
		UpdateName(solarPanel, out __ignore);
	}

	void UpdateName(IMySolarPanel solarPanel, out float oldPower) {
		float __ignore;
		UpdateName(solarPanel, out oldPower, out __ignore);
	}

	void UpdateName(IMySolarPanel solarPanel, out float oldPower, out float currentPower) {
		oldPower = 0.0f;
		if (!MaxOutput(solarPanel, out currentPower)) throw new Exception(" UpdateName(IMySolarPanel, float): failed to read current power output from DetailedInfo");
		if (solarPanel == null) throw new Exception(" UpdateName(IMySolarPanel, float): solarPanel is null");

		string[] array = solarPanel.CustomName.Split('~');
		if (array.Length > 1) {
			// old power has been set
			float.TryParse(array[array.Length - 1], out oldPower);

			// update current power output
			string newName = "";
			for (int i = 0; i < array.Length - 1; i++) {
				newName += array[i];
			}
			newName += "~" + currentPower;
			solarPanel.SetCustomName(newName);
		} else {
			// set current power output
			solarPanel.SetCustomName(solarPanel.CustomName + " ~" + currentPower);
		}
	}

	bool MaxOutput(IMySolarPanel solarPanel, out float power) {
		power = 0.0f;
		if (solarPanel == null) throw new Exception(" MaxOutput(IMySolarPanel, float): solarPanel is null");

		int start = StartIndexMaxOutput(solarPanel);
		int end = StartIndexCurrentOutput(solarPanel) - lang_maxOutput.Length;
		string maxOutput = solarPanel.DetailedInfo.Substring(start, end - start);
		bool success = float.TryParse(System.Text.RegularExpressions.Regex.Replace(maxOutput, @"[^0-9.]", ""), out power);

		// power should be in kW
		if (success) {
			if (maxOutput.Contains(" W")) {
				// W  -> kW: * 0.001
				power *= 0.001f;
			} else if (maxOutput.Contains(" kW")) {
				// kW -> kW: * 1
				power *= 1.0f;
			} else if (maxOutput.Contains(" MW")) {
				// MW -> kW: * 1,000
				power *= 1000.0f;
			} else if (maxOutput.Contains(" GW")) {
				// GW -> kW: * 1,000,000
				power *= 1000000.0f;
			} else throw new Exception(" MaxOutput(IMySolarPanel, float): maximum power output is too high (" + maxOutput + ")");
		}

		return success;
	}

	// currently unused
	bool CurrentOutput(IMySolarPanel solarPanel, out float power) {
		power = 0.0f;
		if (solarPanel == null) throw new Exception(" CurrentOutput(IMySolarPanel, float): solarPanel is null");

		int start = StartIndexCurrentOutput(solarPanel);
		int end = solarPanel.DetailedInfo.Length;
		string currentOutput = solarPanel.DetailedInfo.Substring(start, end - start);
		bool success = float.TryParse(System.Text.RegularExpressions.Regex.Replace(currentOutput, @"[^0-9.]", ""), out power);

		// power should be in kW
		if (success) {
			if (currentOutput.Contains(" W")) {
				// W  -> kW: * 0.001
				power *= 0.001f;
			} else if (currentOutput.Contains(" kW")) {
				// kW -> kW: * 1
				power *= 1.0f;
			} else if (currentOutput.Contains(" MW")) {
				// MW -> kW: * 1,000
				power *= 1000.0f;
			} else if (currentOutput.Contains(" GW")) {
				// GW -> kW: * 1,000,000
				power *= 1000000.0f;
			} else throw new Exception(" CurrentOutput(IMySolarPanel, float): current power output is too high (" + currentOutput + ")");
		}

		return success;
	}

	int StartIndexMaxOutput(IMySolarPanel solarPanel) {
		if (solarPanel == null) throw new Exception(" StartIndexMaxOutput(IMySolarPanel): solarPanel is null");
		int ret = solarPanel.DetailedInfo.IndexOf(lang_maxOutput);
		if (ret < 0) throw new Exception(" StartIndexMaxOutput(IMySolarPanel): incompatible solar panel");
		return ret + lang_maxOutput.Length;
	}

	int StartIndexCurrentOutput(IMySolarPanel solarPanel) {
		if (solarPanel == null) throw new Exception(" StartIndexCurrentOutput(IMySolarPanel): solarPanel is null");
		int ret = solarPanel.DetailedInfo.IndexOf(lang_currentOutput);
		if (ret < 0 || ret <= StartIndexMaxOutput(solarPanel)) throw new Exception(" StartIndexCurrentOutput(IMySolarPanel): incompatible solar panel");
		return ret + lang_currentOutput.Length;
	}
}