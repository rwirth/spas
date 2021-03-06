﻿using System;
using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using Sandbox.Common.ObjectBuilders;
using VRage;
using VRageMath;

class rev7 : Program {
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
        public const string SolarPanelName = "Solar Panel (optimized)";

        // Determines if the name that was provided for SolarPanelName should be used as group name (true) or block name (false). 
        // Default: false 
        // Allowed values: true, false 
        public const bool SolarPanelName_IsGroup = false;

        // Name of the timer block that should be used for looping. 
        // The name has to be EXACTLY THE SAME as in the terminal overview. 
        // Default: "Loop Timer" 
        public const string TimerName = "Loop Timer";

        // Names of all rotors that are connected to the solar panels that should be optimized. 
        // Each name has to be EXACTLY THE SAME as the corresponding rotor's name in the terminal overview. 
        // Default: { "Advanced Rotor" } 
        // Allowed values: comma-separated list of strings 
        public static readonly string[] RotorNames = new string[] { "Advanced Rotor" };

        // Determines for each element of RotorNames if the name provided should be used as group name (true) or block name (false). 
        // This needs to have the same length as RotorNames! 
        // Default: { false } 
        // Allowed values: comma-separated list of true and false 
        public static readonly bool[] RotorNames_IsGroup = new bool[] { false };

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

    static class Status { public const int UPDATING = 1, TESTING = 2, ALIGNING = 3; }

    int CurrentStatus {
        get { return currentStatus; }
        set {
            if (value == Status.UPDATING) Echo("Updating...");
            if (value == Status.TESTING) Echo("Testing...");
            if (value == Status.ALIGNING) Echo("Aligning...");
            currentStatus = value;
        }
    }

    float currentPower = 0.0f;
    int currentRotorIndex = 0;
    int currentDirection = 1;
    int currentStatus = Status.UPDATING;
    IMyTimerBlock timer = null;
    List<IMyMotorStator> currentRotors = new List<IMyMotorStator>();
    List<IMySolarPanel> solarPanels = new List<IMySolarPanel>();
    List<List<IMyMotorStator>> rotors = new List<List<IMyMotorStator>>();

    void Main() {
        // initialize the solar panels 
        InitializeSolarPanels();

        // initialize the rotors 
        InitializeRotors();

        // initialize the timer 
        InitializeTimer();

        // get current power output 
        if (!Utilities.AverageMaxOutput(solarPanels, out currentPower)) throw new Exception("Main(): failed to read average maximum power from solar panels\n\nDid you configure this script correctly?");
        if (currentPower == float.NaN) throw new Exception("Main(): failed to read average maximum power from solar panels - power output too high (> 1 TW)");

        Echo("current average output: " + currentPower + " kW");

        // check if the energy saver feature has been enabled 
        if (Configuration.Features.EnergySaver.Enabled) {
            // check if the current power output is too low to make rotating effective 
            if (currentPower <= Configuration.Features.EnergySaver.HibernatePowerOutput) {
                Hibernate();
                return;
            }
        }

        // check if the target output has been reached 
        if (currentPower >= Configuration.TargetPowerOutput) {
            Hibernate();
            return;
        }

        // update the rotor and check which direction yields the higher power output 
        if (CurrentStatus == Status.UPDATING) {
            currentRotors = rotors[currentRotorIndex];
            UpdateNames();
            Utilities.SetSpeed(currentRotors, GetAppropriateSpeed());
            Utilities.ToggleOn(currentRotors);
            CurrentStatus = Status.TESTING;
            Utilities.Trigger(timer, Configuration.WorkDelay);
            return;
        }

        // set the rotation direction towards the higher power output 
        if (CurrentStatus == Status.TESTING) {
            Utilities.SetSpeed(currentRotors, GetAppropriateSpeed());
            Utilities.ToggleOff(currentRotors);

            float oldPower;
            UpdateNames(out oldPower);
            if (currentPower < oldPower) {
                currentDirection = -currentDirection;
                Utilities.SetSpeed(currentRotors, GetAppropriateSpeed());
            }

            CurrentStatus = Status.ALIGNING;
        }

        // rotate towards the local maximum output 
        if (CurrentStatus == Status.ALIGNING) {
            Utilities.SetSpeed(currentRotors, GetAppropriateSpeed());
            Utilities.ToggleOn(currentRotors);

            float oldPower;
            UpdateNames(out oldPower);
            if (currentPower < oldPower) {
                if (rotors.Count > 1) {
                    Utilities.ToggleOff(currentRotors);
                    CurrentStatus = Status.UPDATING;
                    currentRotorIndex = (currentRotorIndex + 1) % rotors.Count;
                } else {
                    currentDirection = -currentDirection;
                    Utilities.SetSpeed(currentRotors, GetAppropriateSpeed());
                }
            }

            Utilities.Trigger(timer, Configuration.WorkDelay);
            return;
        }
    }

    void InitializeSolarPanels() {
        // exit this method if the solar panels have already been initialized 
        if (solarPanels.Count > 0) return;

        if (Configuration.SolarPanelName_IsGroup) {
            // get a list of all block groups 
            List<IMyBlockGroup> groups = new List<IMyBlockGroup>();
            GridTerminalSystem.GetBlockGroups(groups);

            // search all block groups for one that has the provided name as its own 
            for (int i = 0; i < groups.Count; i++) {
                IMyBlockGroup group = groups[i];
                if (group.Name.Equals(Configuration.SolarPanelName)) {
                    // add all solar panels in that group 
                    for (int j = 0; j < group.Blocks.Count; j++) {
                        IMySolarPanel solarPanel = group.Blocks[j] as IMySolarPanel;
                        if (solarPanel != null) {
                            solarPanels.Add(solarPanel);
                        }
                    }
                    break;
                }
            }

            if (solarPanels.Count <= 0) throw new Exception("InitializeSolarPanels(): failed to find any solar panels in the group \"" + Configuration.SolarPanelName + "\"");
        } else {
            // get a list of all solar panels 
            List<IMyTerminalBlock> panels = new List<IMyTerminalBlock>();
            GridTerminalSystem.GetBlocksOfType<IMySolarPanel>(panels);

            // search all solar panels for one which starts with the provided name 
            for (int i = 0; i < panels.Count; i++) {
                IMySolarPanel solarPanel = panels[i] as IMySolarPanel;
                if (solarPanel != null && solarPanel.CustomName.StartsWith(Configuration.SolarPanelName)) {
                    solarPanels.Add(solarPanel);
                    break;
                }
            }

            if (solarPanels.Count <= 0) throw new Exception("InitializeSolarPanels(): failed to find solar panel with name \"" + Configuration.SolarPanelName + "\"");
        }

        Echo("initialized solar panels");
    }

    void InitializeRotors() {
        // exit this method if the rotors have already been initialized 
        if (rotors.Count > 0) return;

        if (Configuration.RotorNames.Length != Configuration.RotorNames_IsGroup.Length)
            throw new Exception("InitializeRotors(): RotorNames and RotorNames_IsGroup do not have the same length");

        for (int i = 0; i < Configuration.RotorNames.Length; i++) {
            List<IMyMotorStator> axis = new List<IMyMotorStator>();

            if (Configuration.RotorNames_IsGroup[i]) {
                // get a list of all block groups 
                List<IMyBlockGroup> groups = new List<IMyBlockGroup>();
                GridTerminalSystem.GetBlockGroups(groups);

                // search all block groups for one that has the provided name as its own 
                for (int j = 0; j < groups.Count; j++) {
                    IMyBlockGroup group = groups[j];
                    if (group.Name.Equals(Configuration.RotorNames[i])) {
                        // add all rotors in that group 
                        for (int k = 0; k < group.Blocks.Count; k++) {
                            IMyMotorStator rotor = group.Blocks[k] as IMyMotorStator;
                            if (rotor != null) {
                                axis.Add(rotor);
                                Utilities.ToggleOff(rotor);
                            }
                        }
                        break;
                    }
                }

                if (axis.Count <= 0) throw new Exception("InitializeRotors(): failed to find any rotors in the group \"" + Configuration.RotorNames[i] + "\"");
            } else {
                IMyMotorStator rotor = GridTerminalSystem.GetBlockWithName(Configuration.RotorNames[i]) as IMyMotorStator;
                if (rotor == null) throw new Exception("InitializeRotors(): failed to find rotor with name \"" + Configuration.RotorNames[i] + "\"");
                axis.Add(rotor);
                Utilities.ToggleOff(rotor);
            }

            rotors.Add(axis);
        }

        Echo("initialized rotors");
    }

    void InitializeTimer() {
        // exit this method if the timer has already been initialized 
        if (timer != null) return;

        timer = GridTerminalSystem.GetBlockWithName(Configuration.TimerName) as IMyTimerBlock;
        if (timer == null) throw new Exception("InitializeTimer(): failed to find timer block with name \"" + Configuration.TimerName + "\"");

        Echo("initialized timer");
    }

    void Hibernate() {
        for (int i = 0; i < rotors.Count; i++) {
            Utilities.ToggleOff(rotors[i]);
        }
        UpdateNames();
        Echo("Hibernating...");
        Utilities.Trigger(timer, Configuration.IdleDelay);
    }

    void UpdateNames() {
        float __ignored;
        UpdateNames(out __ignored);
    }

    void UpdateNames(out float oldPower) {
        oldPower = 0.0f;
        for (int i = 0; i < solarPanels.Count; i++) {
            IMySolarPanel panel = solarPanels[i];

            float panelOutput;
            if (!Utilities.MaxOutput(panel, out panelOutput)) continue;

            string[] split = panel.CustomName.Split('~');
            if (split.Length <= 1) {
                panel.SetCustomName(panel.CustomName + "~" + panelOutput);
                continue;
            }

            float panelOldOutput;
            float.TryParse(split[split.Length - 1], out panelOldOutput);

            string name = "";
            for (int k = 0; k < split.Length - 1; k++) {
                name += split[k];
            }
            name += "~" + panelOutput;
            panel.SetCustomName(name);

            if (oldPower == 0) oldPower = panelOldOutput;
            else oldPower = (oldPower + panelOldOutput) / 2;
        }
    }

    float GetAppropriateSpeed() {
        float speed = Configuration.RotorSpeed;
        if (Configuration.Features.EnergySaver.Enabled && currentPower <= Configuration.Features.EnergySaver.PanicPowerOutput) {
            speed *= Configuration.Features.EnergySaver.PanicSpeedMultiplier;
            Echo("Panicking...");
        }
        return speed * currentDirection;
    }

    static class Utilities {
        public static void ToggleOn(IMyFunctionalBlock block) {
            block.GetActionWithName("OnOff_On").Apply(block);
        }

        public static void ToggleOn(List<IMyMotorStator> blocks) {
            for (int i = 0; i < blocks.Count; i++) {
                ToggleOn(blocks[i]);
            }
        }

        public static void ToggleOff(IMyFunctionalBlock block) {
            block.GetActionWithName("OnOff_Off").Apply(block);
        }

        public static void ToggleOff(List<IMyMotorStator> blocks) {
            for (int i = 0; i < blocks.Count; i++) {
                ToggleOff(blocks[i]);
            }
        }

        public static void Trigger(IMyTimerBlock timer, float delay) {
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

        public static bool MaxOutput(IMySolarPanel panel, out float power) {
            power = 0.0f;
            int start = panel.DetailedInfo.IndexOf(Configuration.Localization.MaxOutput);
            int end = panel.DetailedInfo.IndexOf(Configuration.Localization.CurrentOutput);
            if (start < 0 || end < 0 || end < start) return false;
            start += Configuration.Localization.MaxOutput.Length;

            string maxOutput = panel.DetailedInfo.Substring(start, end - start);
            if (float.TryParse(System.Text.RegularExpressions.Regex.Replace(maxOutput, @"[^0-9.]", ""), out power)) {
                float factor = 0.0f;
                if (maxOutput.Contains(" W")) factor = 0.001f;
                else if (maxOutput.Contains(" kW")) factor = 1.0f;
                else if (maxOutput.Contains(" MW")) factor = 1000.0f;
                else if (maxOutput.Contains(" GW")) factor = 1000000.0f;
                else {
                    power = float.NaN;
                    return true;
                }

                power *= factor;
                return true;
            }

            return false;
        }

        public static bool AverageMaxOutput(List<IMySolarPanel> panels, out float power) {
            power = 0.0f;
            for (int i = 0; i < panels.Count; i++) {
                float maxOutput;
                IMySolarPanel panel = panels[i];
                if (!MaxOutput(panel, out maxOutput)) return false;

                if (power == 0) power = maxOutput;
                else power = (power + maxOutput) / 2;
            }
            return true;
        }
    }
}