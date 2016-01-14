using System;
using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using Sandbox.Common.ObjectBuilders;
using VRage;
using VRageMath;

class rev8 : Program {
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

        // Delay between executions while aligning in hibernation mode (in seconds)
        // Default: 1.5f
        // Recommended values: 1.5f - 5f;
        public const float HibernationWorkDelay = 1.5f;

        // Delay between executions while idling in hibernation mode (in seconds)
        // Default: 10.0f
        // Recommended values: 2.5f - 10.0f
        public const float HibernationIdleDelay = 10.0f;

        // Maximum deviation when comparing angles (in degrees)
        // Default: 1.0f;
        // Recommended values: 0.0001f - 1.0f
        public const float MaxAngleDeviation = 1.0f;

        // Speed of all rotors that are used to align the solar panels (in RPM)
        // Lower values mean higher accuracy but also higher alignment time. 
        // Default: 0.1f 
        // Recommended values: 0.05f - 0.5f 
        public const float RotorSpeed = 0.1f;

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
        public static readonly string[] RotorNames = { "Advanced Rotor (SPAS)" };

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
                // Default (Vanilla): 20 for large ship/station, 5 for small ship 
                // Allowed values: 0 - 999999999 
                public const int HibernatePowerOutput = 20;

                // Maximum power output at which the rotors will be accelerated by the panic speed multiplier (in kW). 
                // Default (Vanilla): 60 for large ship/station, 15 for small ship 
                // Allowed values: 0 - 999999999 
                public const int PanicPowerOutput = 60;

                // Value by which the default rotor speed will be multiplied while in panic mode.
                // Default: 2.0f 
                // Recommended values: 1.0f - 5.0f 
                public const float PanicSpeedMultiplier = 2.0f;
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

    int CurrentStatus {
        get { return currentStatus; }
        set {
            if (value == Status.HIBERNATING) Echo("Hibernating...");
            else if (value == Status.IDLING) Echo("Idling...");
            else if (value == Status.UPDATING) Echo("Updating...");
            else if (value == Status.TESTING) Echo("Testing...");
            else if (value == Status.ALIGNING) Echo("Aligning...");
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

    void Main(string argument) {
        // toggle maintenance mode
        if (Configuration.Features.MaintenanceMode.Enabled && (argument + " ").Contains("maintenance ")) {
            if (Me.CustomName.EndsWith(Configuration.Features.MaintenanceMode.MaintenanceSuffix)) {
                Me.SetCustomName(Me.CustomName.Substring(0, Me.CustomName.Length - Configuration.Features.MaintenanceMode.MaintenanceSuffix.Length));
                Echo("This programmable block is no longer in maintenance mode.");

                InitializeSolarPanels(Configuration.Features.MaintenanceMode.Reinitialize);
                InitializeRotors(Configuration.Features.MaintenanceMode.Reinitialize);
                InitializeTimer(Configuration.Features.MaintenanceMode.Reinitialize);
            } else {
                Me.SetCustomName(Me.CustomName + Configuration.Features.MaintenanceMode.MaintenanceSuffix);
                Echo("This programmable block is now in maintenance mode.");
                return;
            }
        } else {
            // initialize the solar panels 
            InitializeSolarPanels();

            // initialize the rotors 
            InitializeRotors();

            // initialize the timer 
            InitializeTimer();
        }

        // maintenance mode
        if (Me.CustomName.EndsWith(Configuration.Features.MaintenanceMode.MaintenanceSuffix)) {
            Echo("MAINTENANCE MODE");
            Utilities.Trigger(timer, Configuration.Features.MaintenanceMode.MaintenanceDelay);
            return;
        }

        // get current power output 
        if (!Utilities.AverageMaxOutput(solarPanels, out currentPower)) throw new Exception(" Main(): failed to read average maximum power from solar panels\n\nDid you configure this script correctly?");
        if (currentPower == float.NaN) throw new Exception(" Main(): failed to read average maximum power from solar panels - power output too high (>= 1 PW)");

        Echo("current average output: " + currentPower + " kW");

        // check if the energy saver feature has been enabled 
        if (Configuration.Features.EnergySaver.Enabled) {
            // check if the current power output is too low to make rotating effective but allow direction testing
            if (currentPower <= Configuration.Features.EnergySaver.HibernatePowerOutput && CurrentStatus != Status.TESTING) {
                Hibernate();
                return;
            }
        }

        // check if the target output has been reached 
        if (currentPower >= Configuration.TargetAveragePowerOutput) {
            Idle();
            return;
        }

        // update the rotor and check which direction yields the higher power output 
        if (CurrentStatus == Status.UPDATING || CurrentStatus == Status.IDLING || CurrentStatus == Status.HIBERNATING) {
            currentRotors = rotors[currentRotorIndex];
            UpdateNames();
            Utilities.SetSpeed(currentRotors, GetRotorSpeed());
            Utilities.ToggleOn(currentRotors);
            CurrentStatus = Status.TESTING;
            Utilities.Trigger(timer, Configuration.WorkDelay);
            return;
        }

        // set the rotation direction towards the higher power output 
        if (CurrentStatus == Status.TESTING) {
            Utilities.SetSpeed(currentRotors, GetRotorSpeed());
            Utilities.ToggleOff(currentRotors);

            float oldPower;
            UpdateNames(out oldPower);
            if (currentPower < oldPower) {
                currentDirection = -currentDirection;
                Utilities.SetSpeed(currentRotors, GetRotorSpeed());
            }

            CurrentStatus = Status.ALIGNING;
        }

        // rotate towards the local maximum output 
        if (CurrentStatus == Status.ALIGNING) {
            Utilities.SetSpeed(currentRotors, GetRotorSpeed());
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
                    Utilities.SetSpeed(currentRotors, GetRotorSpeed());
                }
            }

            Utilities.Trigger(timer, Configuration.WorkDelay);
            return;
        }
    }

    void InitializeSolarPanels(bool forced = false) {
        // exit this method if the solar panels have already been initialized 
        if (solarPanels.Count > 0 && !forced) return;

        if (Configuration.SolarPanelName_IsGroup) {
            InitializeSolarPanelGroup();
        } else {
            InitializeSolarPanel();
        }

        Echo("initialized solar panels");
    }

    void InitializeSolarPanelGroup() {
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

        if (solarPanels.Count <= 0) throw new Exception(" InitializeSolarPanelGroup(): failed to find any solar panels in the group \"" + Configuration.SolarPanelName + "\"");
    }

    void InitializeSolarPanel() {
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

        if (solarPanels.Count <= 0) throw new Exception(" InitializeSolarPanels(): failed to find solar panel with name \"" + Configuration.SolarPanelName + "\"");
    }

    void InitializeRotors(bool forced = false) {
        // exit this method if the rotors have already been initialized 
        if (rotors.Count > 0 && !forced) return;

        for (int i = 0; i < Configuration.RotorNames.Length; i++) {
            List<IMyMotorStator> axis = new List<IMyMotorStator>();
            string name = Configuration.RotorNames[i];
            bool isGroup = i >= Configuration.RotorNames_IsGroup.Length? false : Configuration.RotorNames_IsGroup[i];

            if (isGroup) {
                InitializeRotorGroup(axis, name);
            } else {
                InitializeRotor(axis, name);
            }

            rotors.Add(axis);
        }

        Echo("initialized rotors");
    }

    void InitializeRotorGroup(List<IMyMotorStator> axis, string groupName) {
        // get a list of all block groups 
        List<IMyBlockGroup> groups = new List<IMyBlockGroup>();
        GridTerminalSystem.GetBlockGroups(groups);
        bool foundGroup = false;

        // search all block groups for one that has the provided name as its own 
        for (int j = 0; j < groups.Count; j++) {
            IMyBlockGroup group = groups[j];
            if (group.Name.Equals(groupName)) {
                foundGroup = true;
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

        if (!foundGroup) throw new Exception(" InitializeRotors(): failed to find group with name \"" + groupName + "\"");
        if (axis.Count <= 0) throw new Exception(" InitializeRotors(): failed to find any rotors in the group \"" + groupName + "\"");
    }

    void InitializeRotor(List<IMyMotorStator> axis, string rotorName) {
        IMyMotorStator rotor = GridTerminalSystem.GetBlockWithName(rotorName) as IMyMotorStator;
        if (rotor == null) {
            string message = " InitializeRotor(): failed to find rotor with name \"" + rotorName + "\"";
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
    }

    void InitializeTimer(bool forced = false) {
        // exit this method if the timer has already been initialized 
        if (timer != null && !forced) return;

        timer = GridTerminalSystem.GetBlockWithName(Configuration.TimerName) as IMyTimerBlock;
        if (timer == null) throw new Exception("InitializeTimer(): failed to find timer block with name \"" + Configuration.TimerName + "\"");

        Echo("initialized timer");
    }

    void Idle() {
        for (int i = 0; i < rotors.Count; i++) {
            Utilities.ToggleOff(rotors[i]);
        }
        UpdateNames();
        CurrentStatus = Status.IDLING;
        Utilities.Trigger(timer, Configuration.IdleDelay);
    }

    void Hibernate() {
        bool idle = true;
        float speed = GetRotorSpeed();
        for (int i = 0; i < rotors.Count; i++) {
            List<IMyMotorStator> axis = rotors[i];
            for (int j = 0; j < axis.Count; j++) {
                IMyMotorStator rotor = axis[j];
                if (Utilities.ReachedLowerLimit(rotor) || Utilities.ReachedUpperLimit(rotor)) {
                    Utilities.ToggleOff(rotor);
                } else {
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
        CurrentStatus = Status.HIBERNATING;
        Utilities.Trigger(timer, idle ? Configuration.HibernationIdleDelay : Configuration.HibernationWorkDelay);
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

    float GetRotorSpeed() {
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

        public static bool RotationEquals(float a, float b) {
            return Math.Abs(a - b) <= Configuration.MaxAngleDeviation;
        }

        public static bool HasLowerLimit(IMyMotorStator rotor) {
            return !float.IsInfinity(rotor.LowerLimit);
        }

        public static bool HasUpperLimit(IMyMotorStator rotor) {
            return !float.IsInfinity(rotor.UpperLimit);
        }

        public static bool ReachedLowerLimit(IMyMotorStator rotor) {
            return !HasLowerLimit(rotor) || (HasLowerLimit(rotor) && RotationEquals(ToDegrees(rotor.Angle), ToDegrees(rotor.LowerLimit)));
        }

        public static bool ReachedUpperLimit(IMyMotorStator rotor) {
            return !HasUpperLimit(rotor) || (HasUpperLimit(rotor) && RotationEquals(ToDegrees(rotor.Angle), ToDegrees(rotor.UpperLimit)));
        }

        public static float ToDegrees(float radians) {
            return (float)(radians / Math.PI) * 180;
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
                else if (maxOutput.Contains(" TW")) factor = 1000000000.0f;
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
