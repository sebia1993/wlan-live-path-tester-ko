namespace WlanLivePathTester.Core.Wlan;

public static class WlanChannelCalculator
{
    public static uint? FromCenterFrequencyMhz(uint? centerFrequencyMhz)
    {
        if (centerFrequencyMhz is null)
        {
            return null;
        }

        uint frequency = centerFrequencyMhz.Value;

        if (frequency == 2484)
        {
            return 14;
        }

        if (frequency is >= 2412 and <= 2472 && (frequency - 2407) % 5 == 0)
        {
            return (frequency - 2407) / 5;
        }

        if (frequency is >= 4910 and <= 4980 && (frequency - 4000) % 5 == 0)
        {
            return (frequency - 4000) / 5;
        }

        if (frequency is >= 5000 and <= 5895 && (frequency - 5000) % 5 == 0)
        {
            return (frequency - 5000) / 5;
        }

        if (frequency == 5935)
        {
            return 2;
        }

        if (frequency is >= 5955 and <= 7115 && (frequency - 5950) % 5 == 0)
        {
            return (frequency - 5950) / 5;
        }

        return null;
    }

    public static string GetBandName(uint? centerFrequencyMhz)
    {
        return centerFrequencyMhz switch
        {
            >= 2400 and < 2500 => "2.4 GHz",
            >= 4900 and < 5925 => "5 GHz",
            >= 5925 and <= 7125 => "6 GHz",
            >= 57000 and <= 71000 => "60 GHz",
            null => "확인 불가",
            _ => "기타"
        };
    }
}
