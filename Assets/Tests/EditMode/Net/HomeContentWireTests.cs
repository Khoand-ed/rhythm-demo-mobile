using NUnit.Framework;
using Promuse.Contracts.Mail;
using Promuse.Contracts.Notices;
using Promuse.Contracts.SignIn;
using Promuse.Net;

// 邮件/公告/签到的线上格式 / The mailbox, notice board and sign-in replies in the shape the server writes
// them (camelCase, enums by name, nulls written out), read through the client's own parser.
public class HomeContentWireTests
{
    private const string Box =
        @"{""mails"":[{""mailId"":""6f1c2b8e-3d4a-4f5b-9c6d-7e8f9a0b1c2d"",""title"":""Greetings"",""body"":""Doctor,\nthank you."",""sender"":""Closure"",""senderCharacterId"":""AMIYA"",""attachments"":[{""itemId"":1,""amount"":300}],""sentAt"":""2026-10-11T08:00:00+00:00"",""expiresAt"":""2026-11-10T08:00:00+00:00"",""isOpened"":false}],""unopened"":1,""capacity"":100,""serverTime"":""2026-10-11T09:00:00+00:00""}";

    private const string Notices =
        @"{""notices"":[{""noticeId"":""0a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d"",""category"":""System"",""listTitle"":""Maintenance"",""title"":""Maintenance on Oct 14"",""body"":""<b>When:</b> 04:00"",""bannerUrl"":null,""startsAt"":""2026-10-11T00:00:00+00:00"",""endsAt"":null,""sortOrder"":5,""isRead"":false}],""serverTime"":""2026-10-11T09:00:00+00:00""}";

    private const string Month =
        @"{""monthKey"":""2026-10"",""year"":2026,""month"":10,""rewards"":[{""itemId"":2,""amount"":2000},{""itemId"":11,""amount"":5}],""signed"":1,""canSignToday"":false,""nextDayAt"":""2026-10-11T21:00:00+00:00"",""serverTime"":""2026-10-11T09:00:00+00:00""}";

    [Test]
    public void AMailbox_ReadsWithItsAttachments()
    {
        MailBox box = PromuseApi.TryParse<MailBox>(Box);

        Assert.IsNotNull(box);
        Assert.AreEqual(1, box.Unopened);
        Assert.AreEqual(100, box.Capacity);
        Assert.AreEqual("AMIYA", box.Mails[0].SenderCharacterId);
        Assert.AreEqual(300, box.Mails[0].Attachments[0].Amount);
        Assert.IsFalse(box.Mails[0].IsOpened);
    }

    [Test]
    public void ANotice_ReadsItsCategoryByName_AndNoBanner()
    {
        NoticeList list = PromuseApi.TryParse<NoticeList>(Notices);

        Assert.IsNotNull(list);
        Assert.AreEqual(NoticeCategory.System, list.Notices[0].Category, "enums arrive by name");
        Assert.IsNull(list.Notices[0].BannerUrl);
        Assert.IsNull(list.Notices[0].EndsAt);
    }

    [Test]
    public void TheMonth_ReadsItsSquaresAndState()
    {
        SignInMonth month = PromuseApi.TryParse<SignInMonth>(Month);

        Assert.IsNotNull(month);
        Assert.AreEqual(("2026-10", 2, 1), (month.MonthKey, month.Rewards.Count, month.Signed));
        Assert.IsFalse(month.CanSignToday);
    }
}
