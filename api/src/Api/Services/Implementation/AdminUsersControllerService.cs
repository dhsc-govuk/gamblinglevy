using Api.Dtos;
using Api.Services.Abstract;
using Application.Common.Interfaces.Queries;
using Domain.Users;
using LanguageExt;

namespace Api.Services.Implementation;

public class AdminUsersControllerService(
    IUserQueries userQueries) : IAdminUsersControllerService
{
    public async Task<IEnumerable<UserDto>> GetAll(CancellationToken cancellationToken)
    {
        var users = await userQueries.GetAllUsers(cancellationToken);

        return users.Select(UserDto.FromUserListItem);
    }

    public async Task<Option<UserDetailDto>> Get(Guid userId, CancellationToken cancellationToken)
    {
        var entityId = new UserId(userId);
        var detail = await userQueries.FindByIdWithDetails(entityId, cancellationToken);

        return detail.Match(
            item => UserDetailDto.FromDetailItem(item),
            () => Option<UserDetailDto>.None);
    }
}