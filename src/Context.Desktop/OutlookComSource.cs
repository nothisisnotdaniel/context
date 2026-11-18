using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.CSharp.RuntimeBinder;

namespace Context;

/// <summary>
/// Reads the running classic Outlook through its COM interface, late-bound (<c>dynamic</c>, no interop assembly), with
/// the profile that is already signed in. It only attaches to a running Outlook (<c>GetActiveObject</c>) and never
/// starts it, never writes to it, and reads only the subject, location, start, end and all-day flag of the default
/// Calendar's items and the received time, sender name and subject of the default Inbox's items. Every COM object it
/// touches is released in a <c>finally</c>. A COM error becomes a kind, never an exception text.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class OutlookComSource : IOutlookSource
{
    private const string ProgId = "Outlook.Application";

    // OlDefaultFolders: olFolderCalendar and olFolderInbox.
    private const int CalendarFolder = 9;
    private const int InboxFolder = 6;

    // The most items walked per read, whatever the folder holds.
    private const int MaxItems = 500;

    // The most mails returned per read.
    private const int MaxMails = 10;

    private const int MkEUnavailable = unchecked((int)0x800401E3);
    private const int EAccessDenied = unchecked((int)0x80070005);
    private const int EAbort = unchecked((int)0x80004004);

    public OutlookAvailability Availability()
    {
        try
        {
            return Type.GetTypeFromProgID(ProgId) is null ? OutlookAvailability.NotInstalled : OutlookAvailability.Available;
        }
        catch (Exception)
        {
            return OutlookAvailability.NotInstalled;
        }
    }

    public OutlookRead<Appointment> Appointments(DateOnly day, TimeZoneInfo zone)
    {
        Stack<object> held = new();
        try
        {
            if (Attach(held) is not { } application)
            {
                return new OutlookRead<Appointment>(OutlookReadKind.NotRunning);
            }

            // The day's bounds in the calendar's zone, as the machine's local time Outlook filters in. Only these two
            // dates go into the filter; no outside text does.
            DateTime from = ToMachine(day.ToDateTime(TimeOnly.MinValue), zone);
            DateTime to = ToMachine(day.AddDays(1).ToDateTime(TimeOnly.MinValue), zone);
            string filter = "[Start] < '" + Filter(to) + "' AND [End] > '" + Filter(from) + "'";

            dynamic ns = Hold(held, application.GetNamespace("MAPI"));
            dynamic folder = Hold(held, ns.GetDefaultFolder(CalendarFolder));
            dynamic items = Hold(held, folder.Items);

            // Outlook expands recurring meetings itself: sort by start, include recurrences, then restrict.
            items.Sort("[Start]");
            items.IncludeRecurrences = true;
            dynamic restricted = Hold(held, items.Restrict(filter));

            List<Appointment> found = [];
            int taken = 0;
            int walked = 0;
            object? next = restricted.GetFirst();
            while (next is not null && walked < MaxItems)
            {
                walked++;
                dynamic item = Hold(held, next);
                Appointment? appointment = ReadAppointment(item, zone);
                if (appointment is not null)
                {
                    found.Insert(taken, appointment);
                    taken++;
                }

                next = restricted.GetNext();
            }

            return new OutlookRead<Appointment>(OutlookReadKind.Ok, found);
        }
        catch (Exception e)
        {
            return new OutlookRead<Appointment>(KindOf(e));
        }
        finally
        {
            Release(held);
        }
    }

    public OutlookRead<MailSummary> RecentMail(DateTime sinceUtc, int max)
    {
        Stack<object> held = new();
        try
        {
            if (Attach(held) is not { } application)
            {
                return new OutlookRead<MailSummary>(OutlookReadKind.NotRunning);
            }

            int limit = Math.Clamp(max, 0, MaxMails);
            DateTime since = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(sinceUtc, DateTimeKind.Utc), TimeZoneInfo.Local);
            string filter = "[ReceivedTime] >= '" + Filter(since) + "'";

            dynamic ns = Hold(held, application.GetNamespace("MAPI"));
            dynamic folder = Hold(held, ns.GetDefaultFolder(InboxFolder));
            dynamic items = Hold(held, folder.Items);
            dynamic restricted = Hold(held, items.Restrict(filter));
            restricted.Sort("[ReceivedTime]", true);

            List<MailSummary> found = [];
            int taken = 0;
            int walked = 0;
            object? next = restricted.GetFirst();
            while (next is not null && walked < MaxItems && taken < limit)
            {
                walked++;
                dynamic item = Hold(held, next);
                MailSummary? mail = ReadMail(item);
                if (mail is not null && mail.ReceivedUtc >= sinceUtc)
                {
                    found.Insert(taken, mail);
                    taken++;
                }

                next = restricted.GetNext();
            }

            return new OutlookRead<MailSummary>(OutlookReadKind.Ok, found);
        }
        catch (Exception e)
        {
            return new OutlookRead<MailSummary>(KindOf(e));
        }
        finally
        {
            Release(held);
        }
    }

    // The running Outlook, or null when none runs. Never starts one.
    private static dynamic? Attach(Stack<object> held)
    {
        int result = CLSIDFromProgID(ProgId, out Guid clsid);
        if (result != 0)
        {
            return null;
        }

        result = GetActiveObject(ref clsid, IntPtr.Zero, out object? running);
        if (result == MkEUnavailable || running is null)
        {
            return null;
        }

        if (result != 0)
        {
            throw new COMException(null, result);
        }

        held.Push(running);
        return running;
    }

    private static Appointment? ReadAppointment(dynamic item, TimeZoneInfo zone)
    {
        try
        {
            string subject = (string?)item.Subject ?? string.Empty;
            string? location = (string?)item.Location;
            DateTime start = (DateTime)item.Start;
            DateTime end = (DateTime)item.End;
            bool allDay = (bool)item.AllDayEvent;
            if (allDay)
            {
                return new Appointment(
                    subject, string.IsNullOrEmpty(location) ? null : location, start.Date, end.Date, true);
            }

            return new Appointment(
                subject, string.IsNullOrEmpty(location) ? null : location, ToZone(start, zone), ToZone(end, zone), false);
        }
        catch (Exception e) when (e is RuntimeBinderException or InvalidCastException or ArgumentException)
        {
            // Not an appointment item, or a time that doesn't exist in the zone: skipped.
            return null;
        }
    }

    private static MailSummary? ReadMail(dynamic item)
    {
        try
        {
            DateTime received = (DateTime)item.ReceivedTime;

            // SenderName can make Outlook's object-model guard ask the user; a refusal is a COM error (Denied).
            string sender = (string?)item.SenderName ?? string.Empty;
            string subject = (string?)item.Subject ?? string.Empty;
            DateTime receivedUtc = TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(received, DateTimeKind.Unspecified), TimeZoneInfo.Local);
            return new MailSummary(receivedUtc, sender, subject);
        }
        catch (Exception e) when (e is RuntimeBinderException or InvalidCastException or ArgumentException)
        {
            // Not a mail item (a report, say), or a time that doesn't exist: skipped.
            return null;
        }
    }

    // A wall-clock time in the calendar's zone as the machine's local time.
    private static DateTime ToMachine(DateTime wallClock, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified), zone, TimeZoneInfo.Local);

    // A machine-local time from Outlook as a wall-clock time in the calendar's zone.
    private static DateTime ToZone(DateTime machine, TimeZoneInfo zone) =>
        DateTime.SpecifyKind(
            TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(machine, DateTimeKind.Unspecified), TimeZoneInfo.Local, zone),
            DateTimeKind.Unspecified);

    // Outlook's Restrict parses dates in the user's current culture; "g" is its short date and time.
    private static string Filter(DateTime local) => local.ToString("g", CultureInfo.CurrentCulture);

    private static OutlookReadKind KindOf(Exception e) => e switch
    {
        UnauthorizedAccessException => OutlookReadKind.Denied,
        COMException com when com.HResult == EAccessDenied || com.HResult == EAbort => OutlookReadKind.Denied,
        COMException com when com.HResult == MkEUnavailable => OutlookReadKind.NotRunning,
        _ => OutlookReadKind.Failed,
    };

    private static object Hold(Stack<object> held, object value)
    {
        held.Push(value);
        return value;
    }

    private static void Release(Stack<object> held)
    {
        while (held.TryPop(out object? value))
        {
            try
            {
                if (Marshal.IsComObject(value))
                {
                    Marshal.FinalReleaseComObject(value);
                }
            }
            catch (Exception)
            {
                // Releasing never fails a read.
            }
        }
    }

    [DllImport("ole32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int CLSIDFromProgID(string progId, out Guid clsid);

    [DllImport("oleaut32.dll", ExactSpelling = true)]
    private static extern int GetActiveObject(
        ref Guid clsid, IntPtr reserved, [MarshalAs(UnmanagedType.IUnknown)] out object? unknown);
}
