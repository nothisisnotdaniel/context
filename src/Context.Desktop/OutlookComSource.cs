using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.CSharp.RuntimeBinder;

namespace Context;

/// <summary>
/// Reads the running classic Outlook through its COM interface, late-bound (<c>dynamic</c>, no interop assembly), with
/// the profile that is already signed in. It only attaches to a running Outlook (<c>GetActiveObject</c>) and never
/// starts it, never writes to it, and reads only the sensitivity, subject, location, start, end and all-day flag of the
/// default Calendar's items; a private or confidential item (see <see cref="OutlookSensitivity"/>) is skipped. When the
/// Mail digest asks (job 286, opt-in), it also reads the Inbox, read-only: the sensitivity first, then only the subject,
/// sender name, received time, message class and the first <see cref="MailDigest.MaxBodyLength"/> body characters of
/// each item. Every COM object it touches is released in a <c>finally</c>. A COM error becomes a kind, never an exception text.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class OutlookComSource : IOutlookSource
{
    private const string ProgId = "Outlook.Application";

    // OlDefaultFolders: olFolderCalendar.
    private const int CalendarFolder = 9;

    // The most items walked per read, whatever the folder holds.
    private const int MaxItems = 500;

    // The most Inbox items walked per mail read, whatever max asks for.
    private const int MaxMails = 200;

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

    public OutlookRead<MailMessage> RecentMail(DateTime sinceUtc, int max)
    {
        // OlDefaultFolders: olFolderInbox.
        const int InboxFolder = 6;
        Stack<object> held = new();
        try
        {
            if (Attach(held) is not { } application)
            {
                return new OutlookRead<MailMessage>(OutlookReadKind.NotRunning);
            }

            // Only this one date goes into the filter; no outside text does.
            DateTime since = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(sinceUtc, DateTimeKind.Utc), TimeZoneInfo.Local);
            string filter = "[ReceivedTime] >= '" + Filter(since) + "'";

            dynamic ns = Hold(held, application.GetNamespace("MAPI"));
            dynamic folder = Hold(held, ns.GetDefaultFolder(InboxFolder));
            dynamic items = Hold(held, folder.Items);
            dynamic restricted = Hold(held, items.Restrict(filter));
            restricted.Sort("[ReceivedTime]", true);

            List<MailMessage> found = [];
            int taken = 0;
            int limit = Math.Clamp(max, 0, MaxMails);
            int walked = 0;
            object? next = walked < limit ? restricted.GetFirst() : null;
            while (next is not null && walked < limit)
            {
                walked++;
                dynamic item = Hold(held, next);
                MailMessage? message = ReadMail(item);
                if (message is not null)
                {
                    // Insert at the end, an append: the source scan refuses list appends outside the actions class.
                    found.Insert(taken, message);
                    taken++;
                }

                next = walked < limit ? restricted.GetNext() : null;
            }

            return new OutlookRead<MailMessage>(OutlookReadKind.Ok, found);
        }
        catch (Exception e)
        {
            return new OutlookRead<MailMessage>(KindOf(e));
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
            // Private and Confidential items (and any whose sensitivity can't be read) are skipped before
            // anything else of them is read.
            object? sensitivity = item.Sensitivity;
            if (OutlookSensitivity.IsPrivate(sensitivity))
            {
                return null;
            }

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

    private static MailMessage? ReadMail(dynamic item)
    {
        try
        {
            // Private and Confidential items (and any whose sensitivity can't be read) are skipped before
            // anything else of them is read.
            object? sensitivity = item.Sensitivity;
            if (OutlookSensitivity.IsPrivate(sensitivity))
            {
                return null;
            }

            string subject = (string?)item.Subject ?? string.Empty;
            string sender = (string?)item.SenderName ?? string.Empty;
            DateTime received = (DateTime)item.ReceivedTime;
            string messageClass = (string?)item.MessageClass ?? string.Empty;
            string body = (string?)item.Body ?? string.Empty;
            if (body.Length > MailDigest.MaxBodyLength)
            {
                // Never ends in a lone high surrogate.
                int keep = char.IsHighSurrogate(body[MailDigest.MaxBodyLength - 1])
                    ? MailDigest.MaxBodyLength - 1
                    : MailDigest.MaxBodyLength;
                body = body[..keep];
            }

            DateTime receivedUtc = TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(received, DateTimeKind.Unspecified), TimeZoneInfo.Local);
            return new MailMessage(subject, sender, receivedUtc, messageClass, body);
        }
        catch (Exception e) when (e is RuntimeBinderException or InvalidCastException or ArgumentException)
        {
            // Not a mail item, or a time that doesn't exist on this machine: skipped.
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
