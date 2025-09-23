namespace AutoBot;

public static class Helper
{
    public static DateTime TimeStampToDateTime(this string timeStamp)
    {
        if (DateTime.TryParse(timeStamp, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var result))
        {
            return result;
        }

        throw new ArgumentException($"Invalid timestamp format: {timeStamp}", nameof(timeStamp));
    }
}
