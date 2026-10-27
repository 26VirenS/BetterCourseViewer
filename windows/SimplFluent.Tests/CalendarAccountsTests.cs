using System.Text;
using System.Text.Json.Nodes;
using Simpl.Support;
using Xunit;

namespace Simpl.Tests;

/// <summary>(0.3.81) The calendar accounts (Support/CalendarProviders.cs, CalendarAccounts.cs): iCalendar read and written,
/// CalDAV's answers, Google's and Outlook's events in the page's shape and as each takes Simpl's, and the sign-in's parts.</summary>
public class CalendarAccountsTests
{
    private static double Ms(int y, int mo, int d, int h = 0, int mi = 0) =>
        PulledEvent.Ms(new DateTime(y, mo, d, h, mi, 0, DateTimeKind.Utc));

    // ---- iCalendar ----

    [Fact]
    public void AnICalendarsEventsAreRead()
    {
        const string ics = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\n"
            + "BEGIN:VEVENT\r\nUID:one@example\r\nDTSTART:20261027T150000Z\r\nDTEND:20261027T160000Z\r\nSUMMARY:Study group\\, room 2\r\n"
            + "LOCATION:Library\\; second floor\r\nTRANSP:TRANSPARENT\r\nBEGIN:VALARM\r\nTRIGGER:-PT10M\r\nDESCRIPTION:Not this\r\nEND:VALARM\r\nEND:VEVENT\r\n"
            + "BEGIN:VEVENT\r\nUID:two@example\r\nDTSTART;VALUE=DATE:20261030\r\nDTEND;VALUE=DATE:20261101\r\nSUMMARY:Fall break\r\nEND:VEVENT\r\n"
            + "BEGIN:VEVENT\r\nUID:three@example\r\nDTSTART:20261028T090000Z\r\nDURATION:PT1H30M\r\nSUMMARY:Lab\r\nEND:VEVENT\r\n"
            + "BEGIN:VEVENT\r\nUID:gone@example\r\nDTSTART:20261029T090000Z\r\nSTATUS:CANCELLED\r\nSUMMARY:Called off\r\nEND:VEVENT\r\n"
            + "END:VCALENDAR\r\n";
        var all = Ics.Events(ics);
        Assert.Equal(3, all.Count);

        var a = all[0];
        Assert.Equal("Study group, room 2", a.Title);
        Assert.Equal("Library; second floor", a.Location);
        Assert.True(a.Free);
        Assert.False(a.AllDay);
        Assert.Equal(Ms(2026, 10, 27, 15), a.Start);
        Assert.Equal(Ms(2026, 10, 27, 16), a.End);
        Assert.StartsWith("one@example|", a.Id);

        var b = all[1];
        Assert.True(b.AllDay);
        Assert.Equal("2026-10-30", b.Day);
        Assert.Equal(2, b.Days);
        Assert.Equal(TimeSpan.FromDays(2).TotalMilliseconds, b.End - b.Start);

        var c = all[2];
        Assert.Equal("Lab", c.Title);
        Assert.Equal(Ms(2026, 10, 28, 10, 30), c.End);
    }

    [Fact]
    public void FoldedLinesAndQuotedZonesAreRead()
    {
        const string ics = "BEGIN:VEVENT\nUID:x\nDTSTART;TZID=\"America/New_York\":20260115T090000\nDTEND;TZID=\"America/New_York\":20260115T100000\n"
            + "SUMMARY:A long\n  title\nEND:VEVENT\n";
        var e = Assert.Single(Ics.Events(ics));
        Assert.Equal("A long title", e.Title);
        // (9 in New York in January is 14:00 UTC)
        Assert.Equal(Ms(2026, 1, 15, 14), e.Start);
        Assert.Equal(Ms(2026, 1, 15, 15), e.End);
    }

    [Fact]
    public void SimplsEventIsWrittenAsICalendarAndReadsBack()
    {
        var e = new PushEvent
        {
            Uid = "simpl-work-42", Title = "Essay, draft; final", Start = Ms(2026, 11, 3, 23, 59), End = Ms(2026, 11, 3, 23, 59),
            Description = "Line one\nLine two", Url = "https://school.example/courses/1/assignments/42", Alarm = 30,
            Rrule = "FREQ=WEEKLY;INTERVAL=2",
        };
        var text = Ics.Calendar(e, new DateTime(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc));
        Assert.NotNull(text);
        Assert.Contains("DTSTART:20261103T235900Z", text);
        // (a due time is given a quarter of an hour: a calendar takes no event of no length)
        Assert.Contains("DTEND:20261104T001400Z", text);
        Assert.Contains("SUMMARY:Essay\\, draft\\; final", text);
        Assert.Contains("DESCRIPTION:Line one\\nLine two", text);
        Assert.Contains("RRULE:FREQ=WEEKLY;INTERVAL=2", text);
        Assert.Contains("TRIGGER:-PT30M", text);
        Assert.Contains("TRANSP:OPAQUE", text);
        var back = Assert.Single(Ics.Events(text!));
        Assert.Equal("Essay, draft; final", back.Title);
        Assert.Equal(e.Start, back.Start);
    }

    [Fact]
    public void AWholeDayIsWrittenByItsDates()
    {
        var e = new PushEvent { Uid = "plan-1", Title = "Read chapter 4", AllDay = true, Day = "2026-11-05", Days = 2, Free = true };
        var text = Ics.Calendar(e, DateTime.UtcNow)!;
        Assert.Contains("DTSTART;VALUE=DATE:20261105", text);
        Assert.Contains("DTEND;VALUE=DATE:20261107", text);
        Assert.Contains("TRANSP:TRANSPARENT", text);
        Assert.Null(Ics.Calendar(new PushEvent { Uid = "none", Title = "No time" }, DateTime.UtcNow));
    }

    [Fact]
    public void LongLinesAreFoldedNeverInsideACharacter()
    {
        var sb = new StringBuilder();
        var line = "SUMMARY:" + string.Concat(Enumerable.Repeat("Study 📚 ", 20));
        Ics.Line(sb, line);
        var lines = sb.ToString().Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length > 1);
        Assert.All(lines, l => Assert.True(Encoding.UTF8.GetByteCount(l) <= 75, l));
        Assert.All(lines, l => Assert.DoesNotContain('�', Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(l))));
        // (unfolded, it is the line it was)
        var unfolded = string.Concat(lines.Select((l, i) => i == 0 ? l : l[1..]));
        Assert.Equal(line, unfolded);
    }

    [Fact]
    public void AnEventsFileIsNamedTheSameEachTime()
    {
        Assert.Equal(Ics.FileName("a"), Ics.FileName("a"));
        Assert.NotEqual(Ics.FileName("a"), Ics.FileName("b"));
        Assert.Matches("^simpl-[0-9a-f]{24}\\.ics$", Ics.FileName("simpl-work-42"));
    }

    // ---- CalDAV ----

    [Fact]
    public void CalDavsCalendarsAreReadLeavingOutTheInboxAndReminders()
    {
        const string xml = """
            <d:multistatus xmlns:d="DAV:" xmlns:c="urn:ietf:params:xml:ns:caldav" xmlns:a="http://apple.com/ns/ical/">
              <d:response><d:href>/123/calendars/</d:href><d:propstat><d:prop><d:resourcetype><d:collection/></d:resourcetype></d:prop></d:propstat></d:response>
              <d:response><d:href>/123/calendars/home/</d:href><d:propstat><d:prop>
                <d:resourcetype><d:collection/><c:calendar/></d:resourcetype><d:displayname>Home</d:displayname>
                <a:calendar-color>#34C759FF</a:calendar-color>
                <c:supported-calendar-component-set><c:comp name="VEVENT"/></c:supported-calendar-component-set></d:prop></d:propstat></d:response>
              <d:response><d:href>/123/calendars/tasks/</d:href><d:propstat><d:prop>
                <d:resourcetype><d:collection/><c:calendar/></d:resourcetype><d:displayname>Reminders</d:displayname>
                <c:supported-calendar-component-set><c:comp name="VTODO"/></c:supported-calendar-component-set></d:prop></d:propstat></d:response>
              <d:response><d:href>/123/calendars/inbox/</d:href><d:propstat><d:prop>
                <d:resourcetype><d:collection/><c:schedule-inbox/></d:resourcetype></d:prop></d:propstat></d:response>
            </d:multistatus>
            """;
        var all = CalDav.Calendars(xml, new Uri("https://p42-caldav.icloud.com/"));
        var home = Assert.Single(all);
        Assert.Equal("Home", home.Name);
        Assert.Equal("#34c759", home.Color);
        Assert.Equal("https://p42-caldav.icloud.com/123/calendars/home/", home.Id);
    }

    [Fact]
    public void CalDavsHrefsAndEventsAreFound()
    {
        const string principal = """<d:multistatus xmlns:d="DAV:"><d:response><d:href>/</d:href><d:propstat><d:prop><d:current-user-principal><d:href>/123/principal/</d:href></d:current-user-principal></d:prop></d:propstat></d:response></d:multistatus>""";
        Assert.Equal("/123/principal/", CalDav.Href(principal, CalDav.D + "current-user-principal"));
        Assert.Null(CalDav.Href("not xml", CalDav.D + "current-user-principal"));
        const string events = """<d:multistatus xmlns:d="DAV:" xmlns:c="urn:ietf:params:xml:ns:caldav"><d:response><d:propstat><d:prop><c:calendar-data>BEGIN:VCALENDAR</c:calendar-data></d:prop></d:propstat></d:response></d:multistatus>""";
        Assert.Equal("BEGIN:VCALENDAR", Assert.Single(CalDav.CalendarData(events)));
        var ask = CalDav.EventsAsk(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.Contains("start=\"20261001T000000Z\"", ask);
        Assert.Contains("end=\"20261101T000000Z\"", ask);
    }

    // ---- Google and Outlook ----

    [Fact]
    public void GooglesEventsAreRead()
    {
        var timed = JsonNode.Parse("""{"id":"g1","summary":"Office hours","location":"Room 5","start":{"dateTime":"2026-10-27T10:00:00-04:00"},"end":{"dateTime":"2026-10-27T11:00:00-04:00"}}""")!.AsObject();
        var e = GoogleCalendar.Pulled(timed)!;
        Assert.Equal(Ms(2026, 10, 27, 14), e.Start);
        Assert.Equal(Ms(2026, 10, 27, 15), e.End);
        Assert.Equal("Room 5", e.Location);
        Assert.False(e.Free);

        var day = JsonNode.Parse("""{"id":"g2","summary":"Trip","transparency":"transparent","start":{"date":"2026-11-02"},"end":{"date":"2026-11-05"}}""")!.AsObject();
        var d = GoogleCalendar.Pulled(day)!;
        Assert.True(d.AllDay);
        Assert.Equal("2026-11-02", d.Day);
        Assert.Equal(3, d.Days);
        Assert.True(d.Free);

        Assert.Null(GoogleCalendar.Pulled(JsonNode.Parse("""{"id":"g3","status":"cancelled","start":{"date":"2026-11-02"},"end":{"date":"2026-11-03"}}""")!.AsObject()));
        Assert.Equal("Busy", GoogleCalendar.Pulled(JsonNode.Parse("""{"id":"g4","start":{"dateTime":"2026-10-27T10:00:00Z"},"end":{"dateTime":"2026-10-27T11:00:00Z"}}""")!.AsObject())!.Title);
    }

    [Fact]
    public void SimplsEventIsWrittenAsGoogleTakesIt()
    {
        var b = GoogleCalendar.Body(new PushEvent { Uid = "u1", Title = "Quiz 3", Start = Ms(2026, 10, 30, 13), End = Ms(2026, 10, 30, 14), Alarm = 15, Rrule = "FREQ=DAILY" })!;
        Assert.Equal("2026-10-30T13:00:00Z", b["start"]!["dateTime"]!.GetValue<string>());
        Assert.Equal("2026-10-30T14:00:00Z", b["end"]!["dateTime"]!.GetValue<string>());
        Assert.Equal("u1", b["extendedProperties"]!["private"]!["simplUid"]!.GetValue<string>());
        Assert.Equal("RRULE:FREQ=DAILY", b["recurrence"]![0]!.GetValue<string>());
        Assert.Equal(15, b["reminders"]!["overrides"]![0]!["minutes"]!.GetValue<int>());

        var day = GoogleCalendar.Body(new PushEvent { Uid = "u2", Title = "Plan", AllDay = true, Day = "2026-11-05" })!;
        Assert.Equal("2026-11-05", day["start"]!["date"]!.GetValue<string>());
        Assert.Equal("2026-11-06", day["end"]!["date"]!.GetValue<string>());
    }

    [Fact]
    public void OutlooksEventsAreRead()
    {
        var timed = JsonNode.Parse("""{"id":"o1","subject":"Advising","showAs":"free","start":{"dateTime":"2026-10-27T14:00:00.0000000","timeZone":"UTC"},"end":{"dateTime":"2026-10-27T14:30:00.0000000","timeZone":"UTC"},"location":{"displayName":"Zoom"}}""")!.AsObject();
        var e = OutlookCalendar.Pulled(timed)!;
        Assert.Equal(Ms(2026, 10, 27, 14), e.Start);
        Assert.Equal(Ms(2026, 10, 27, 14, 30), e.End);
        Assert.True(e.Free);
        Assert.Equal("Zoom", e.Location);

        var day = JsonNode.Parse("""{"id":"o2","subject":"Holiday","isAllDay":true,"start":{"dateTime":"2026-11-26T00:00:00.0000000"},"end":{"dateTime":"2026-11-28T00:00:00.0000000"}}""")!.AsObject();
        var d = OutlookCalendar.Pulled(day)!;
        Assert.True(d.AllDay);
        Assert.Equal("2026-11-26", d.Day);
        Assert.Equal(2, d.Days);

        Assert.Null(OutlookCalendar.Pulled(JsonNode.Parse("""{"id":"o3","isCancelled":true,"start":{"dateTime":"2026-11-26T00:00:00"},"end":{"dateTime":"2026-11-27T00:00:00"}}""")!.AsObject()));
    }

    [Fact]
    public void SimplsEventIsWrittenAsOutlookTakesIt()
    {
        var b = OutlookCalendar.Body(new PushEvent { Uid = "u1", Title = "Lab report", Start = Ms(2026, 10, 30, 13), End = Ms(2026, 10, 30, 15), Free = true })!;
        Assert.Equal("2026-10-30T13:00:00", b["start"]!["dateTime"]!.GetValue<string>());
        Assert.Equal("UTC", b["start"]!["timeZone"]!.GetValue<string>());
        Assert.Equal("free", b["showAs"]!.GetValue<string>());
        Assert.False(b["isReminderOn"]!.GetValue<bool>());

        var weekly = OutlookCalendar.Body(new PushEvent { Uid = "u2", Title = "Plan", AllDay = true, Day = "2026-11-04", Rrule = "FREQ=WEEKLY;INTERVAL=1" })!;
        Assert.True(weekly["isAllDay"]!.GetValue<bool>());
        Assert.Equal("weekly", weekly["recurrence"]!["pattern"]!["type"]!.GetValue<string>());
        Assert.Equal("wednesday", weekly["recurrence"]!["pattern"]!["daysOfWeek"]![0]!.GetValue<string>());
    }

    // ---- Simpl's events ----

    [Fact]
    public void AnEventsSignatureChangesWithWhatItSays()
    {
        var a = new PushEvent { Uid = "x", Title = "Essay", Start = 1000 };
        var b = new PushEvent { Uid = "x", Title = "Essay", Start = 1000 };
        Assert.Equal(a.Signature, b.Signature);
        b.Title = "Essay (late)";
        Assert.NotEqual(a.Signature, b.Signature);
        b.Title = "Essay";
        b.Alarm = 10;
        Assert.NotEqual(a.Signature, b.Signature);
    }

    [Fact]
    public void RepeatsAreReadFromTheRule()
    {
        Assert.Equal(("WEEKLY", 2), new PushEvent { Rrule = "FREQ=WEEKLY;INTERVAL=2;BYDAY=MO" }.Repeats);
        Assert.Equal(("DAILY", 1), new PushEvent { Rrule = "freq=daily" }.Repeats);
        Assert.Null(new PushEvent { Rrule = "FREQ=YEARLY" }.Repeats);
        Assert.Null(new PushEvent().Repeats);
    }

    [Fact]
    public void AShownCalendarsLinkIdIsShortAndTheSameEachTime()
    {
        var id = CalendarAccounts.LinkId("account-1", "calendar-a");
        Assert.Equal(id, CalendarAccounts.LinkId("account-1", "calendar-a"));
        Assert.NotEqual(id, CalendarAccounts.LinkId("account-1", "calendar-b"));
        Assert.Matches("^a[0-9a-f]{16}$", id);
    }

    // ---- signing in ----

    [Fact]
    public void PkcesChallengeIsTheVerifiersDigest()
    {
        Assert.Equal("AOyxDBa1JPgzkaZGVWI3r2VXyB7pOaOHNi-XJGN199E", Pkce.Challenge("dBjftJeZ4CVP-mo4N0xr3Bsnsj0iN3WwFZTGqqw-u0t"));
        var v = Pkce.Verifier();
        Assert.Equal(64, v.Length);
        Assert.Matches("^[A-Za-z0-9_-]+$", v);
        Assert.NotEqual(v, Pkce.Verifier());
    }

    [Fact]
    public void WhoSignedInIsReadFromTheIdToken()
    {
        Assert.Equal("student@example.edu", OAuthTokens.Identity("x.eyJlbWFpbCI6ICJzdHVkZW50QGV4YW1wbGUuZWR1IiwgInN1YiI6ICIxIn0.sig"));
        Assert.Null(OAuthTokens.Identity("garbage"));
        Assert.Null(OAuthTokens.Identity("a.!!!.b"));
    }

    [Fact]
    public void IcloudIsOfferedAndTheOthersWaitForTheirSignInClients()
    {
        Assert.True(CalendarKinds.Available(CalendarKinds.ICloud));
        Assert.Equal(CalendarKeys.Google.Length > 0, CalendarKinds.Available(CalendarKinds.Google));
        Assert.Equal(CalendarKeys.Microsoft.Length > 0, CalendarKinds.Available(CalendarKinds.Microsoft));
        Assert.All(CalendarKinds.All, k => Assert.True(CalendarKinds.Action(k).Length <= 30));
    }
}
