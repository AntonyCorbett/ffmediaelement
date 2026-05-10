namespace Unosquare.FFME.Windows.Sample.Foundation
{
    using ClosedCaptions;
    using System;
    using System.Globalization;
    using System.Windows;
    using System.Windows.Data;
    using System.Windows.Media;

    /// <inheritdoc />
    internal sealed class TimeSpanToSecondsConverter : IValueConverter
    {
        /// <inheritdoc />
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value switch
            {
                TimeSpan span => span.TotalSeconds,
                Duration duration => duration.HasTimeSpan ? duration.TimeSpan.TotalSeconds : 0d,
                _ => 0d
            };
        }

        /// <inheritdoc />
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double == false) return 0d;
            var result = TimeSpan.FromTicks(System.Convert.ToInt64(TimeSpan.TicksPerSecond * (double)value));

            // Do the conversion from visibility to bool
            if (targetType == typeof(TimeSpan)) return result;
            return targetType == typeof(Duration) ?
                new Duration(result) : Activator.CreateInstance(targetType);
        }
    }

    /// <inheritdoc />
    internal sealed class TimeSpanFormatter : IValueConverter
    {
        /// <inheritdoc />
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            TimeSpan p;

            switch (value)
            {
                case TimeSpan position:
                    p = position;
                    break;
                case Duration duration when duration.HasTimeSpan:
                    p = duration.TimeSpan;
                    break;
                default:
                    return string.Empty;
            }

            if (p == TimeSpan.MinValue)
                return "N/A";

            return $"{(int)p.TotalHours:00}:{p.Minutes:00}:{p.Seconds:00}.{p.Milliseconds:000}";
        }

        /// <inheritdoc />
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotImplementedException();
    }

    /// <inheritdoc />
    internal sealed class ByteFormatter : IValueConverter
    {
        /// <inheritdoc />
        public object Convert(object value, Type targetType, object format, CultureInfo culture)
        {
            const double minKiloByte = 1024;
            const double minMegaByte = 1024 * 1024;
            const double minGigaByte = 1024 * 1024 * 1024;

            var byteCount = System.Convert.ToDouble(value, CultureInfo.InvariantCulture);

            var suffix = "b";
            var output = 0d;

            if (byteCount >= minKiloByte)
            {
                suffix = "kB";
                output = Math.Round(byteCount / minKiloByte, 2);
            }

            if (byteCount >= minMegaByte)
            {
                suffix = "MB";
                output = Math.Round(byteCount / minMegaByte, 2);
            }

            if (byteCount >= minGigaByte)
            {
                suffix = "GB";
                output = Math.Round(byteCount / minGigaByte, 2);
            }

            return suffix == "b" ?
                $"{output:0} {suffix}" :
                $"{output:0.00} {suffix}";
        }

        /// <inheritdoc />
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotImplementedException();
    }

    /// <inheritdoc />
    internal sealed class BitFormatter : IValueConverter
    {
        /// <inheritdoc />
        public object Convert(object value, Type targetType, object format, CultureInfo culture)
        {
            const double minKiloBit = 1000;
            const double minMegaBit = 1000 * 1000;
            const double minGigaBit = 1000 * 1000 * 1000;

            var byteCount = System.Convert.ToDouble(value, CultureInfo.InvariantCulture);

            var suffix = "bits/s";
            var output = 0d;

            if (byteCount >= minKiloBit)
            {
                suffix = "kbits/s";
                output = Math.Round(byteCount / minKiloBit, 2);
            }

            if (byteCount >= minMegaBit)
            {
                suffix = "Mbits/s";
                output = Math.Round(byteCount / minMegaBit, 2);
            }

            if (byteCount >= minGigaBit)
            {
                suffix = "Gbits/s";
                output = Math.Round(byteCount / minGigaBit, 2);
            }

            return suffix == "b" ?
                $"{output:0} {suffix}" :
                $"{output:0.00} {suffix}";
        }

        /// <inheritdoc />
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotImplementedException();
    }

    /// <inheritdoc />
    internal sealed class PercentageFormatter : IValueConverter
    {
        /// <inheritdoc />
        public object Convert(object value, Type targetType, object format, CultureInfo culture)
        {
            var percentage = 0d;
            if (value is double d) percentage = d;

            percentage = Math.Round(percentage * 100d, 0);

            if (format == null || Math.Abs(percentage) <= double.Epsilon)
                return $"{percentage,3:0} %".Trim();

            return $"{(percentage > 0d ? "R " : "L ")} {Math.Abs(percentage),3:0} %".Trim();
        }

        /// <inheritdoc />
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotImplementedException();
    }

    /// <inheritdoc />
    internal sealed class PlaylistEntryThumbnailConverter : IValueConverter
    {
        /// <inheritdoc />
        public object Convert(object value, Type targetType, object format, CultureInfo culture)
        {
            if (value is string thumbnailFilename && !App.IsInDesignMode)
            {
                return ThumbnailGenerator.GetThumbnail(
                    App.ViewModel.Playlist.ThumbsDirectory, thumbnailFilename);
            }

            return default(ImageSource);
        }

        /// <inheritdoc />
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotImplementedException();
    }

    /// <inheritdoc />
    internal sealed class PlaylistDurationFormatter : IValueConverter
    {
        /// <inheritdoc />
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var duration = value is TimeSpan span ? span : TimeSpan.FromSeconds(-1);

            if (duration.TotalSeconds <= 0)
                return "∞";

            return duration.TotalMinutes >= 100 ?
                $"{System.Convert.ToInt64(duration.TotalHours)}h {System.Convert.ToInt64(duration.Minutes)}m" :
                $"{System.Convert.ToInt64(duration.Minutes):00}:{System.Convert.ToInt64(duration.Seconds):00}";
        }

        /// <inheritdoc />
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    /// <inheritdoc />
    internal sealed class UtcDateToLocalTimeString : IValueConverter
    {
        /// <inheritdoc />
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return "unknown";
            var utcDate = (DateTime)value;
            return utcDate.ToLocalTime().ToString("f", CultureInfo.InvariantCulture);
        }

        /// <inheritdoc />
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    /// <inheritdoc />
    [ValueConversion(typeof(bool), typeof(bool))]
    internal sealed class InverseBooleanConverter : IValueConverter
    {
        /// <inheritdoc />
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (targetType != typeof(bool) && targetType != typeof(bool?))
                throw new InvalidOperationException("The target must be a boolean or a nullable boolean");

            if (value is bool?)
            {
                var nullableBool = (bool?)value;
                return !nullableBool.Value;
            }

            if (value.GetType() == typeof(bool))
            {
                return !((bool)value);
            }

            return true;
        }

        /// <inheritdoc />
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return true;

            if (value is bool?)
            {
                var nullableBool = (bool?)value;
                return !nullableBool.Value;
            }

            if (value.GetType() == typeof(bool))
            {
                return !((bool)value);
            }

            return true;
        }
    }

    /// <inheritdoc />
    [ValueConversion(typeof(bool), typeof(bool))]
    internal sealed class ClosedCaptionsChannelConverter : IValueConverter
    {
        /// <inheritdoc />
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value != null && (CaptionsChannel)value != CaptionsChannel.CCP;

        /// <inheritdoc />
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            value != null && (bool)value ? CaptionsChannel.CC1 : CaptionsChannel.CCP;
    }
}
