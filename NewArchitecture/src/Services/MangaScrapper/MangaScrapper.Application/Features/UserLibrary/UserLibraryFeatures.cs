using FluentValidation;
using MangaScrapper.Application.Common.Abstractions;
using MangaScrapper.Domain.Aggregates;
using MangaScrapper.Domain.Repositories;
using MangaScrapper.Domain.ValueObjects;
using Mapster;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NovaStack.Contracts.Responses;
using NovaStack.SharedKernel.Results;

namespace MangaScrapper.Application.Features.UserLibrary;

// 1. AddOrUpdate
public record AddOrUpdateUserLibraryCommand(string UserId, Guid MangaId, string Status, bool IsFavorite) : ICommand<UserLibraryResponse>;

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
            //update the library
            existing.UpdateLibrary(command.Status, command.IsFavorite);
            await libraryRepository.UpdateAsync(existing, ct);
            return new UserLibraryResponse(existing.Id, existing.UserId, existing.MangaId.Value, existing.AddedAt, existing.UpdatedAt,existing.Status,existing.IsFavorite,null);
        }

        //create new
        var library = Domain.Aggregates.UserLibrary.Create(command.UserId, MangaId.From(command.MangaId),status:command.Status);
        await libraryRepository.AddAsync(library, ct);

        return new UserLibraryResponse(library.Id, library.UserId, library.MangaId.Value, library.AddedAt,library.UpdatedAt,library.Status,library.IsFavorite,null);
    }
}

// 2. GetUserLibrary
public record GetUserLibraryQuery(string UserId,string? Search,string? Type,string? Status, bool? IsFavorite,string SortBy = "UpdatedAt",string OrderBy="desc", int Page = 1, int PageSize = 10) : IQuery<PagedResponse<UserLibraryResponse>>;

internal sealed class GetUserLibraryQueryHandler(IUserLibraryRepository libraryRepository, IMangaRepository mangaRepository)
    : IQueryHandler<GetUserLibraryQuery, PagedResponse<UserLibraryResponse>>
{
    public async Task<Result<PagedResponse<UserLibraryResponse>>> Handle(GetUserLibraryQuery query, CancellationToken ct)
    {
        var paged = await libraryRepository.GetPagedByUserIdAsync(query.UserId,query.Search,query.Type,query.Status,query.IsFavorite,query.SortBy,query.OrderBy, query.Page, query.PageSize, ct);
        var mangaIds = paged.Items.Select(x => x.MangaId.Value).ToList();
        var mangas = await mangaRepository.GetByIdsAsync(mangaIds,ct);
        var mapped = paged.Items.Select(l => new UserLibraryResponse(l.Id, l.UserId, l.MangaId.Value, l.AddedAt,l.UpdatedAt,l.Status,l.IsFavorite, mangas.First(x=>x.Id.Equals(l.MangaId)).Adapt<MangaSummaryResponse>()));

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
        })
        .RequireAuthorization()
        .WithName("AddUserLibrary");

        group.MapGet("/", async (string userId, ISender sender, CancellationToken ct,string? search,string? type,string? status,bool? isFavorite,string sortBy="UpdatedAt",string orderBy="desc", int page = 1, int pageSize = 10) =>
        {
            var res = await sender.Send(new GetUserLibraryQuery(userId, search,type,status,isFavorite,sortBy, orderBy, page, pageSize), ct);
            return res.IsSuccess ? Results.Ok(ApiResponse.Ok(res.Value)) : res.Error.ToHttpResult();
        }).WithName("GetUserLibrary")
        .Produces<ApiResponse<PagedResponse<UserLibraryResponse>>>();

        group.MapDelete("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var res = await sender.Send(new RemoveUserLibraryCommand(id), ct);
            return res.IsSuccess ? Results.Ok(ApiResponse.Ok<object?>(null, "Removed")) : res.Error.ToHttpResult();
        })
        .RequireAuthorization()
        .WithName("RemoveUserLibrary");
    }
}
