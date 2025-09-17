namespace AutoBot;

public static class Helper
{
    public static long ToUnixTimeInMilliseconds(this DateTime dateTime)
        => (long)dateTime.Subtract(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;

    public static DateTime TimeStampToDateTime(this long timeStamp)
    {
        DateTime dateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
        dateTime = dateTime.AddMilliseconds(timeStamp).ToLocalTime();
        return dateTime;
    }

    public static DateTime TimeStampToDateTime(this string timeStamp)
    {
        if (DateTime.TryParse(timeStamp, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var result))
        {
            return result;
        }

        throw new ArgumentException($"Invalid timestamp format: {timeStamp}", nameof(timeStamp));
    }
}
