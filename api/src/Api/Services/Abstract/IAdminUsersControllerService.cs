using Api.Dtos;
using LanguageExt;

namespace Api.Services.Abstract;

public interface IAdminUsersControllerService
{
    Task<IEnumerable<UserDto>> GetAll(CancellationToken cancellationToken);
    Task<Option<UserDetailDto>> Get(Guid userId, CancellationToken cancellationToken);
}