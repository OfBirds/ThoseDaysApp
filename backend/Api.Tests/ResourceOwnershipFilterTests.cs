using System.Security.Claims;
using Api.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Api.Tests;

/// <summary>
/// ResourceOwnershipFilter — a token for user A must not reach user B's data. Verifies the
/// 403 on a route/userId mismatch, the pass-through when they match, and that anonymous or
/// userId-less actions are unaffected.
/// </summary>
public class ResourceOwnershipFilterTests
{
    private static ClaimsPrincipal Authenticated(Guid sub)
    {
        var identity = new ClaimsIdentity(
            [new Claim(JwtRegisteredClaimNames.Sub, sub.ToString())], authenticationType: "Bearer");
        return new ClaimsPrincipal(identity);
    }

    private static (ActionExecutingContext ctx, ActionExecutionDelegate next, Func<bool> nextCalled) Context(
        ClaimsPrincipal user, string? routeUserId)
    {
        var httpContext = new DefaultHttpContext { User = user };
        var routeData = new RouteData();
        if (routeUserId is not null) routeData.Values["userId"] = routeUserId;

        var actionContext = new ActionContext(httpContext, routeData, new ActionDescriptor());
        var ctx = new ActionExecutingContext(
            actionContext, new List<IFilterMetadata>(), new Dictionary<string, object?>(), controller: null!);

        var called = false;
        ActionExecutionDelegate next = () =>
        {
            called = true;
            return Task.FromResult(new ActionExecutedContext(
                actionContext, new List<IFilterMetadata>(), controller: null!));
        };
        return (ctx, next, () => called);
    }

    [Fact]
    public async Task MismatchedUserId_IsForbidden_AndShortCircuits()
    {
        var (ctx, next, nextCalled) = Context(Authenticated(Guid.NewGuid()), routeUserId: Guid.NewGuid().ToString());

        await new ResourceOwnershipFilter().OnActionExecutionAsync(ctx, next);

        Assert.IsType<ForbidResult>(ctx.Result);
        Assert.False(nextCalled());
    }

    [Fact]
    public async Task MatchingUserId_PassesThrough()
    {
        var id = Guid.NewGuid();
        var (ctx, next, nextCalled) = Context(Authenticated(id), routeUserId: id.ToString());

        await new ResourceOwnershipFilter().OnActionExecutionAsync(ctx, next);

        Assert.Null(ctx.Result);
        Assert.True(nextCalled());
    }

    [Fact]
    public async Task NoUserIdInRoute_PassesThrough()
    {
        var (ctx, next, nextCalled) = Context(Authenticated(Guid.NewGuid()), routeUserId: null);

        await new ResourceOwnershipFilter().OnActionExecutionAsync(ctx, next);

        Assert.Null(ctx.Result);
        Assert.True(nextCalled());
    }

    [Fact]
    public async Task AnonymousPrincipal_IsNotBlocked()
    {
        var anon = new ClaimsPrincipal(new ClaimsIdentity()); // not authenticated
        var (ctx, next, nextCalled) = Context(anon, routeUserId: Guid.NewGuid().ToString());

        await new ResourceOwnershipFilter().OnActionExecutionAsync(ctx, next);

        Assert.Null(ctx.Result);
        Assert.True(nextCalled());
    }
}
