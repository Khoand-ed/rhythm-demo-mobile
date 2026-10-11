using System.Security.Claims;
using Promuse.Api.Infrastructure;
using Promuse.Api.LiveOps;
using Promuse.Contracts.Mail;
using Empty = Promuse.Api.Events.EventEndpoints.Empty;

namespace Promuse.Api.Mailbox;

public static class MailEndpoints
{
    public static void MapMailEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/v1/mail")
            .WithTags("mail")
            .RequireAuthorization();

        group.MapGet("", BoxAsync);

        // 都要幂等 / Each hands something out or changes the box, so a retry after a lost answer
        // replays it rather than doing it again.
        group.MapPost("/{mailId:guid}/open", OpenAsync).AddEndpointFilter<IdempotencyFilter<Empty>>();
        group.MapPost("/collect-all", CollectAllAsync).AddEndpointFilter<IdempotencyFilter<Empty>>();
        group.MapPost("/delete-opened", DeleteOpenedAsync).AddEndpointFilter<IdempotencyFilter<Empty>>();

        RouteGroupBuilder admin = app.MapGroup("/v1/admin/mail")
            .WithTags("admin")
            .RequireAuthorization()
            .AddEndpointFilter<AdminOnly>();

        admin.MapGet("", AdminListAsync);
        admin.MapPost("", SendAsync);
        admin.MapPost("/{mailId:guid}/retract", RetractAsync);
    }

    private static async Task<IResult> BoxAsync(ClaimsPrincipal user, MailService mail, CancellationToken ct)
    {
        if (!user.TryGetAccountId(out Guid accountId)) return ApiProblems.Unauthorized().ToResult();
        return Results.Json(await mail.BoxAsync(accountId, ct));
    }

    private static Task<IResult> OpenAsync(Guid mailId, ClaimsPrincipal user, MailService mail, CancellationToken ct) =>
        ActAsync(user, accountId => mail.OpenAsync(accountId, mailId, ct));

    private static Task<IResult> CollectAllAsync(ClaimsPrincipal user, MailService mail, CancellationToken ct) =>
        ActAsync(user, accountId => mail.CollectAllAsync(accountId, ct));

    private static Task<IResult> DeleteOpenedAsync(ClaimsPrincipal user, MailService mail, CancellationToken ct) =>
        ActAsync(user, accountId => mail.DeleteOpenedAsync(accountId, ct));

    private static async Task<IResult> ActAsync(ClaimsPrincipal user, Func<Guid, Task<Outcome<MailActionResult>>> act)
    {
        if (!user.TryGetAccountId(out Guid accountId)) return ApiProblems.Unauthorized().ToResult();

        var outcome = await act(accountId);
        return outcome.IsSuccess
            ? Results.Json(outcome.Value, statusCode: StatusCodes.Status201Created)
            : outcome.Problem!.ToResult();
    }

    private static async Task<IResult> AdminListAsync(MailService mail, CancellationToken ct) =>
        Results.Json(await mail.ListAsync(ct));

    private static async Task<IResult> SendAsync(MailSendRequest request, ClaimsPrincipal user, MailService mail, CancellationToken ct)
    {
        if (!user.TryGetAccountId(out Guid adminId)) return ApiProblems.Unauthorized().ToResult();

        var outcome = await mail.SendAsync(adminId, request, ct);
        return outcome.IsSuccess
            ? Results.Json(outcome.Value, statusCode: StatusCodes.Status201Created)
            : outcome.Problem!.ToResult();
    }

    private static async Task<IResult> RetractAsync(Guid mailId, MailService mail, CancellationToken ct)
    {
        var outcome = await mail.RetractAsync(mailId, ct);
        return outcome.IsSuccess ? Results.NoContent() : outcome.Problem!.ToResult();
    }
}
