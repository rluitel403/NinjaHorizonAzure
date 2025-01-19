using System;

namespace NinjaHorizon.Function
{
    public static class EnergySystem
    {
        public const int ENERGY_RESTORE_MINUTES = 5;
        public const int ENERGY_RESTORE_AMOUNT = 5;

        public static EnergyData RestoreEnergy(EnergyData energyData, DateTime currentTime)
        {
            DateTime lastUpdatedTime;
            try
            {
                lastUpdatedTime = DateTime.Parse(
                    energyData.lastUpdatedTime,
                    null,
                    System.Globalization.DateTimeStyles.RoundtripKind
                );
            }
            catch
            {
                // Handle invalid timestamp by resetting it
                energyData.lastUpdatedTime = currentTime.ToString("o");
                return energyData;
            }

            // Prevent time manipulation by ensuring lastUpdatedTime isn't in the future
            if (lastUpdatedTime > currentTime)
            {
                lastUpdatedTime = currentTime;
            }

            TimeSpan timePassed = DateTime.UtcNow - lastUpdatedTime;
            int secondsPerRestore = ENERGY_RESTORE_MINUTES * 60;
            int energyToRestore =
                (int)(timePassed.TotalSeconds / secondsPerRestore) * ENERGY_RESTORE_AMOUNT;
            // Calculate the remaining time until the next energy restore
            float remainingSeconds = (float)(timePassed.TotalSeconds % secondsPerRestore);
            if (energyData.currentEnergy >= energyData.maxEnergy)
            {
                energyData.lastUpdatedTime = DateTime.UtcNow.ToString("o");
            }
            else
            {
                energyData.currentEnergy = Math.Min(
                    energyData.maxEnergy,
                    energyData.currentEnergy + energyToRestore
                );
                energyData.lastUpdatedTime = DateTime
                    .UtcNow.AddSeconds(-remainingSeconds)
                    .ToString("o");
            }

            return energyData;
        }

        public static TimeSpan GetTimeUntilNextEnergy(EnergyData energyData, DateTime currentTime)
        {
            if (energyData.currentEnergy >= energyData.maxEnergy)
                return TimeSpan.Zero;

            DateTime lastUpdated = DateTime.Parse(
                energyData.lastUpdatedTime,
                null,
                System.Globalization.DateTimeStyles.RoundtripKind
            );
            double minutesSinceLastUpdate = (currentTime - lastUpdated).TotalMinutes;
            double minutesUntilNextEnergy =
                ENERGY_RESTORE_MINUTES - (minutesSinceLastUpdate % ENERGY_RESTORE_MINUTES);

            return TimeSpan.FromMinutes(minutesUntilNextEnergy);
        }
    }
}
