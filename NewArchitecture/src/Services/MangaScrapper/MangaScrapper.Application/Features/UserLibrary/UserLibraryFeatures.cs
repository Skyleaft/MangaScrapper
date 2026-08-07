using FluentValidation;
using MangaScrapper.Application.Common.Abstractions;
using MangaScrapper.Domain.Aggregates;
using MangaScrapper.Domain.Repositories;
using MangaScrapper.Domain.ValueObjects;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NovaStack.Contracts.Responses;
using NovaStack.SharedKernel.Results;

namespace MangaScrapper.Application.Features.UserLibrary;

// 1. AddOrUpdate
public record AddOrUpdateUserLibraryCommand(string UserId, Guid MangaId) : ICommand<UserLibraryResponse>;

public class AddOrUpdateUserLibraryCommandValidator : AbstractValidator<AddOrUpdateUserLibraryCommand>
{
    public AddOrUpdateUserLibraryCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().WithMessage("UserId is required.");
        RuleFor(x => x.MangaId).NotEmpty().WithMessage("MangaId is required.");
    }
}

internal sealed class AddOrUpdateUserLibraryCommandHandler(
    IUserLibraryRepository libraryRepository)
    : ICommandHandler<AddOrUpdateUserLibraryCommand, UserLibraryResponse>
{
    public async Task<Result<UserLibraryResponse>> Handle(AddOrUpdateUserLibraryCommand command, CancellationToken ct)
    {
        var existing = await libraryRepository.GetByUserIdAndMangaIdAsync(command.UserId, MangaId.From(command.MangaId), ct);
        if (existing is not null)
        {
            return new UserLibraryResponse(existing.Id, existing.UserId, existing.MangaId.Value, existing.AddedAt, null);
        }

        var library = Domain.Aggregates.UserLibrary.Create(command.UserId, MangaId.From(command.MangaId));
        await libraryRepository.AddAsync(library, ct);

        return new UserLibraryResponse(library.Id, library.UserId, library.MangaId.Value, library.AddedAt, null);
    }
}

// 2. GetUserLibrary
public record GetUserLibraryQuery(string UserId, int Page = 1, int PageSize = 10) : IQuery<PagedResponse<UserLibraryResponse>>;

internal sealed class GetUserLibraryQueryHandler(IUserLibraryRepository libraryRepository)
    : IQueryHandler<GetUserLibraryQuery, PagedResponse<UserLibraryResponse>>
{
    public async Task<Result<PagedResponse<UserLibraryResponse>>> Handle(GetUserLibraryQuery query, CancellationToken ct)
    {
        var paged = await libraryRepository.GetPagedByUserIdAsync(query.UserId, query.Page, query.PageSize, ct);
        var mapped = paged.Items.Select(l => new UserLibraryResponse(l.Id, l.UserId, l.MangaId.Value, l.AddedAt, null));

        return PagedResponse<UserLibraryResponse>.Create(mapped, paged.Page, paged.PageSize, paged.TotalCount);
    }
}

// 3. RemoveUserLibrary
public record RemoveUserLibraryCommand(Guid Id) : ICommand;

internal sealed class RemoveUserLibraryCommandHandler(IUserLibraryRepository libraryRepository)
    : ICommandHandler<RemoveUserLibraryCommand>
{
    public async Task<Result> Handle(RemoveUserLibraryCommand command, CancellationToken ct)
    {
        await libraryRepository.DeleteAsync(command.Id, ct);
        return Result.Success();
    }
}

// Endpoints
public sealed class UserLibraryEndpoints : IEndpointDefinition
{
    public void DefineEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/user-library").WithTags("UserLibrary");

        group.MapPost("/", async (AddOrUpdateUserLibraryCommand cmd, ISender sender, CancellationToken ct) =>
        {
            var res = await sender.Send(cmd, ct);
            return res.IsSuccess ? Results.Ok(ApiResponse.Ok(res.Value)) : res.Error.ToHttpResult();
        }).WithName("AddUserLibrary");

        group.MapGet("/{userId}", async (string userId, ISender sender, CancellationToken ct, int page = 1, int pageSize = 10) =>
        {
            var res = await sender.Send(new GetUserLibraryQuery(userId, page, pageSize), ct);
            return res.IsSuccess ? Results.Ok(ApiResponse.Ok(res.Value)) : res.Error.ToHttpResult();
        }).WithName("GetUserLibrary");

        group.MapDelete("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var res = await sender.Send(new RemoveUserLibraryCommand(id), ct);
            return res.IsSuccess ? Results.Ok(ApiResponse.Ok<object?>(null, "Removed")) : res.Error.ToHttpResult();
        }).WithName("RemoveUserLibrary");
    }
}
