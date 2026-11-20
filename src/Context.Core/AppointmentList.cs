namespace Context;

/// <summary>
/// Which of a day's appointments a short day list shows (the desktop sidebar). Pure: the caller passes the day's
/// appointments in <see cref="CalendarStore.Day(DateOnly)"/> order and the current local time.
/// </summary>
internal static class AppointmentList
{
    /// <summary>
    /// On today (<paramref name="isToday"/>): the last past appointment (an appointment is past when its
    /// <c>End</c> is at or before <paramref name="localNow"/>; the last is the one that comes last in
    /// <paramref name="day"/>) plus every appointment that is not past, in <paramref name="day"/>'s order. On any
    /// other day: every appointment.
    /// </summary>
    public static IReadOnlyList<Appointment> Shown(IReadOnlyList<Appointment> day, DateTime localNow, bool isToday)
    {
        if (!isToday)
        {
            return day;
        }

        int lastPast = -1;
        for (int i = 0; i < day.Count; i++)
        {
            if (day[i].End <= localNow)
            {
                lastPast = i;
            }
        }

        List<Appointment> shown = [];
        for (int i = 0; i < day.Count; i++)
        {
            if (i == lastPast || day[i].End > localNow)
            {
                shown.Add(day[i]);
            }
        }

        return shown;
    }
}
